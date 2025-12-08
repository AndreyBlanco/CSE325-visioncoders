using CSE325_visioncoders.Models;
using MongoDB.Driver;
using TimeZoneConverter;
using System.Linq;

namespace CSE325_visioncoders.Services
{
    public class CustomerOrdersService
    {
        private readonly IMongoCollection<Order> _orders;
        private readonly IMongoCollection<MenuDay> _menuDays;
        private readonly IMongoCollection<Meal> _meals;
        private readonly IMongoCollection<MealReview> _reviews;

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

        public CustomerOrdersService(IConfiguration configuration)
        {
            var cs = configuration.GetConnectionString("MongoDb");
            var client = new MongoClient(cs);
            var db = client.GetDatabase("lunchmate");

            _orders = db.GetCollection<Order>("orders");
            _menuDays = db.GetCollection<MenuDay>("menu_days");
            _meals = db.GetCollection<Meal>("meals");
            _reviews = db.GetCollection<MealReview>("reviews");
        }

        // Obtiene mis órdenes en un rango (opcional filtro por cook)
        public async Task<List<Order>> GetMyOrdersRangeAsync(string customerId, DateTime fromUtc, DateTime toUtc, string? cookId = null)
        {
            var filter = Builders<Order>.Filter.Eq(o => o.CustomerId, customerId) &
                         Builders<Order>.Filter.Gte(o => o.DeliveryDateUtc, fromUtc) &
                         Builders<Order>.Filter.Lt(o => o.DeliveryDateUtc, toUtc);

            if (!string.IsNullOrWhiteSpace(cookId))
                filter &= Builders<Order>.Filter.Eq(o => o.CookId, cookId);

            return await _orders.Find(filter).SortBy(o => o.DeliveryDateUtc).ToListAsync();
        }

        // Crea o actualiza una orden (misma llave CustomerId+CookId+DeliveryDateUtc). Permite editar antes del cutoff.
        public async Task<(bool ok, string message, Order? order)> CreateOrUpdateOrderAsync(
            string customerId,
            string cookId,
            string mealId,
            DateTime deliveryDateLocal, // Kind Unspecified (fecha del menú, medianoche local)
            string tzId)
        {
            // 1) Validar menú del día
            var dayKey = NormalizeLocalDate(deliveryDateLocal);
            var mdFilter = Builders<MenuDay>.Filter.Eq(m => m.CookId, cookId) &
                           Builders<MenuDay>.Filter.Eq(m => m.Date, dayKey);
            var menuDay = await _menuDays.Find(mdFilter).FirstOrDefaultAsync();
            if (menuDay == null)
                return (false, "No menu found for the selected day.", null);

            // 2) Validar que la opción exista en el menú
            var dish = menuDay.Dishes.FirstOrDefault(d => d.MealId == mealId);
            if (dish == null)
                return (false, "Selected meal is not part of this day's menu.", null);

            // 3) Resolver TZ y calcular cutoff
            var tz = ResolveTimeZone(tzId);
            var cancelUntilUtc = ComputeCancelUntilUtc(dayKey, tz);

            // 4) DeliveryDateUtc (llave de día en UTC; convención de este proyecto: 00:00 UTC)
            var deliveryDateUtc = UtcKey(dayKey);

            // 5) Meal para congelar precio
            var meal = await _meals.Find(m => m.Id == mealId).FirstOrDefaultAsync();
            if (meal == null) return (false, "Meal not found.", null);

            // 6) Upsert por llave lógica
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
                    .Set(o => o.PriceAtOrder, meal.Price)   // actualizar precio al editar
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

        // Cancelar orden (por llave lógica). Solo antes del cutoff.
        public async Task<(bool ok, string message)> CancelOrderAsync(
            string customerId,
            string cookId,
            DateTime deliveryDateLocal) // Kind Unspecified
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

        // DTO para pintar en la UI la semana con selección del usuario
        public class MenuDayWithSelection
        {
            public MenuDay Day { get; set; } = default!;
            public Order? MyOrder { get; set; }
            public string? SelectedMealId => MyOrder?.MealId;
            public bool CanCancel => MyOrder != null && CustomerOrdersService.CanCancel(MyOrder);

            // NUEVO: platos hidratados desde la colección meals
            public List<DishInfo> DishesHydrated { get; set; } = new();
        }

        // Retorna la semana de menús de un cook y las órdenes del customer para esa semana
        public async Task<List<MenuDayWithSelection>> GetWeekWithSelectionsAsync(
            string customerId,
            string cookId,
            DateTime weekStartLocal) // Kind Unspecified (lunes local)
        {
            var startLocal = NormalizeLocalDate(weekStartLocal);
            var endLocal = startLocal.AddDays(7);

            var days = await _menuDays.Find(m => m.CookId == cookId &&
                                                 m.Date >= startLocal &&
                                                 m.Date < endLocal)
                                      .SortBy(m => m.Date)
                                      .ToListAsync();

            // Rango para DeliveryDateUtc usando la convención (00:00 UTC por día local)
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
                            CookName = mm?.CookName,
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

        public static bool CanCancel(Order o) => DateTime.UtcNow <= o.CancelUntilUtc;

        public static DateTime NormalizeLocalDate(DateTime localDate)
            => new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Unspecified);

        public static DateTime UtcKey(DateTime localDate) // convención del proyecto
            => new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Utc);

        private static TimeZoneInfo ResolveTimeZone(string tzId)
        {
            try { return TZConvert.GetTimeZoneInfo(tzId); }
            catch
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(tzId); }
                catch { return TimeZoneInfo.Utc; }
            }
        }

        private static DateTime ComputeCancelUntilUtc(DateTime dayLocal, TimeZoneInfo tz)
        {
            var d = NormalizeLocalDate(dayLocal);
            var cutoffLocal = new DateTime(d.Year, d.Month, d.Day, 8, 0, 0, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(cutoffLocal, tz);
        }
    }
}