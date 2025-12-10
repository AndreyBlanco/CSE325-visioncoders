/*
  File: MenuDayService.cs
  Description: MongoDB-backed service for managing cook menu days. Provides weekly retrieval,
               upsert operations, and a deterministic key helper.
*/

using CSE325_visioncoders.Models;
using System.Security.Cryptography;
using MongoDB.Driver;

namespace CSE325_visioncoders.Services
{
    /// <summary>
    /// Class: MenuDayService
    /// Purpose: Manages menu day documents for cooks, including weekly retrieval and upsert.
    /// </summary>
    public class MenuDayService
    {
        private readonly IMongoCollection<MenuDay> _menuDays;

        // Constructor and initialization

        /// <summary>
        /// Constructor: MenuDayService
        /// Purpose: Initializes MongoDB collection and ensures unique index for cook and date.
        /// </summary>
        public MenuDayService(IConfiguration configuration)
        {
            var cs = configuration.GetConnectionString("MongoDb");
            var client = new MongoClient(cs);
            var db = client.GetDatabase("lunchmate");
            _menuDays = db.GetCollection<MenuDay>("menu_days");
            try
            {
                _menuDays.Indexes.CreateOne(
                    new CreateIndexModel<MenuDay>(
                        Builders<MenuDay>.IndexKeys.Ascending(m => m.CookId).Ascending(m => m.Date),
                        new CreateIndexOptions { Unique = true, Name = "ux_menudays_cook_date" }
                    )
                );
            }
            catch
            {
                // Index may already exist or there may be pre-existing duplicates.
            }
        }

        // Retrieval

        /// <summary>
        /// Function: GetWeekByCookAsync
        /// Purpose: Retrieves a seven-day window of menu days for the specified cook starting at the given local date.
        /// </summary>
        public async Task<List<MenuDay>> GetWeekByCookAsync(string cookId, DateTime weekStartLocal)
        {
            var start = NormalizeLocalDate(weekStartLocal);
            var end = start.AddDays(7);

            var days = await _menuDays.Find(m => m.CookId == cookId && m.Date >= start && m.Date < end)
                                      .SortBy(m => m.Date)
                                      .ToListAsync();
            return days;
        }

        // Mutations

        /// <summary>
        /// Function: UpsertMenuDayAsync
        /// Purpose: Creates or updates a menu day for the given cook and date.
        /// </summary>
        public async Task UpsertMenuDayAsync(MenuDay source, string cookId, string tzId)
        {
            if (string.IsNullOrWhiteSpace(cookId))
                throw new InvalidOperationException("CookId is required.");

            // Normalize local date to midnight (Kind Unspecified)
            var dateKey = NormalizeLocalDate(source.Date);

            // Ensure three dishes ordered by index
            source.EnsureThreeDishes();

            var dishes = source.Dishes
                .OrderBy(d => d.Index)
                .Take(3)
                .Select(d => new MenuDish
                {
                    Index = d.Index,
                    MealId = d.MealId,
                    Name = d.Name,
                    Notes = d.Notes
                })
                .ToList();

            // Find existing record by logical key CookId + Date
            var filter = Builders<MenuDay>.Filter.Eq(m => m.CookId, cookId) &
                         Builders<MenuDay>.Filter.Eq(m => m.Date, dateKey);

            var existing = await _menuDays.Find(filter).FirstOrDefaultAsync();

            if (existing == null)
            {
                var toInsert = new MenuDay
                {
                    // Deterministic int key to avoid duplicates per cook and day
                    Id = ComputeMenuDayKey(cookId, dateKey),

                    CookId = cookId,
                    TimeZone = string.IsNullOrWhiteSpace(source.TimeZone) ? tzId : source.TimeZone,
                    Date = dateKey,
                    Status = source.Status,
                    Dishes = dishes,
                    PublishedAt = source.Status == MenuDayStatus.Published ? DateTime.UtcNow : null,
                    ClosedAt = null,
                    ConfirmationsCount = source.ConfirmationsCount
                };

                await _menuDays.InsertOneAsync(toInsert);
            }
            else
            {
                var wasPublished = existing.Status == MenuDayStatus.Published;
                var nowPublished = source.Status == MenuDayStatus.Published;

                var publishedAt = existing.PublishedAt;
                if (!wasPublished && nowPublished)
                    publishedAt = DateTime.UtcNow;

                DateTime? closedAt = existing.ClosedAt;
                if (source.Status == MenuDayStatus.Closed && existing.ClosedAt == null)
                    closedAt = DateTime.UtcNow;
                if (source.Status != MenuDayStatus.Closed)
                    closedAt = null;

                var update = Builders<MenuDay>.Update
                    .Set(m => m.TimeZone, string.IsNullOrWhiteSpace(source.TimeZone) ? tzId : source.TimeZone)
                    .Set(m => m.Status, source.Status)
                    .Set(m => m.Dishes, dishes)
                    .Set(m => m.PublishedAt, publishedAt)
                    .Set(m => m.ClosedAt, closedAt)
                    .Set(m => m.ConfirmationsCount, source.ConfirmationsCount);

                await _menuDays.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = false });
            }
        }

        // Helpers

        /// <summary>
        /// Function: NormalizeLocalDate
        /// Purpose: Returns the same date with time set to midnight and Kind set to Unspecified.
        /// </summary>
        public static DateTime NormalizeLocalDate(DateTime localDate)
            => new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Unspecified);

        /// <summary>
        /// Function: ComputeMenuDayKey
        /// Purpose: Computes a deterministic integer identifier from CookId and Date.
        /// </summary>
        private static int ComputeMenuDayKey(string cookId, DateTime dateKey)
        {
            var s = $"{cookId}|{dateKey:yyyy-MM-dd}";
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            var hash = SHA1.HashData(bytes);
            return BitConverter.ToInt32(hash, 0);
        }
    }
}