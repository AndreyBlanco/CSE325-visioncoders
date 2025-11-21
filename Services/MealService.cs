using CSE325_visioncoders.Models;
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
    }
}
