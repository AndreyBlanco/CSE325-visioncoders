using CSE325_visioncoders.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CSE325_visioncoders.Services
{
    /// <summary>
    /// Service responsible for managing meal objects stored in MongoDB.
    /// Provides CRUD operations, active meal filtering, and cook lookup.
    /// This service abstracts database access and ensures the UI interacts
    /// cleanly with structured Meal data.
    /// </summary>
    public class MealService
    {
        private readonly IMongoCollection<Meal> _meals;
        private readonly IMongoCollection<BsonDocument> _users;

        /// <summary>
        /// Initializes the service by connecting to MongoDB and preparing
        /// the collections required for meals and cook name lookup.
        /// </summary>
        public MealService(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("MongoDb");
            var client = new MongoClient(connectionString);
            var database = client.GetDatabase("lunchmate");
            _users = database.GetCollection<BsonDocument>("users");
            _meals = database.GetCollection<Meal>("meals");
        }

        /// <summary>
        /// Retrieves all meals in the system, regardless of status.
        /// </summary>
        public async Task<List<Meal>> GetAsync() =>
            await _meals.Find(_ => true).ToListAsync();

        /// <summary>
        /// Retrieves a single meal by its ID. Returns null if not found.
        /// </summary>
        public async Task<Meal?> GetByIdAsync(string id) =>
            await _meals.Find(m => m.Id == id).FirstOrDefaultAsync();

        /// <summary>
        /// Creates a new meal document, assigning timestamps and marking it active.
        /// </summary>
        public async Task CreateAsync(Meal meal)
        {
            meal.CreatedAt = DateTime.UtcNow;
            meal.IsActive = true;
            await _meals.InsertOneAsync(meal);
        }

        /// <summary>
        /// Replaces the full meal document with updated data.
        /// </summary>
        public async Task UpdateAsync(Meal meal) =>
            await _meals.ReplaceOneAsync(m => m.Id == meal.Id, meal);

        /// <summary>
        /// Permanently deletes a meal by ID.
        /// </summary>
        public async Task DeleteAsync(string id) =>
            await _meals.DeleteOneAsync(m => m.Id == id);

        /// <summary>
        /// Returns all active meals, optionally filtered by cook ID or search text
        /// (which matches name, description, or ingredient list).
        /// Useful for meal browser and cook dashboards.
        /// </summary>
        public async Task<List<Meal>> GetActiveAsync(string? cookId = null, string? search = null)
        {
            var filter = Builders<Meal>.Filter.Eq(m => m.IsActive, true);

            // Filter by the cook who created the meal
            if (IsValidObjectId(cookId))
                filter &= Builders<Meal>.Filter.Eq(m => m.CookId, cookId!);

            // Text search using case-insensitive regex
            if (!string.IsNullOrWhiteSpace(search))
            {
                var regex = new BsonRegularExpression(search, "i");
                var or = Builders<Meal>.Filter.Or(
                    Builders<Meal>.Filter.Regex(m => m.Name, regex),
                    Builders<Meal>.Filter.Regex(m => m.Description, regex),
                    Builders<Meal>.Filter.Regex(m => m.Ingredients, regex)
                );
                filter &= or;
            }

            return await _meals.Find(filter).SortBy(m => m.Name).ToListAsync();
        }

        /// <summary>
        /// Validates that a string can be interpreted as a MongoDB ObjectId.
        /// Used to filter meals by cook ID.
        /// </summary>
        private static bool IsValidObjectId(string? id)
            => !string.IsNullOrWhiteSpace(id) && ObjectId.TryParse(id, out _);

        /// <summary>
        /// Returns all cooks who currently have active meals, by mapping
        /// Meal.CookId → users collection. Supports dropdown selection for filtering.
        /// </summary>
        public async Task<List<CookOption>> GetActiveCooksAsync()
        {
            var filterActive = Builders<Meal>.Filter.Eq(m => m.IsActive, true);

            var cookIds = await _meals.Distinct<string>(nameof(Meal.CookId), filterActive).ToListAsync();
            cookIds = cookIds.Where(IsValidObjectId).Distinct().ToList();
            if (cookIds.Count == 0) return new List<CookOption>();

            var objIds = cookIds.Select(id => ObjectId.Parse(id)).ToList();
            var proj = Builders<BsonDocument>.Projection.Include("_id").Include("name");
            var userDocs = await _users.Find(Builders<BsonDocument>.Filter.In("_id", objIds))
                                       .Project(proj)
                                       .ToListAsync();

            var nameById = userDocs.ToDictionary(
                d => d["_id"].AsObjectId.ToString(),
                d => d.TryGetValue("name", out var n) ? n.AsString : "Cook",
                StringComparer.Ordinal
            );

            return cookIds
                .Select(id => new CookOption(id, nameById.TryGetValue(id, out var nm) && !string.IsNullOrWhiteSpace(nm) ? nm : "Cook"))
                .OrderBy(c => c.Name)
                .ToList();
        }

        /// <summary>
        /// Retrieves all meals created by a specific cook.
        /// Can optionally include inactive meals.
        /// </summary>
        public async Task<List<Meal>> GetByCookAsync(string cookId, bool onlyActive = true)
        {
            var filter = Builders<Meal>.Filter.Eq(m => m.CookId, cookId);
            if (onlyActive) filter &= Builders<Meal>.Filter.Eq(m => m.IsActive, true);
            return await _meals.Find(filter).SortBy(m => m.Name).ToListAsync();
        }
    }
}
