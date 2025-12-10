/*
  File: CustomerOrdersService.cs
  Description: MongoDB-backed service for customer order flows, including:
               range retrieval, create/update, cancellation, weekly menu with user selection,
               rating aggregation, and time zone utilities.
*/

using CSE325_visioncoders.Models;
using MongoDB.Driver;
using MongoDB.Bson;
using TimeZoneConverter;
using System.Linq;

namespace CSE325_visioncoders.Services
{
    /// <summary>
    /// Class: CustomerOrdersService
    /// Purpose: Manages customer order lifecycle and weekly menu selections, including validation and aggregation.
    /// </summary>
    public class CustomerOrdersService
    {
        private readonly IMongoCollection<Order> _orders;
        private readonly IMongoCollection<MenuDay> _menuDays;
        private readonly IMongoCollection<Meal> _meals;
        private readonly IMongoCollection<MealReview> _reviews;
        private readonly IMongoCollection<BsonDocument> _users;

        // DTOs

        /// <summary>
        /// Class: DishInfo
        /// Purpose: Represents a menu dish hydrated with details, pricing, and ratings.
        /// </summary>
        public class DishInfo
        {
            public int Index { get; set; }
            public string MealId { get; set; } = "";
            public string Name { get; set; } = "";
            public string? Description { get; set; }
            public string? Ingredients { get; set; }
            public decimal Price { get; set; }
            public string? ImageUrl { get; set; }
            public string? Notes { get; set; }
            public string? CookName { get; set; }
            public double AverageRating { get; set; }
            public int TotalReviews { get; set; }
        }

        // Constructor and initialization

        /// <summary>
        /// Constructor: CustomerOrdersService
        /// Purpose: Initializes MongoDB collections for users, orders, menu days, meals, and reviews.
        /// </summary>
        public CustomerOrdersService(IConfiguration configuration)
        {
            var cs = configuration.GetConnectionString("MongoDb");
            var client = new MongoClient(cs);
            var db = client.GetDatabase("lunchmate");
            _users = db.GetCollection<BsonDocument>("users");
            _orders = db.GetCollection<Order>("orders");
            _menuDays = db.GetCollection<MenuDay>("menu_days");
            _meals = db.GetCollection<Meal>("meals");
            _reviews = db.GetCollection<MealReview>("reviews");
        }

        // Retrieval

        /// <summary>
        /// Function: GetMyOrdersRangeAsync
        /// Purpose: Retrieves customer orders in a UTC date range with optional cook filter.
        /// </summary>
        public async Task<List<Order>> GetMyOrdersRangeAsync(string customerId, DateTime fromUtc, DateTime toUtc, string? cookId = null)
        {
            var filter = Builders<Order>.Filter.Eq(o => o.CustomerId, customerId) &
                         Builders<Order>.Filter.Gte(o => o.DeliveryDateUtc, fromUtc) &
                         Builders<Order>.Filter.Lt(o => o.DeliveryDateUtc, toUtc);

            if (!string.IsNullOrWhiteSpace(cookId))
                filter &= Builders<Order>.Filter.Eq(o => o.CookId, cookId);

            return await _orders.Find(filter).SortBy(o => o.DeliveryDateUtc).ToListAsync();
        }

        // Mutations

        /// <summary>
        /// Function: CreateOrUpdateOrderAsync
        /// Purpose: Creates or updates an order for a given customer, cook, meal, and local delivery date.
        /// </summary>
        public async Task<(bool ok, string message, Order? order)> CreateOrUpdateOrderAsync(
            string customerId,
            string cookId,
            string mealId,
            DateTime deliveryDateLocal,
            string tzId)
        {
            // Validate that a menu exists for the cook and date
            var dayKey = NormalizeLocalDate(deliveryDateLocal);
            var mdFilter = Builders<MenuDay>.Filter.Eq(m => m.CookId, cookId) &
                           Builders<MenuDay>.Filter.Eq(m => m.Date, dayKey);
            var menuDay = await _menuDays.Find(mdFilter).FirstOrDefaultAsync();
            if (menuDay == null)
                return (false, "No menu found for the selected day.", null);

            // Validate that the selected meal is present in the menu
            var dish = menuDay.Dishes.FirstOrDefault(d => d.MealId == mealId);
            if (dish == null)
                return (false, "Selected meal is not part of this day's menu.", null);

            // Resolve time zone and compute cutoff
            var tz = ResolveTimeZone(tzId);
            var cancelUntilUtc = ComputeCancelUntilUtc(dayKey, tz);

            // Compute delivery date UTC key (00:00 UTC per local day)
            var deliveryDateUtc = UtcKey(dayKey);

            // Resolve meal to freeze price at order time
            var meal = await _meals.Find(m => m.Id == mealId).FirstOrDefaultAsync();
            if (meal == null) return (false, "Meal not found.", null);

            // Upsert by logical key: customer + cook + day
            var keyFilter = Builders<Order>.Filter.Eq(o => o.CustomerId, customerId) &
                            Builders<Order>.Filter.Eq(o => o.CookId, cookId) &
                            Builders<Order>.Filter.Eq(o => o.DeliveryDateUtc, deliveryDateUtc);

            var existing = await _orders.Find(keyFilter).FirstOrDefaultAsync();

            if (existing != null)
            {
                if (!CanCancel(existing))
                    return (false, "Cutoff time has passed. You cannot modify this order.", existing);

                var update = Builders<Order>.Update
                    .Set(o => o.MealId, mealId)
                    .Set(o => o.PriceAtOrder, meal.Price)
                    .Set(o => o.TimeZone, tzId)
                    .Set(o => o.CancelUntilUtc, cancelUntilUtc)
                    .Set(o => o.UpdatedAt, DateTime.UtcNow);

                await _orders.UpdateOneAsync(keyFilter, update);
                var updated = await _orders.Find(keyFilter).FirstOrDefaultAsync();
                return (true, "Order updated.", updated);
            }
            else
            {
                var order = new Order
                {
                    CookId = cookId,
                    CustomerId = customerId,
                    MealId = mealId,
                    DeliveryDateUtc = deliveryDateUtc,
                    CancelUntilUtc = cancelUntilUtc,
                    TimeZone = tzId,
                    PriceAtOrder = meal.Price,
                    Status = OrderStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _orders.InsertOneAsync(order);
                return (true, "Order created.", order);
            }
        }

        /// <summary>
        /// Function: CancelOrderAsync
        /// Purpose: Cancels an order by logical key if cutoff has not passed.
        /// </summary>
        public async Task<(bool ok, string message)> CancelOrderAsync(
            string customerId,
            string cookId,
            DateTime deliveryDateLocal)
        {
            var dayKey = NormalizeLocalDate(deliveryDateLocal);
            var deliveryDateUtc = UtcKey(dayKey);

            var keyFilter = Builders<Order>.Filter.Eq(o => o.CustomerId, customerId) &
                            Builders<Order>.Filter.Eq(o => o.CookId, cookId) &
                            Builders<Order>.Filter.Eq(o => o.DeliveryDateUtc, deliveryDateUtc);

            var existing = await _orders.Find(keyFilter).FirstOrDefaultAsync();
            if (existing == null) return (false, "Order not found.");

            if (!CanCancel(existing))
                return (false, "Cutoff time has passed. You cannot cancel this order.");

            var update = Builders<Order>.Update
                .Set(o => o.Status, OrderStatus.Cancelled)
                .Set(o => o.UpdatedAt, DateTime.UtcNow);

            await _orders.UpdateOneAsync(keyFilter, update);
            return (true, "Order cancelled.");
        }

        // Weekly view with user selection

        /// <summary>
        /// Class: MenuDayWithSelection
        /// Purpose: Represents a menu day combined with the customer's selection and hydrated dishes.
        /// </summary>
        public class MenuDayWithSelection
        {
            public MenuDay Day { get; set; } = default!;
            public Order? MyOrder { get; set; }
            public string? SelectedMealId => MyOrder?.MealId;
            public bool CanCancel => MyOrder != null && CustomerOrdersService.CanCancel(MyOrder);
            public List<DishInfo> DishesHydrated { get; set; } = new();
        }

        /// <summary>
        /// Function: GetWeekWithSelectionsAsync
        /// Purpose: Retrieves a week of menu days for a cook and the customer's orders for that week, hydrated with meal data and ratings.
        /// </summary>
        public async Task<List<MenuDayWithSelection>> GetWeekWithSelectionsAsync(
            string customerId,
            string cookId,
            DateTime weekStartLocal)
        {
            var startLocal = NormalizeLocalDate(weekStartLocal);
            var endLocal = startLocal.AddDays(7);

            var days = await _menuDays.Find(m => m.CookId == cookId &&
                                                 m.Date >= startLocal &&
                                                 m.Date < endLocal)
                                      .SortBy(m => m.Date)
                                      .ToListAsync();

            // DeliveryDateUtc range using the UTC midnight convention
            var startUtcKey = UtcKey(startLocal);
            var endUtcKey = UtcKey(endLocal);

            var orders = await _orders.Find(o =>
                o.CustomerId == customerId &&
                o.CookId == cookId &&
                o.DeliveryDateUtc >= startUtcKey &&
                o.DeliveryDateUtc < endUtcKey)
                .ToListAsync();

            var byDate = orders.ToDictionary(o => o.DeliveryDateUtc, o => o);

            var mealIds = days
                .SelectMany(d => d.Dishes?.Select(x => x.MealId) ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            var mealsList = await _meals.Find(m => mealIds.Contains(m.Id!)).ToListAsync();
            var mealById = mealsList.ToDictionary(m => m.Id!, m => m);

            var cookIdsInMeals = mealsList.Select(m => m.CookId).Where(IsValidObjectId).Distinct().ToList();
            var cookNamesById = new Dictionary<string, string>(StringComparer.Ordinal);
            if (cookIdsInMeals.Count > 0)
            {
                var cookObjIds = cookIdsInMeals.Select(ObjectId.Parse).ToList();
                var projUsers = Builders<BsonDocument>.Projection.Include("_id").Include("name");
                var cooks = await _users.Find(Builders<BsonDocument>.Filter.In("_id", cookObjIds))
                                        .Project(projUsers).ToListAsync();

                foreach (var u in cooks)
                {
                    var id = u["_id"].AsObjectId.ToString();
                    var nm = u.TryGetValue("name", out var n) ? n.AsString : "Cook";
                    cookNamesById[id] = string.IsNullOrWhiteSpace(nm) ? "Cook" : nm;
                }
            }

            var ratingsAgg = await _reviews.Aggregate()
                .Match(r => mealIds.Contains(r.MealId))
                .Group(
                    r => r.MealId,
                    g => new { MealId = g.Key, Avg = g.Average(x => x.Rating), Cnt = g.Count() }
                )
                .ToListAsync();

            var ratingByMealId = ratingsAgg.ToDictionary(x => x.MealId, x => (avg: x.Avg, cnt: x.Cnt));

            var result = new List<MenuDayWithSelection>();
            foreach (var d in days)
            {
                var keyUtc = UtcKey(d.Date);
                byDate.TryGetValue(keyUtc, out var myOrder);

                var hydrated = (d.Dishes ?? new List<MenuDish>())
                    .Where(x => !string.IsNullOrWhiteSpace(x.MealId))
                    .OrderBy(x => x.Index)
                    .Select(x =>
                    {
                        mealById.TryGetValue(x.MealId, out var mm);
                        ratingByMealId.TryGetValue(x.MealId, out var rv);

                        return new DishInfo
                        {
                            Index = x.Index,
                            MealId = x.MealId,
                            Name = mm?.Name ?? x.Name,
                            Description = mm?.Description,
                            Ingredients = mm?.Ingredients,
                            Price = mm?.Price ?? 0m,
                            ImageUrl = mm?.ImageUrl,
                            Notes = x.Notes,
                            CookName = (mm != null && cookNamesById.TryGetValue(mm.CookId, out var cn)) ? cn : (mm?.CookName),
                            AverageRating = rv.avg,
                            TotalReviews = rv.cnt
                        };
                    })
                    .ToList();

                result.Add(new MenuDayWithSelection
                {
                    Day = d,
                    MyOrder = myOrder,
                    DishesHydrated = hydrated
                });
            }

            return result;
        }

        // Helpers

        /// <summary>
        /// Function: IsValidObjectId
        /// Purpose: Validates an ObjectId string.
        /// </summary>
        private static bool IsValidObjectId(string? id)
            => !string.IsNullOrWhiteSpace(id) && ObjectId.TryParse(id, out _);

        /// <summary>
        /// Function: CanCancel
        /// Purpose: Indicates if the current UTC time is before or equal to the order's cancel-until timestamp.
        /// </summary>
        public static bool CanCancel(Order o) => DateTime.UtcNow <= o.CancelUntilUtc;

        /// <summary>
        /// Function: NormalizeLocalDate
        /// Purpose: Returns the same local date at midnight with Kind set to Unspecified.
        /// </summary>
        public static DateTime NormalizeLocalDate(DateTime localDate)
            => new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Unspecified);

        /// <summary>
        /// Function: UtcKey
        /// Purpose: Returns the UTC midnight key for the supplied local date.
        /// </summary>
        public static DateTime UtcKey(DateTime localDate)
            => new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Function: ResolveTimeZone
        /// Purpose: Resolves a time zone by ID using TZConvert or system fallback.
        /// </summary>
        private static TimeZoneInfo ResolveTimeZone(string tzId)
        {
            try { return TZConvert.GetTimeZoneInfo(tzId); }
            catch
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(tzId); }
                catch { return TimeZoneInfo.Utc; }
            }
        }

        /// <summary>
        /// Function: ComputeCancelUntilUtc
        /// Purpose: Computes the cancel cutoff time in UTC for the given local day at 8:00 AM local.
        /// </summary>
        private static DateTime ComputeCancelUntilUtc(DateTime dayLocal, TimeZoneInfo tz)
        {
            var d = NormalizeLocalDate(dayLocal);
            var cutoffLocal = new DateTime(d.Year, d.Month, d.Day, 8, 0, 0, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(cutoffLocal, tz);
        }
    }
}