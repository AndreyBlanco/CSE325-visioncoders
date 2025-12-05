using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using TimeZoneConverter;
using CSE325_visioncoders.Models;

public static class DevSeederPreserve
{

    private class UserDoc
    {
        [BsonId, BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string PasswordHash { get; set; } = "dev";
        public string Role { get; set; } = "customer"; 
    }

    public static async Task RunAsync(IMongoDatabase db, bool preserve = true)
    {
        var usersCol = db.GetCollection<UserDoc>("users");
        var mealsCol = db.GetCollection<Meal>("meals");
        var menuDaysCol = db.GetCollection<MenuDay>("menu_days");
        var ordersCol = db.GetCollection<Order>("orders");
        var reviewsCol = db.GetCollection<MealReview>("reviews");

        await EnsureIndexesAsync(usersCol, mealsCol, menuDaysCol, ordersCol, reviewsCol);

        var cooksSeed = new[]
        {
            new UserDoc { Name = "Chef Ana",   Email = "ana@demo.local",   Role = "cook" },
            new UserDoc { Name = "Chef Bruno", Email = "bruno@demo.local", Role = "cook" }
        };
        var customersSeed = new[]
        {
            new UserDoc { Name = "Carla", Email = "carla@demo.local", Role = "customer" },
            new UserDoc { Name = "Diego", Email = "diego@demo.local", Role = "customer" },
            new UserDoc { Name = "Elena", Email = "elena@demo.local", Role = "customer" },
            new UserDoc { Name = "Fabio", Email = "fabio@demo.local", Role = "customer" },
            new UserDoc { Name = "Gina",  Email = "gina@demo.local",  Role = "customer" },
        };

        var usersMap = await UpsertUsersAsync(usersCol, cooksSeed.Concat(customersSeed));
        var cookAna = usersMap["ana@demo.local"];
        var cookBruno = usersMap["bruno@demo.local"];
        var customerIds = customersSeed.Select(c => usersMap[c.Email].Id!).ToList();

        var mealsAna = await UpsertMealsForCookAsync(mealsCol, cookAna.Id!, "Chef Ana");
        var mealsBruno = await UpsertMealsForCookAsync(mealsCol, cookBruno.Id!, "Chef Bruno");

        var tzId = "America/Bogota";
        var tz = ResolveTimeZone(tzId);
        var daysLocal = NextBusinessWeekLocal(tz); 

        var menusAna = await UpsertWeeklyMenuAsync(menuDaysCol, cookAna.Id!, tzId, daysLocal, mealsAna);
        var menusBruno = await UpsertWeeklyMenuAsync(menuDaysCol, cookBruno.Id!, tzId, daysLocal, mealsBruno);

        var rnd = new Random();
        foreach (var customerId in customerIds)
        {
            var pickDaysAna = daysLocal.OrderBy(_ => rnd.Next()).Take(rnd.Next(1, 3)).ToList();
            foreach (var d in pickDaysAna)
                await CreateOrderIfMissingAsync(ordersCol, menuDaysCol, mealsCol, cookAna.Id!, customerId, d, tzId);

            var pickDaysBruno = daysLocal.OrderBy(_ => rnd.Next()).Take(1).ToList();
            foreach (var d in pickDaysBruno)
                await CreateOrderIfMissingAsync(ordersCol, menuDaysCol, mealsCol, cookBruno.Id!, customerId, d, tzId);
        }

        var allMeals = mealsAna.Concat(mealsBruno).ToList();
        var somePairs = customerIds
            .SelectMany(c => allMeals.OrderBy(_ => Guid.NewGuid()).Take(2).Select(m => (c, m)))
            .ToList();

        foreach (var (userId, meal) in somePairs)
        {
            var exists = await reviewsCol.Find(r => r.MealId == meal.Id && r.UserId == userId).AnyAsync();
            if (exists) continue;

            var review = new MealReview
            {
                MealId = meal.Id!,
                UserId = userId,
                UserName = "Demo User",
                Rating = rnd.Next(3, 6),
                Comment = "Muy rico!",
                CreatedAt = DateTime.UtcNow
            };
            await reviewsCol.InsertOneAsync(review);
        }
    }

    private static async Task EnsureIndexesAsync(
        IMongoCollection<UserDoc> usersCol,
        IMongoCollection<Meal> mealsCol,
        IMongoCollection<MenuDay> menuDaysCol,
        IMongoCollection<Order> ordersCol,
        IMongoCollection<MealReview> reviewsCol)
    {
        try
        {
            await usersCol.Indexes.CreateOneAsync(
                new CreateIndexModel<UserDoc>(
                    Builders<UserDoc>.IndexKeys.Ascending(u => u.Email),
                    new CreateIndexOptions { Unique = true, Name = "ux_users_email" }));
        }
        catch { /* ya existe */ }

        try
        {
            await mealsCol.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<Meal>(
                    Builders<Meal>.IndexKeys.Ascending(m => m.CookId).Ascending(m => m.Name),
                    new CreateIndexOptions { Unique = true, Name = "ux_meals_cook_name" }),
                new CreateIndexModel<Meal>(
                    Builders<Meal>.IndexKeys.Ascending(m => m.CookId),
                    new CreateIndexOptions { Name = "ix_meals_cook" }),
                new CreateIndexModel<Meal>(
                    Builders<Meal>.IndexKeys.Ascending(m => m.IsActive),
                    new CreateIndexOptions { Name = "ix_meals_active" })
            });
        }
        catch { /* ya existen */ }

        try
        {
            await menuDaysCol.Indexes.CreateOneAsync(
                new CreateIndexModel<MenuDay>(
                    Builders<MenuDay>.IndexKeys.Ascending(m => m.CookId).Ascending(m => m.Date),
                    new CreateIndexOptions { Unique = true, Name = "ux_menudays_cook_date" }));
        }
        catch { /* puede fallar si ya existe o hay duplicados; el seed seguirá */ }

        try
        {
            await ordersCol.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<Order>(
                    Builders<Order>.IndexKeys
                        .Ascending(o => o.CustomerId)
                        .Ascending(o => o.CookId)
                        .Ascending(o => o.DeliveryDateUtc),
                    new CreateIndexOptions { Unique = true, Name = "ux_orders_customer_cook_date" }),
                new CreateIndexModel<Order>(
                    Builders<Order>.IndexKeys
                        .Ascending(o => o.CookId)
                        .Ascending(o => o.DeliveryDateUtc),
                    new CreateIndexOptions { Name = "ix_orders_cook_date" }),
                new CreateIndexModel<Order>(
                    Builders<Order>.IndexKeys
                        .Ascending(o => o.CustomerId)
                        .Ascending(o => o.DeliveryDateUtc),
                    new CreateIndexOptions { Name = "ix_orders_customer_date" })
            });
        }
        catch { /* ya existen */ }

        try
        {
            await reviewsCol.Indexes.CreateManyAsync(new[]
            {
            new CreateIndexModel<MealReview>(
                Builders<MealReview>.IndexKeys.Ascending(r => r.MealId),
                new CreateIndexOptions { Name = "ix_reviews_meal" }),
            new CreateIndexModel<MealReview>(
                Builders<MealReview>.IndexKeys.Ascending(r => r.MealId).Ascending(r => r.UserId),
                new CreateIndexOptions { Unique = true, Name = "ux_reviews_meal_user" })
            });
        }
        catch { /* ya existen */ }
    }

    private static async Task<Dictionary<string, UserDoc>> UpsertUsersAsync(
        IMongoCollection<UserDoc> usersCol,
        IEnumerable<UserDoc> users)
    {
        var map = new Dictionary<string, UserDoc>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in users)
        {
            var existing = await usersCol.Find(x => x.Email == u.Email).FirstOrDefaultAsync();
            if (existing == null)
            {
                await usersCol.InsertOneAsync(u);
                existing = u;
            }
            map[u.Email] = existing;
        }
        return map;
    }

    private static async Task<List<Meal>> UpsertMealsForCookAsync(
        IMongoCollection<Meal> mealsCol,
        string cookId,
        string cookName)
    {
        var templates = new (string Name, string Desc, string Ing, decimal Price)[]
        {
            ("Pollo a la plancha", "Pechuga con especias y ensalada", "Pollo, lechuga, tomate", 5.50m),
            ("Lasaña de carne", "Lasaña casera con queso", "Pasta, carne, queso", 6.75m),
            ("Ensalada César", "Clásica con aderezo", "Lechuga, crutones, parmesano", 4.25m),
            ("Arroz con camarones", "Arroz salteado con mariscos", "Arroz, camarón, verduras", 7.20m),
            ("Sopa de tomate", "Cremosa y casera", "Tomate, crema, albahaca", 3.90m),
            ("Tacos de res", "Con pico de gallo", "Tortilla, res, salsa", 5.80m),
            ("Curry de garbanzos", "Estilo hindú", "Garbanzos, curry, coco", 5.10m),
            ("Pescado al horno", "Con limón y hierbas", "Pescado, limón, hierbas", 7.50m),
            ("Pasta al pesto", "Albahaca fresca", "Pasta, pesto, piñones", 5.60m),
            ("Bowl vegetariano", "Quinoa y vegetales", "Quinoa, vegetales", 5.40m),
        };

        var result = new List<Meal>();
        foreach (var t in templates)
        {
            var filter = Builders<Meal>.Filter.Eq(m => m.CookId, cookId) &
                         Builders<Meal>.Filter.Eq(m => m.Name, t.Name);

            var existing = await mealsCol.Find(filter).FirstOrDefaultAsync();
            if (existing == null)
            {
                var meal = new Meal
                {
                    CookId = cookId,
                    CookName = cookName,
                    Name = t.Name,
                    Description = t.Desc,
                    Ingredients = t.Ing,
                    Price = t.Price,
                    ImageUrl = null,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsActive = true
                };
                await mealsCol.InsertOneAsync(meal);
                existing = meal;
            }
            else
            {
                if (!existing.IsActive)
                {
                    var update = Builders<Meal>.Update.Set(m => m.IsActive, true).Set(m => m.UpdatedAt, DateTime.UtcNow);
                    await mealsCol.UpdateOneAsync(filter, update);
                    existing.IsActive = true;
                }
            }

            result.Add(existing);
        }

        return result;
    }

    private static async Task<List<MenuDay>> UpsertWeeklyMenuAsync(
        IMongoCollection<MenuDay> menuDaysCol,
        string cookId,
        string tzId,
        List<DateTime> daysLocal,
        List<Meal> availableMealsForCook)
    {
        var rnd = new Random();
        var results = new List<MenuDay>();

        foreach (var localDate in daysLocal)
        {
            var dateKey = NormalizeLocalDate(localDate);

            var filter = Builders<MenuDay>.Filter.Eq(m => m.CookId, cookId) &
                         Builders<MenuDay>.Filter.Eq(m => m.Date, dateKey);

            var existing = await menuDaysCol.Find(filter).FirstOrDefaultAsync();
            if (existing == null)
            {
                var threeMeals = availableMealsForCook.OrderBy(_ => rnd.Next()).Take(3).ToList();

                var md = new MenuDay
                {
                    Id = ComputeMenuDayKey(cookId, dateKey),  // ← evita _id duplicado 0
                    CookId = cookId,
                    TimeZone = tzId,
                    Date = dateKey,
                    Status = MenuDayStatus.Published,
                    PublishedAt = DateTime.UtcNow,
                    Dishes = new List<MenuDish>
                    {
                        new MenuDish { Index = 1, MealId = threeMeals[0].Id!, Name = threeMeals[0].Name },
                        new MenuDish { Index = 2, MealId = threeMeals[1].Id!, Name = threeMeals[1].Name },
                        new MenuDish { Index = 3, MealId = threeMeals[2].Id!, Name = threeMeals[2].Name }
                    },
                    ConfirmationsCount = 0
                };
                md.EnsureThreeDishes();
                await menuDaysCol.InsertOneAsync(md);
                results.Add(md);
            }
            else
            {
                existing.TimeZone = string.IsNullOrWhiteSpace(existing.TimeZone) ? tzId : existing.TimeZone;
                existing.Status = existing.Status == MenuDayStatus.Draft ? MenuDayStatus.Published : existing.Status;
                if (existing.Status == MenuDayStatus.Published && existing.PublishedAt == null)
                    existing.PublishedAt = DateTime.UtcNow;

                existing.EnsureThreeDishes();
                var present = new HashSet<string>(
                    existing.Dishes.Where(d => !string.IsNullOrWhiteSpace(d.MealId)).Select(d => d.MealId),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var dish in existing.Dishes)
                {
                    if (string.IsNullOrWhiteSpace(dish.MealId))
                    {
                        var candidate = availableMealsForCook.FirstOrDefault(m => !present.Contains(m.Id!));
                        if (candidate == null)
                            candidate = availableMealsForCook.OrderBy(_ => rnd.Next()).First();

                        dish.MealId = candidate.Id!;
                        if (string.IsNullOrWhiteSpace(dish.Name))
                            dish.Name = candidate.Name;

                        present.Add(candidate.Id!);
                    }
                }

                var update = Builders<MenuDay>.Update
                    .Set(m => m.TimeZone, existing.TimeZone)
                    .Set(m => m.Status, existing.Status)
                    .Set(m => m.PublishedAt, existing.PublishedAt)
                    .Set(m => m.Dishes, existing.Dishes);

                await menuDaysCol.UpdateOneAsync(filter, update);
                results.Add(existing);
            }
        }

        return results;
    }

    private static async Task CreateOrderIfMissingAsync(
        IMongoCollection<Order> ordersCol,
        IMongoCollection<MenuDay> menuDaysCol,
        IMongoCollection<Meal> mealsCol,
        string cookId,
        string customerId,
        DateTime dayLocal,      
        string tzId)
    {
        var dateKey = NormalizeLocalDate(dayLocal);

        var menuFilter = Builders<MenuDay>.Filter.Eq(m => m.CookId, cookId) &
                         Builders<MenuDay>.Filter.Eq(m => m.Date, dateKey);

        var menu = await menuDaysCol.Find(menuFilter).FirstOrDefaultAsync();
        if (menu == null || menu.Dishes.Count == 0)
            return;

        var dish = menu.Dishes.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.MealId))
                   ?? menu.Dishes.First();

        var deliveryDateUtc = new DateTime(dateKey.Year, dateKey.Month, dateKey.Day, 0, 0, 0, DateTimeKind.Utc);

        var existsFilter = Builders<Order>.Filter.Eq(o => o.CustomerId, customerId) &
                           Builders<Order>.Filter.Eq(o => o.CookId, cookId) &
                           Builders<Order>.Filter.Eq(o => o.DeliveryDateUtc, deliveryDateUtc);

        var exists = await ordersCol.Find(existsFilter).AnyAsync();
        if (exists) return;

        var meal = await mealsCol.Find(m => m.Id == dish.MealId).FirstOrDefaultAsync();
        if (meal == null) return;

        var tz = ResolveTimeZone(tzId);

        var cutoffLocal = new DateTime(dateKey.Year, dateKey.Month, dateKey.Day, 8, 0, 0, DateTimeKind.Unspecified);
        var cancelUntilUtc = TimeZoneInfo.ConvertTimeToUtc(cutoffLocal, tz);

        var order = new Order
        {
            CookId = cookId,
            CustomerId = customerId,
            MealId = dish.MealId,
            DeliveryDateUtc = deliveryDateUtc,
            CancelUntilUtc = cancelUntilUtc,
            TimeZone = tzId,
            PriceAtOrder = meal.Price,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Notes = null
        };

        await ordersCol.InsertOneAsync(order);
    }

    
    private static TimeZoneInfo ResolveTimeZone(string tzId)
    {
        return TZConvert.GetTimeZoneInfo(tzId);
    }

    private static List<DateTime> NextBusinessWeekLocal(TimeZoneInfo tz)
    {
       
        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        int daysToMonday = ((int)DayOfWeek.Monday - (int)nowLocal.DayOfWeek + 7) % 7;
        if (daysToMonday == 0) daysToMonday = 7; 

        var nextMondayLocal = nowLocal.Date.AddDays(daysToMonday);
        
        return Enumerable.Range(0, 5)
            .Select(d => NormalizeLocalDate(nextMondayLocal.AddDays(d)))
            .ToList();
    }

    private static DateTime NormalizeLocalDate(DateTime localDate)
    {
        return new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
    }

    private static int ComputeMenuDayKey(string cookId, DateTime dateKey)
    {
        var s = $"{cookId}|{dateKey:yyyy-MM-dd}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        var hash = System.Security.Cryptography.SHA1.HashData(bytes);
        return BitConverter.ToInt32(hash, 0);
    }


}