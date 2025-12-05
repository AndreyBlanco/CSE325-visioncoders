using CSE325_visioncoders.Models;
using MongoDB.Driver;

namespace CSE325_visioncoders.Services
{
    public class ReviewService
    {
        private readonly IMongoCollection<MealReview> _reviews;

        public ReviewService(IConfiguration configuration)
        {
            var conn = configuration.GetConnectionString("MongoDb");
            var client = new MongoClient(conn);
            var db = client.GetDatabase("lunchmate");
            _reviews = db.GetCollection<MealReview>("reviews");

            try
            {
                _reviews.Indexes.CreateMany(new[]
                {
                    new CreateIndexModel<MealReview>(
                        Builders<MealReview>.IndexKeys.Ascending(r => r.MealId),
                        new CreateIndexOptions { Name = "ix_reviews_meal" }),
                    new CreateIndexModel<MealReview>(
                        Builders<MealReview>.IndexKeys.Ascending(r => r.MealId).Ascending(r => r.UserId),
                        new CreateIndexOptions { Unique = true, Name = "ux_reviews_meal_user" })
                });
            }
            catch { /* índices ya existen o hay duplicados previos */ }
        }

        public async Task<List<MealReview>> GetByMealAsync(string mealId)
        {
            return await _reviews.Find(r => r.MealId == mealId)
                                 .SortByDescending(r => r.CreatedAt)
                                 .ToListAsync();
        }

        public async Task<MealReview?> GetUserReviewAsync(string mealId, string userId)
        {
            return await _reviews.Find(r => r.MealId == mealId && r.UserId == userId)
                                 .FirstOrDefaultAsync();
        }

        // Crea o reemplaza la review del usuario para ese meal
        public async Task UpsertAsync(MealReview review)
        {
            review.CreatedAt = DateTime.UtcNow;
            var filter = Builders<MealReview>.Filter.Eq(r => r.MealId, review.MealId) &
                         Builders<MealReview>.Filter.Eq(r => r.UserId, review.UserId);

            await _reviews.ReplaceOneAsync(filter, review, new ReplaceOptions { IsUpsert = true });
        }
    }
}