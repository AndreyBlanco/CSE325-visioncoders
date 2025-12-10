/*
  File: OrderService.cs
  Description: MongoDB-backed service for managing orders. Provides CRUD operations,
               time-window queries, customer history, cook-focused expanded/grouped views,
               and status updates.
*/

namespace CSE325_visioncoders.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CSE325_visioncoders.Models;
using MongoDB.Bson;
using MongoDB.Driver;

/// <summary>
///— Class: OrderRow
/// Purpose: Lightweight projection for listing orders with denormalized meal and customer info.
/// </summary>
public class OrderRow
{
    public string Id { get; set; } = default!;
    public DateTime DeliveryDateUtc { get; set; }
    public string CustomerId { get; set; } = default!;
    public string CustomerName { get; set; } = "";
    public string MealId { get; set; } = default!;
    public string MealName { get; set; } = "";
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
///— Class: OrderGroupRow
///— Purpose: Aggregate row grouped by local date for a specific meal.
/// </summary>
public class OrderGroupRow
{
    public DateTime DateLocal { get; set; }
    public string MealId { get; set; } = default!;
    public string MealName { get; set; } = "";
    public int Total { get; set; }
    public int Canceled { get; set; }
    public int InProcess { get; set; }
    public int Ready { get; set; }
    public int Delivered { get; set; }
}

/// <summary>
///— Interface: IOrderService
///— Purpose: Defines operations for orders including CRUD, queries, views, and status updates.
/// </summary>
public interface IOrderService
{
    // CRUD Operations
    /// <summary>
    /// Function: GetAsync
    /// Purpose: Retrieves all orders sorted by creation date descending.
    /// </summary>
    Task<List<Order>> GetAsync();

    /// <summary>
    /// Function: GetByIdAsync
    /// Purpose: Retrieves an order by its identifier.
    /// </summary>
    Task<Order?> GetByIdAsync(string id);

    /// <summary>
    /// Function: CreateAsync
    /// Purpose: Inserts a new order.
    /// </summary>
    Task CreateAsync(Order order);

    /// <summary>
    /// Function: UpdateAsync
    /// Purpose: Replaces an existing order by ID.
    /// </summary>
    Task UpdateAsync(Order order);

    /// <summary>
    /// Function: DeleteAsync
    /// Purpose: Deletes an order by ID.
    /// </summary>
    Task DeleteAsync(string id);

    // Window by CreatedAt (legacy)
    /// <summary>
    /// Function: GetByLocalWindowAsync
    /// Purpose: Retrieves orders by local time window based on CreatedAt.
    /// </summary>
    Task<List<Order>> GetByLocalWindowAsync(DateTime localStart, DateTime localEnd, string timeZoneId);

    // Customers Tab (legacy)
    /// <summary>
    /// Function: GetOrdersByCustomerIdAsync
    /// Purpose: Retrieves delivered and cancelled orders for a customer.
    /// </summary>
    Task<List<Order>> GetOrdersByCustomerIdAsync(string customerId);

    // New: expanded view for /cook/orders
    /// <summary>
    /// Function: GetCookOrdersExpandedAsync
    /// Purpose: Returns denormalized rows for a cook across a date range with optional filters.
    /// </summary>
    Task<List<OrderRow>> GetCookOrdersExpandedAsync(
        string cookId,
        DateTime fromLocal,
        DateTime toLocal,
        string tzId,
        DateTime? filterDateLocal = null,
        string? filterMealId = null);

    // New: grouped view for a meal
    /// <summary>
    /// Function: GetCookOrdersGroupedAsync
    /// Purpose: Returns grouped aggregates by local date for a specific meal and cook.
    /// </summary>
    Task<List<OrderGroupRow>> GetCookOrdersGroupedAsync(
        string cookId,
        string mealId,
        DateTime fromLocal,
        DateTime toLocal,
        string tzId);

    // Status Updates
    /// <summary>
    /// Function: UpdateStatusAsync
    /// Purpose: Updates an order's status and sets UpdatedAt.
    /// </summary>
    Task UpdateStatusAsync(string orderId, OrderStatus newStatus);
}

/// <summary>
///— Class: OrderService
///— Purpose: Implements order CRUD operations, legacy queries, cook-focused views, and status updates.
/// </summary>
public class OrderService : IOrderService
{
    private readonly IMongoDatabase _db;
    private readonly IMongoCollection<Order> _orders;

    /// <summary>
    /// Constructor: OrderService
    /// Purpose: Initializes MongoDB connections and collections.
    /// </summary>
    public OrderService(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MongoDb");
        var client = new MongoClient(connectionString);
        _db = client.GetDatabase("lunchmate");
        _orders = _db.GetCollection<Order>("orders");
    }

    // CRUD Operations

    /// <summary>
    /// Function: GetAsync
    /// Purpose: Retrieves all orders sorted by creation date descending.
    /// </summary>
    public async Task<List<Order>> GetAsync() =>
        await _orders.Find(_ => true)
                     .SortByDescending(o => o.CreatedAt)
                     .ToListAsync();

    /// <summary>
    /// Function: GetByIdAsync
    /// Purpose: Retrieves an order by its identifier.
    /// </summary>
    public async Task<Order?> GetByIdAsync(string id) =>
        await _orders.Find(o => o.Id == id).FirstOrDefaultAsync();

    /// <summary>
    /// Function: CreateAsync
    /// Purpose: Inserts a new order.
    /// </summary>
    public async Task CreateAsync(Order order) =>
        await _orders.InsertOneAsync(order);

    /// <summary>
    /// Function: UpdateAsync
    /// Purpose: Replaces an existing order by ID.
    /// </summary>
    public async Task UpdateAsync(Order order) =>
        await _orders.ReplaceOneAsync(o => o.Id == order.Id, order);

    /// <summary>
    /// Function: DeleteAsync
    /// Purpose: Deletes an order by ID.
    /// </summary>
    public async Task DeleteAsync(string id) =>
        await _orders.DeleteOneAsync(o => o.Id == id);

    // Window by CreatedAt (legacy)

    /// <summary>
    /// Function: GetByLocalWindowAsync
    /// Purpose: Retrieves orders by local time window based on CreatedAt.
    /// </summary>
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

    // Customers Tab (legacy)

    /// <summary>
    /// Function: GetOrdersByCustomerIdAsync
    /// Purpose: Retrieves delivered and cancelled orders for a customer.
    /// </summary>
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

    /// <summary>
    /// Function: GetCookOrdersExpandedAsync
    /// Purpose: Returns denormalized rows for a cook across a date range with optional filters.
    /// </summary>
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

    /// <summary>
    /// Function: GetCookOrdersGroupedAsync
    /// Purpose: Returns grouped aggregates by local date for a specific meal and cook.
    /// </summary>
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

    // Status Updates

    /// <summary>
    /// Function: UpdateStatusAsync
    /// Purpose: Updates an order's status and sets UpdatedAt.
    /// </summary>
    public async Task UpdateStatusAsync(string orderId, OrderStatus newStatus)
    {
        var ordersCol = _db.GetCollection<Order>("orders");
        var update = Builders<Order>.Update
            .Set(o => o.Status, newStatus)
            .Set(o => o.UpdatedAt, DateTime.UtcNow);

        await ordersCol.UpdateOneAsync(o => o.Id == orderId, update);
    }
}