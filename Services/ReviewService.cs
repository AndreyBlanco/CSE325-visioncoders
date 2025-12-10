/*
  File: ReviewService.cs
  Description: MongoDB-backed service for managing meal reviews, including:
               retrieval by meal, retrieval by user+meal, and upsert operations.
*/

using CSE325_visioncoders.Models;
using MongoDB.Driver;

namespace CSE325_visioncoders.Services
{
    /// <summary>
    /// Class: ReviewService
    /// Purpose: Manages read and upsert operations for meal reviews in MongoDB.
    /// </summary>
    public class ReviewService
    {
        private readonly IMongoCollection<MealReview> _reviews;

        /// <summary>
        /// Constructor: ReviewService
        /// Purpose: Initializes MongoDB client, database, reviews collection, and ensures indexes exist.
        /// </summary>
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
            catch
            {
            }
        }

        // Retrieval
        /// <summary>
        /// Function: GetByMealAsync
        /// Purpose: Retrieves all reviews for a given meal, sorted by most recent.
        /// </summary>
        public async Task<List<MealReview>> GetByMealAsync(string mealId)
        {
            return await _reviews.Find(r => r.MealId == mealId)
                                 .SortByDescending(r => r.CreatedAt)
                                 .ToListAsync();
        }

        /// <summary>
        /// Function: GetUserReviewAsync
        /// Purpose: Retrieves a specific user's review for a given meal.
        /// </summary>
        public async Task<MealReview?> GetUserReviewAsync(string mealId, string userId)
        {
            return await _reviews.Find(r => r.MealId == mealId && r.UserId == userId)
                                 .FirstOrDefaultAsync();
        }

        // Mutations
        /// <summary>
        /// Function: UpsertAsync
        /// Purpose: Creates or replaces the user's review for the specified meal.
        /// </summary>
        public async Task UpsertAsync(MealReview review)
        {
            review.CreatedAt = DateTime.UtcNow;
            var filter = Builders<MealReview>.Filter.Eq(r => r.MealId, review.MealId) &
                         Builders<MealReview>.Filter.Eq(r => r.UserId, review.UserId);

            await _reviews.ReplaceOneAsync(filter, review, new ReplaceOptions { IsUpsert = true });
        }
    }
}