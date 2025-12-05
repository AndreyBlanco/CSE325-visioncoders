using CSE325_visioncoders.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CSE325_visioncoders.Services
{
    public class MealService
    {
        private readonly IMongoCollection<Meal> _meals;

        public MealService(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("MongoDb");
            var client = new MongoClient(connectionString);
            var database = client.GetDatabase("lunchmate");
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
            // Solo filtramos IsActive en la consulta; el resto lo depuramos en memoria
            var docs = await _meals
                .Find(m => m.IsActive)
                .Project(m => new { m.CookId, m.CookName })
                .ToListAsync();

            var cooks = docs
                .Where(x => IsValidObjectId(x.CookId))
                .GroupBy(x => x.CookId!)
                .Select(g => new CookOption(
                    g.Key,
                    g.Select(x => x.CookName)
                     .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "Cook"))
                .OrderBy(c => c.Name)
                .ToList();

            return cooks;
        }

        public async Task<List<Meal>> GetByCookAsync(string cookId, bool onlyActive = true)
        {
            var filter = Builders<Meal>.Filter.Eq(m => m.CookId, cookId);
            if (onlyActive) filter &= Builders<Meal>.Filter.Eq(m => m.IsActive, true);
            return await _meals.Find(filter).SortBy(m => m.Name).ToListAsync();
        }
    }
}
