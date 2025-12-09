namespace CSE325_visioncoders.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CSE325_visioncoders.Models;
using MongoDB.Bson;
using MongoDB.Driver;


public class OrderRow
{
    public string Id { get; set; } = default!;
    public DateTime DeliveryDateUtc { get; set; } // Día de entrega (llave UTC 00:00)
    public string CustomerId { get; set; } = default!;
    public string CustomerName { get; set; } = "";
    public string MealId { get; set; } = default!;
    public string MealName { get; set; } = "";
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class OrderGroupRow
{
    public DateTime DateLocal { get; set; } // Día local (fecha)
    public string MealId { get; set; } = default!;
    public string MealName { get; set; } = "";
    public int Total { get; set; }
    public int Canceled { get; set; }
    public int InProcess { get; set; }
    public int Ready { get; set; }
    public int Delivered { get; set; }
}

public interface IOrderService
{
    // CRUD básico
    Task<List<Order>> GetAsync();
    Task<Order?> GetByIdAsync(string id);
    Task CreateAsync(Order order);
    Task UpdateAsync(Order order);
    Task DeleteAsync(string id);

    // Ventana por CreatedAt (legacy, usada en otras pantallas)
    Task<List<Order>> GetByLocalWindowAsync(DateTime localStart, DateTime localEnd, string timeZoneId);

    // Usado por Customers tab (legacy)
    Task<List<Order>> GetOrdersByCustomerIdAsync(string customerId);

    // Nuevas vistas para /cook/orders
    Task<List<OrderRow>> GetCookOrdersExpandedAsync(
        string cookId,
        DateTime fromLocal,
        DateTime toLocal,
        string tzId,
        DateTime? filterDateLocal = null,
        string? filterMealId = null);

    Task<List<OrderGroupRow>> GetCookOrdersGroupedAsync(
        string cookId,
        string mealId,
        DateTime fromLocal,
        DateTime toLocal,
        string tzId);

    Task UpdateStatusAsync(string orderId, OrderStatus newStatus);
}

public class OrderService : IOrderService
{
    private readonly IMongoDatabase _db;
    private readonly IMongoCollection<Order> _orders;

    public OrderService(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MongoDb");
        var client = new MongoClient(connectionString);
        _db = client.GetDatabase("lunchmate");
        _orders = _db.GetCollection<Order>("orders");
    }

    // CRUD básico
    public async Task<List<Order>> GetAsync() =>
        await _orders.Find(_ => true)
                     .SortByDescending(o => o.CreatedAt)
                     .ToListAsync();

    public async Task<Order?> GetByIdAsync(string id) =>
        await _orders.Find(o => o.Id == id).FirstOrDefaultAsync();

    public async Task CreateAsync(Order order) =>
        await _orders.InsertOneAsync(order);

    public async Task UpdateAsync(Order order) =>
        await _orders.ReplaceOneAsync(o => o.Id == order.Id, order);

    public async Task DeleteAsync(string id) =>
        await _orders.DeleteOneAsync(o => o.Id == id);

    // Ventana por CreatedAt (legacy)
    public async Task<List<Order>> GetByLocalWindowAsync(DateTime localStart, DateTime localEnd, string timeZoneId)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, tz);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, tz);

        var filter = Builders<Order>.Filter.Gte(o => o.CreatedAt, startUtc)
                   & Builders<Order>.Filter.Lt(o => o.CreatedAt, endUtc);

        return await _orders.Find(filter)
                            .SortBy(o => o.CreatedAt)
                            .ToListAsync();
    }

    // FINAL METHOD USED BY CUSTOMERS TAB (legacy)
    public async Task<List<Order>> GetOrdersByCustomerIdAsync(string customerId)
    {
        var customerFilter = Builders<Order>.Filter.Eq(o => o.CustomerId, customerId);

        var statusFilter = Builders<Order>.Filter.In(o => o.Status, new[]
        {
            OrderStatus.Delivered,
            OrderStatus.Cancelled
        });

        var filter = Builders<Order>.Filter.And(customerFilter, statusFilter);

        return await _orders.Find(filter)
                            .SortByDescending(o => o.CreatedAt)
                            .ToListAsync();
    }

    // NUEVO: vista detalle para /cook/orders
    public async Task<List<OrderRow>> GetCookOrdersExpandedAsync(
        string cookId,
        DateTime fromLocal,
        DateTime toLocal,
        string tzId,
        DateTime? filterDateLocal = null,
        string? filterMealId = null)
    {
        var ordersCol = _db.GetCollection<Order>("orders");
        var mealsCol = _db.GetCollection<Meal>("meals");
        var usersCol = _db.GetCollection<BsonDocument>("users");

        DateTime UtcKey(DateTime d) => new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);

        var fromUtc = UtcKey(fromLocal);
        var toUtc = UtcKey(toLocal);

        var filter = Builders<Order>.Filter.Eq(o => o.CookId, cookId) &
                     Builders<Order>.Filter.Gte(o => o.DeliveryDateUtc, fromUtc) &
                     Builders<Order>.Filter.Lt(o => o.DeliveryDateUtc, toUtc);

        if (filterDateLocal.HasValue)
            filter &= Builders<Order>.Filter.Eq(o => o.DeliveryDateUtc, UtcKey(filterDateLocal.Value));

        if (!string.IsNullOrWhiteSpace(filterMealId))
            filter &= Builders<Order>.Filter.Eq(o => o.MealId, filterMealId);

        var orders = await ordersCol.Find(filter).SortBy(o => o.DeliveryDateUtc).ToListAsync();
        if (orders.Count == 0) return new List<OrderRow>();

        // Join manual para nombres
        var mealIds = orders.Select(o => o.MealId).Distinct().ToList();
        var custIds = orders.Select(o => o.CustomerId).Distinct().ToList();

        var meals = await mealsCol.Find(m => mealIds.Contains(m.Id!)).ToListAsync();
        var mealById = meals.ToDictionary(m => m.Id!, m => m.Name);

        var custObjIds = custIds.Where(id => ObjectId.TryParse(id, out _)).Select(ObjectId.Parse).ToList();
        var userProj = Builders<BsonDocument>.Projection.Include("_id").Include("name");
        var userDocs = custObjIds.Count == 0
            ? new List<BsonDocument>()
            : await usersCol.Find(Builders<BsonDocument>.Filter.In("_id", custObjIds))
                            .Project(userProj)
                            .ToListAsync();

        var nameByUserId = userDocs.ToDictionary(
            d => d["_id"].AsObjectId.ToString(),
            d => d.TryGetValue("name", out var n) ? (n?.AsString ?? "Customer") : "Customer"
        );

        return orders.Select(o => new OrderRow
        {
            Id = o.Id!,
            DeliveryDateUtc = o.DeliveryDateUtc,
            CustomerId = o.CustomerId,
            CustomerName = nameByUserId.TryGetValue(o.CustomerId, out var nm) ? nm : o.CustomerId,
            MealId = o.MealId,
            MealName = mealById.TryGetValue(o.MealId, out var mn) ? mn : o.MealId,
            Status = o.Status,
            CreatedAt = o.CreatedAt
        }).ToList();
    }

    // NUEVO: vista agrupada por fecha para un meal
    public async Task<List<OrderGroupRow>> GetCookOrdersGroupedAsync(
        string cookId,
        string mealId,
        DateTime fromLocal,
        DateTime toLocal,
        string tzId)
    {
        var ordersCol = _db.GetCollection<Order>("orders");
        var mealsCol = _db.GetCollection<Meal>("meals");

        DateTime UtcKey(DateTime d) => new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc);
        var fromUtc = UtcKey(fromLocal);
        var toUtc = UtcKey(toLocal);

        var filter = Builders<Order>.Filter.Eq(o => o.CookId, cookId) &
                     Builders<Order>.Filter.Eq(o => o.MealId, mealId) &
                     Builders<Order>.Filter.Gte(o => o.DeliveryDateUtc, fromUtc) &
                     Builders<Order>.Filter.Lt(o => o.DeliveryDateUtc, toUtc);

        var orders = await ordersCol.Find(filter).ToListAsync();
        if (orders.Count == 0) return new List<OrderGroupRow>();

        var mealName = (await mealsCol.Find(m => m.Id == mealId).FirstOrDefaultAsync())?.Name ?? "(meal)";
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);

        var groups = orders.GroupBy(o => o.DeliveryDateUtc)
            .Select(g =>
            {
                int canceled = g.Count(x => x.Status == OrderStatus.Cancelled);
                int delivered = g.Count(x => x.Status == OrderStatus.Delivered);
                int ready = g.Count(x => x.Status == OrderStatus.Ready);
                int inproc = g.Count(x => x.Status == OrderStatus.Pending);

                return new OrderGroupRow
                {
                    DateLocal = TimeZoneInfo.ConvertTimeFromUtc(g.Key, tz).Date,
                    MealId = mealId,
                    MealName = mealName,
                    Total = g.Count(),
                    Canceled = canceled,
                    Delivered = delivered,
                    Ready = ready,
                    InProcess = inproc
                };
            })
            .OrderBy(r => r.DateLocal)
            .ToList();

        return groups;
    }

    // NUEVO: actualizar estado por parte del cook
    public async Task UpdateStatusAsync(string orderId, OrderStatus newStatus)
    {
        var ordersCol = _db.GetCollection<Order>("orders");
        var update = Builders<Order>.Update
            .Set(o => o.Status, newStatus)
            .Set(o => o.UpdatedAt, DateTime.UtcNow);

        await ordersCol.UpdateOneAsync(o => o.Id == orderId, update);
    }
}