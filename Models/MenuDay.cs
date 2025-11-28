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
        public int Index { get; set; } // 1, 2, 3
        public string Name { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    public class MenuDay
    {
        public int Id { get; set; }
        public DateTime Date { get; set; } // solo fecha
        public MenuDayStatus Status { get; set; } = MenuDayStatus.Draft;
        public List<MenuDish> Dishes { get; set; } = new();
        public DateTime? PublishedAt { get; set; }
        public DateTime? ClosedAt { get; set; }

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
                .OrderBy(d => d.Index)
                .Take(3)
                .ToList();
        }
    }
}
