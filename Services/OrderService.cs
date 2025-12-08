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

    Task<List<Order>> GetByLocalWindowAsync(DateTime localStart, DateTime localEnd, string timeZoneId);

    //FINAL METHOD USED BY CUSTOMERS TAB
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
}
