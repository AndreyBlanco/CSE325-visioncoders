namespace CSE325_visioncoders.Services;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CSE325_visioncoders.Models;
using MongoDB.Driver;

public interface IOrderService
{
    Task<List<Order>> GetAsync();
    Task<Order?> GetByIdAsync(string id);
    Task CreateAsync(Order order);
    Task UpdateAsync(Order order);
    Task DeleteAsync(string id);

    public class OrderRow
    {
        public string Id { get; set; } = default!;
        public DateTime DeliveryDateUtc { get; set; }
        public string CustomerId { get; set; } = default!;
        public string CustomerName { get; set; } = "";  // ← nombre del customer
        public string MealId { get; set; } = default!;
        public string MealName { get; set; } = "";      // ← nombre del meal
        public OrderStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // DTO para la tabla agrupada por fecha para un meal
    public class OrderGroupRow
    {
        public DateTime DateLocal { get; set; }     // fecha local (día)
        public string MealId { get; set; } = default!;
        public string MealName { get; set; } = "";
        public int Total { get; set; }
        public int Canceled { get; set; }
        public int InProcess { get; set; }
        public int Ready { get; set; }
        public int Delivered { get; set; }
    }

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

    Task<List<Order>> GetByLocalWindowAsync(DateTime localStart, DateTime localEnd, string timeZoneId);

    Task<List<Order>> GetOrdersByCustomerIdAsync(string customerId);
}

public class OrderService : IOrderService
{
    private readonly IMongoCollection<Order> _orders;

    public OrderService(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MongoDb");
        var client = new MongoClient(connectionString);
        var database = client.GetDatabase("lunchmate");  
        _orders = database.GetCollection<Order>("orders"); 
    }

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

    //FINAL METHOD USED BY CUSTOMERS TAB
    public async Task<List<Order>> GetOrdersByCustomerIdAsync(string customerId)
    {
        var customerFilter = Builders<Order>.Filter.Eq(o => o.CustomerId, customerId);

        // Only delivered or cancelled orders
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

    public async Task UpdateStatusAsync(string orderId, OrderStatus newStatus)
    {
        var orders = _db.GetCollection<Order>("orders");
        var update = Builders<Order>.Update
            .Set(o => o.Status, newStatus)
            .Set(o => o.UpdatedAt, DateTime.UtcNow);

        await orders.UpdateOneAsync(o => o.Id == orderId, update);
    }

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

        // Convertimos a DeliveryDateUtc keys usando la convención (00:00 UTC)
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

        var userProj = Builders<BsonDocument>.Projection.Include("_id").Include("name");
        var userDocs = await usersCol.Find(Builders<BsonDocument>.Filter.In("_id", custIds.Select(ObjectId.Parse)))
                                     .Project(userProj).ToListAsync();
        var nameByUserId = userDocs.ToDictionary(d => d["_id"].AsObjectId.ToString(),
                                                 d => d.TryGetValue("name", out var n) ? n.AsString : "Customer");

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

        // Agrupar por DeliveryDateUtc (cada día)
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        var groups = orders.GroupBy(o => o.DeliveryDateUtc)
            .Select(g =>
            {
                int canceled = g.Count(x => x.Status == OrderStatus.Cancelled);
                int delivered = g.Count(x => x.Status == OrderStatus.Delivered);
                int ready = g.Count(x => x.Status == OrderStatus.Ready);
                int inproc = g.Count(x => x.Status == OrderStatus.Pending); // "In process"

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
}
