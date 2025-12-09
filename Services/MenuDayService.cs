using CSE325_visioncoders.Models;
using System.Security.Cryptography;
using MongoDB.Driver;

namespace CSE325_visioncoders.Services
{
    public class MenuDayService
    {
        private readonly IMongoCollection<MenuDay> _menuDays;

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
            catch {  }
        }

        public async Task<List<MenuDay>> GetWeekByCookAsync(string cookId, DateTime weekStartLocal)
        {
            var start = NormalizeLocalDate(weekStartLocal);
            var end = start.AddDays(7);

            var days = await _menuDays.Find(m => m.CookId == cookId && m.Date >= start && m.Date < end)
                                      .SortBy(m => m.Date)
                                      .ToListAsync();
            return days;
        }

        public static DateTime NormalizeLocalDate(DateTime localDate)
            => new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Unspecified);

        // Dentro de la clase MenuDayService

        public async Task UpsertMenuDayAsync(MenuDay source, string cookId, string tzId)
        {
            if (string.IsNullOrWhiteSpace(cookId))
                throw new InvalidOperationException("CookId is required.");

            // Normaliza fecha local a medianoche (Kind Unspecified)
            var dateKey = NormalizeLocalDate(source.Date);

            // Asegura formateo de 3 platos 1..3
            source.EnsureThreeDishes();

            // Si quieres persistir solamente Index y MealId, limpias Name/Notes (opcional)
            var dishes = source.Dishes
                .OrderBy(d => d.Index)
                .Take(3)
                .Select(d => new MenuDish
                {
                    Index = d.Index,
                    MealId = d.MealId,
                    // Name = "", Notes = "" // si prefieres no guardar estos campos
                    Name = d.Name,
                    Notes = d.Notes
                })
                .ToList();

            // Busca si ya existe (clave lógica CookId+Date)
            var filter = Builders<MenuDay>.Filter.Eq(m => m.CookId, cookId) &
                         Builders<MenuDay>.Filter.Eq(m => m.Date, dateKey);

            var existing = await _menuDays.Find(filter).FirstOrDefaultAsync();

            if (existing == null)
            {
                var toInsert = new MenuDay
                {
                    // _id (int) único por cook+day (evita duplicados)
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

        // Helper: _id determinista (int) por CookId+Date
        private static int ComputeMenuDayKey(string cookId, DateTime dateKey)
        {
            var s = $"{cookId}|{dateKey:yyyy-MM-dd}";
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            var hash = SHA1.HashData(bytes);
            return BitConverter.ToInt32(hash, 0);
        }
    }
}