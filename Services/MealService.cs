using CSE325_visioncoders.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CSE325_visioncoders.Services
{
    public class MealService
    {
        private readonly IMongoCollection<Meal> _meals;
        private readonly IMongoCollection<BsonDocument> _users;

        public MealService(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("MongoDb");
            var client = new MongoClient(connectionString);
            var database = client.GetDatabase("lunchmate");
            _users = database.GetCollection<BsonDocument>("users");
            _meals = database.GetCollection<Meal>("meals");
        }

        public async Task<List<Meal>> GetAsync() =>
            await _meals.Find(_ => true).ToListAsync();

        public async Task<Meal?> GetByIdAsync(string id) =>
            await _meals.Find(m => m.Id == id).FirstOrDefaultAsync();  

        public async Task CreateAsync(Meal meal)
        {
            meal.CreatedAt = DateTime.UtcNow;
            meal.IsActive = true;
            await _meals.InsertOneAsync(meal);
        }

        public async Task UpdateAsync(Meal meal) =>                  
            await _meals.ReplaceOneAsync(m => m.Id == meal.Id, meal);

        public async Task DeleteAsync(string id) =>
            await _meals.DeleteOneAsync(m => m.Id == id);

        public async Task<List<Meal>> GetActiveAsync(string? cookId = null, string? search = null)
        {
            var filter = Builders<Meal>.Filter.Eq(m => m.IsActive, true);

            if (IsValidObjectId(cookId))
                filter &= Builders<Meal>.Filter.Eq(m => m.CookId, cookId!);

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

        private static bool IsValidObjectId(string? id)
            => !string.IsNullOrWhiteSpace(id) && ObjectId.TryParse(id, out _);

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

        public async Task<List<Meal>> GetByCookAsync(string cookId, bool onlyActive = true)
        {
            var filter = Builders<Meal>.Filter.Eq(m => m.CookId, cookId);
            if (onlyActive) filter &= Builders<Meal>.Filter.Eq(m => m.IsActive, true);
            return await _meals.Find(filter).SortBy(m => m.Name).ToListAsync();
        }
    }
}
