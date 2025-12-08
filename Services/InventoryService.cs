using CSE325_visioncoders.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CSE325_visioncoders.Services
{
    public class InventoryService
    {
        private readonly IMongoCollection<InventoryItem> _inventory;

        public InventoryService(IOptions<MongoDbSettings> settings)
        {
            var client = new MongoClient(settings.Value.ConnectionString);
            var db = client.GetDatabase(settings.Value.DatabaseName);

            _inventory = db.GetCollection<InventoryItem>("inventory");
        }

        // All items for a specific cook
        public async Task<List<InventoryItem>> GetByCookAsync(string cookId)
        {
            var filter = Builders<InventoryItem>.Filter.Eq(i => i.CookId, cookId);
            return await _inventory.Find(filter)
                                   .SortBy(i => i.Name)
                                   .ToListAsync();
        }

        public async Task CreateAsync(InventoryItem item)
        {
            await _inventory.InsertOneAsync(item);
        }

        // Update only this cook's item
        public async Task UpdateAsync(string cookId, InventoryItem item)
        {
            var filter = Builders<InventoryItem>.Filter.And(
                Builders<InventoryItem>.Filter.Eq(i => i.Id, item.Id),
                Builders<InventoryItem>.Filter.Eq(i => i.CookId, cookId)
            );

            await _inventory.ReplaceOneAsync(filter, item);
        }

        // Change quantity only on this cook's item
        public async Task UpdateQuantityAsync(string cookId, string id, decimal amount)
        {
            var filter = Builders<InventoryItem>.Filter.And(
                Builders<InventoryItem>.Filter.Eq(i => i.Id, id),
                Builders<InventoryItem>.Filter.Eq(i => i.CookId, cookId)
            );

            var update = Builders<InventoryItem>.Update
                .Inc(i => i.Quantity, amount)
                .Set(i => i.LastUpdated, DateTime.UtcNow);

            await _inventory.UpdateOneAsync(filter, update);
        }

        // Delete only this cook's item
        public async Task DeleteAsync(string cookId, string id)
        {
            var filter = Builders<InventoryItem>.Filter.And(
                Builders<InventoryItem>.Filter.Eq(i => i.Id, id),
                Builders<InventoryItem>.Filter.Eq(i => i.CookId, cookId)
            );

            await _inventory.DeleteOneAsync(filter);
        }
    }
}
