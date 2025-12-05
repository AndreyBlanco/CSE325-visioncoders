using System;
using System.Collections.Generic;
using System.Linq;

namespace CSE325_visioncoders.Models
{
    public enum MenuDayStatus
    {
        Draft,
        Published,
        Closed
    }

    public class MenuDish
    {
        public int Index { get; set; }
        public string MealId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    public class MenuDay
    {
        public int Id { get; set; }

        public string CookId { get; set; } = string.Empty;
        public string TimeZone { get; set; } = "America/Bogota";

        public DateTime Date { get; set; }
        public MenuDayStatus Status { get; set; } = MenuDayStatus.Draft;

        public List<MenuDish> Dishes { get; set; } = new();

        public DateTime? PublishedAt { get; set; }
        public DateTime? ClosedAt { get; set; }

        // 🔥 ESTA ES LA PROPIEDAD QUE FALTABA
        public int ConfirmationsCount { get; set; } = 0;

        public void EnsureThreeDishes()
        {
            for (int i = 1; i <= 3; i++)
            {
                if (!Dishes.Any(d => d.Index == i))
                {
                    Dishes.Add(new MenuDish { Index = i });
                }
            }

            Dishes = Dishes
                .Where(d => d.Index >= 1 && d.Index <= 3)
                .OrderBy(d => d.Index)
                .Take(3)
                .ToList();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in Dishes)
            {
                d.MealId ??= string.Empty;
                if (!string.IsNullOrWhiteSpace(d.MealId))
                {
                    if (seen.Contains(d.MealId))
                        d.MealId = string.Empty;
                    else
                        seen.Add(d.MealId);
                }
            }
        }

        public DateTime GetCutoffUtc(TimeZoneInfo tz)
        {
            var local = Date.Date; // asumimos medianoche local del día
            var cutoffLocal = new DateTime(local.Year, local.Month, local.Day, 8, 0, 0, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(cutoffLocal, tz);
        }
    }
}
