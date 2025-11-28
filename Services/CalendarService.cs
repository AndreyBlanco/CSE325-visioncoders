using System;
using System.Collections.Generic;
using System.Linq;
using CSE325_visioncoders.Models;

namespace CSE325_visioncoders.Services
{
    public class CalendarService
    {
        private readonly List<MenuDay> _menuDays = new();
        private int _nextMenuDayId = 1;

        public IEnumerable<MenuDay> GetMenuForRange(DateTime startDate, DateTime endDate)
        {
            var start = startDate.Date;
            var end = endDate.Date;

            return _menuDays
                .Where(m => m.Date >= start && m.Date <= end)
                .OrderBy(m => m.Date)
                .ToList();
        }

        public MenuDay GetOrCreateMenuDay(DateTime date)
        {
            date = date.Date;

            var existing = _menuDays.FirstOrDefault(m => m.Date == date);
            if (existing != null)
            {
                existing.EnsureThreeDishes(); // <- IMPORTANTE
                return existing;
            }

            var menuDay = new MenuDay
            {
                Id = _nextMenuDayId++,
                Date = date,
                Status = MenuDayStatus.Draft
            };

            menuDay.EnsureThreeDishes(); // <- IMPORTANTE
            _menuDays.Add(menuDay);

            return menuDay;
        }

        public void SaveMenuDay(MenuDay updated)
        {
            updated.Date = updated.Date.Date;

            var existing = _menuDays.FirstOrDefault(m => m.Id == updated.Id);

            if (existing == null)
            {
                updated.Id = _nextMenuDayId++;
                updated.EnsureThreeDishes();
                _menuDays.Add(updated);
                return;
            }

            existing.Status = updated.Status;
            existing.PublishedAt = updated.PublishedAt;
            existing.ClosedAt = updated.ClosedAt;

            existing.Dishes.Clear();
            foreach (var d in updated.Dishes.OrderBy(d => d.Index))
            {
                existing.Dishes.Add(new MenuDish
                {
                    Index = d.Index,
                    Name = d.Name ?? "",
                    Notes = d.Notes ?? ""
                });
            }

            existing.EnsureThreeDishes();
        }
    }
}
