using CSE325_visioncoders.Models;
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
    }
}