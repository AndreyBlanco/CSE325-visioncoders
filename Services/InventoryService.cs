/*
  File: InventoryService.cs
  Description: MongoDB-backed service for managing cook inventory items, including retrieval,
               creation, updates, quantity adjustments, and deletion.
*/

using CSE325_visioncoders.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CSE325_visioncoders.Services
{
    /// <summary>
    /// Class: InventoryService
    /// Purpose: Manages inventory items per cook in MongoDB.
    /// </summary>
    public class InventoryService
    {
        private readonly IMongoCollection<InventoryItem> _inventory;

        /// <summary>
        /// Constructor: InventoryService
        /// Purpose: Initializes MongoDB collection using provided settings.
        /// </summary>
        public InventoryService(IOptions<MongoDbSettings> settings)
        {
            var client = new MongoClient(settings.Value.ConnectionString);
            var db = client.GetDatabase(settings.Value.DatabaseName);
            _inventory = db.GetCollection<InventoryItem>("inventory");
        }

        // Retrieval

        /// <summary>
        /// Function: GetByCookAsync
        /// Purpose: Retrieves all inventory items for the specified cook, sorted by name.
        /// </summary>
        public async Task<List<InventoryItem>> GetByCookAsync(string cookId)
        {
            var filter = Builders<InventoryItem>.Filter.Eq(i => i.CookId, cookId);
            return await _inventory.Find(filter)
                                   .SortBy(i => i.Name)
                                   .ToListAsync();
        }

        // Mutations

        /// <summary>
        /// Function: CreateAsync
        /// Purpose: Inserts a new inventory item.
        /// </summary>
        public async Task CreateAsync(InventoryItem item)
        {
            await _inventory.InsertOneAsync(item);
        }

        /// <summary>
        /// Function: UpdateAsync
        /// Purpose: Replaces an inventory item owned by the specified cook.
        /// </summary>
        public async Task UpdateAsync(string cookId, InventoryItem item)
        {
            var filter = Builders<InventoryItem>.Filter.And(
                Builders<InventoryItem>.Filter.Eq(i => i.Id, item.Id),
                Builders<InventoryItem>.Filter.Eq(i => i.CookId, cookId)
            );

            await _inventory.ReplaceOneAsync(filter, item);
        }

        /// <summary>
        /// Function: UpdateQuantityAsync
        /// Purpose: Adjusts quantity by the given amount and updates timestamp for the cook's item.
        /// </summary>
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

        /// <summary>
        /// Function: DeleteAsync
        /// Purpose: Deletes an inventory item owned by the specified cook.
        /// </summary>
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