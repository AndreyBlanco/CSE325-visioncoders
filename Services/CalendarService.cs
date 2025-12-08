using System;
using System.Collections.Generic;
using System.Linq;
using CSE325_visioncoders.Models;

namespace CSE325_visioncoders.Services
{
    public class CalendarService
    {
        private readonly List<MenuDay> _menuDays = new();
        private readonly List<Confirmation> _confirmations = new();
        private readonly List<Client> _clients = new();
        private readonly List<CalendarEvent> _events = new();

        private int _nextMenuDayId = 1;
        private int _nextConfirmationId = 1;
        private int _nextEventId = 1;

        // Hora límite 08:00
        private readonly TimeSpan CutoffTime = new(8, 0, 0);

        public CalendarService()
        {
            // Clientes de ejemplo
            _clients = new()
            {
                new Client { Id = "c1", Name = "Juan Pérez" },
                new Client { Id = "c2", Name = "María González" },
                new Client { Id = "c3", Name = "Luis Ortega" },
                new Client { Id = "c4", Name = "Carolina López" }
            };
        }

        public IEnumerable<Client> GetClients() => _clients;

        /* ============================================================
           CUT-OFF RULES
        ============================================================ */

        public bool IsDayClosed(DateTime date)
        {
            date = date.Date;
            var now = DateTime.Now;

            // Días pasados siempre cerrados
            if (date < DateTime.Today)
                return true;

            // Hoy se cierra a las 08:00
            if (date == DateTime.Today && now.TimeOfDay >= CutoffTime)
                return true;

            return false;
        }

        private void ApplyAutoCutoff(MenuDay menuDay)
        {
            if (IsDayClosed(menuDay.Date))
            {
                if (menuDay.Status != MenuDayStatus.Closed)
                {
                    menuDay.Status = MenuDayStatus.Closed;
                    menuDay.ClosedAt = DateTime.Now;
                }
            }
        }

        /* ============================================================
           MENU DAYS
        ============================================================ */

        public IEnumerable<MenuDay> GetMenuForRange(DateTime start, DateTime end)
        {
            var s = start.Date;
            var e = end.Date;

            var list = _menuDays
                .Where(m => m.Date >= s && m.Date <= e)
                .OrderBy(m => m.Date)
                .ToList();

            // Mantener siempre coherente: 3 platos, contadores y cutoff
            foreach (var m in list)
            {
                m.EnsureThreeDishes();
                UpdateConfirmationCounters(m);
                ApplyAutoCutoff(m);
            }

            return list
                .Select(CloneMenuDay)
                .ToList();
        }

        public MenuDay GetOrCreateMenuDay(DateTime date)
        {
            date = date.Date;

            var existing = _menuDays.FirstOrDefault(m => m.Date == date);

            if (existing != null)
            {
                existing.EnsureThreeDishes();
                UpdateConfirmationCounters(existing);
                ApplyAutoCutoff(existing);
                return CloneMenuDay(existing);
            }

            var menuDay = new MenuDay
            {
                Id = _nextMenuDayId++,
                Date = date,
                Status = MenuDayStatus.Draft
            };

            menuDay.EnsureThreeDishes();
            _menuDays.Add(menuDay);

            ApplyAutoCutoff(menuDay);

            return CloneMenuDay(menuDay);
        }

        public void SaveMenuDay(MenuDay updated)
        {
            if (updated == null) return;

            var date = updated.Date.Date;

            if (IsDayClosed(date))
                throw new Exception("This day is closed due to cutoff.");

            updated.EnsureThreeDishes();

            var existing = _menuDays.FirstOrDefault(m => m.Id == updated.Id);

            if (existing == null)
            {
                updated.Id = _nextMenuDayId++;
                _menuDays.Add(CloneMenuDay(updated));
            }
            else
            {
                var wasPublished = existing.Status == MenuDayStatus.Published;

                existing.Status = updated.Status;
                existing.Dishes = updated.Dishes
                    .OrderBy(x => x.Index)
                    .Select(x => new MenuDish
                    {
                        Index = x.Index,
                        MealId = x.MealId,
                        Name = x.Name,
                        Notes = x.Notes
                    })
                    .ToList();

                // Marca PublishedAt cuando se publica por primera vez
                if (!wasPublished && existing.Status == MenuDayStatus.Published)
                {
                    existing.PublishedAt = DateTime.Now;
                }

                existing.EnsureThreeDishes();
                ApplyAutoCutoff(existing);
            }
        }

        /* ============================================================
           CONFIRMATIONS
        ============================================================ */

        public Confirmation AddConfirmation(string clientId, DateTime date, int dishIndex)
        {
            if (IsDayClosed(date))
                throw new Exception("Confirmations closed.");

            var conf = new Confirmation
            {
                Id = _nextConfirmationId++,
                ClientId = clientId,
                Date = date.Date,
                DishIndex = dishIndex,
                Status = ConfirmationStatus.Confirmed,
                CreatedAt = DateTime.Now
            };

            _confirmations.Add(conf);

            var menu = GetOrCreateMenuDay(date);
            UpdateConfirmationCounters(menu);

            return conf;
        }

        public bool CancelConfirmation(int confirmationId)
        {
            var conf = _confirmations.FirstOrDefault(c => c.Id == confirmationId);
            if (conf == null) return false;
            if (IsDayClosed(conf.Date)) return false;

            conf.Status = ConfirmationStatus.Canceled;
            conf.CanceledAt = DateTime.Now;

            var menu = GetOrCreateMenuDay(conf.Date);
            UpdateConfirmationCounters(menu);

            return true;
        }

        public IEnumerable<Confirmation> GetConfirmationsByDay(DateTime date)
        {
            var d = date.Date;
            return _confirmations
                .Where(c => c.Date == d && c.Status == ConfirmationStatus.Confirmed)
                .ToList();
        }

        public Dictionary<int, int> GetConfirmationsByDish(DateTime date)
        {
            var d = date.Date;

            return _confirmations
                .Where(c => c.Date == d && c.Status == ConfirmationStatus.Confirmed)
                .GroupBy(c => c.DishIndex)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        private void UpdateConfirmationCounters(MenuDay menuDay)
        {
            var d = menuDay.Date;

            menuDay.ConfirmationsCount = _confirmations
                .Count(c => c.Date == d && c.Status == ConfirmationStatus.Confirmed);
        }

        /* ============================================================
           EVENTS
        ============================================================ */

        public CalendarEvent CreateEvent(CalendarEvent ev)
        {
            ValidateEvent(ev);

            ev.Id = _nextEventId++;
            _events.Add(ev);
            return ev;
        }

        public IEnumerable<CalendarEvent> GetEventsByDay(DateTime date)
        {
            var d = date.Date;

            return _events
                .Where(e => e.Start.Date <= d && e.End.Date >= d)
                .OrderBy(e => e.Start)
                .ToList();
        }

        public IEnumerable<CalendarEvent> GetEventsRange(DateTime start, DateTime end)
        {
            var s = start.Date;
            var e = end.Date;

            return _events
                .Where(ev => ev.Start.Date <= e && ev.End.Date >= s)
                .OrderBy(ev => ev.Start)
                .ToList();
        }

        public bool UpdateEvent(CalendarEvent ev)
        {
            ValidateEvent(ev);

            var existing = _events.FirstOrDefault(x => x.Id == ev.Id);
            if (existing == null) return false;

            existing.Title = ev.Title;
            existing.Description = ev.Description;
            existing.Start = ev.Start;
            existing.End = ev.End;
            existing.Category = ev.Category;
            existing.Priority = ev.Priority;
            existing.Status = ev.Status;
            existing.Assignees = ev.Assignees.ToList();
            existing.RecurrenceRule = ev.RecurrenceRule;

            return true;
        }

        public bool DeleteEvent(int id)
        {
            var ev = _events.FirstOrDefault(x => x.Id == id);
            if (ev == null) return false;

            _events.Remove(ev);
            return true;
        }

        // Validación sencilla: si End < Start, corrige a 1 hora de duración
        private void ValidateEvent(CalendarEvent ev)
        {
            if (ev.End < ev.Start)
            {
                ev.End = ev.Start.AddHours(1);
            }
        }

        /* ============================================================
           CLONE
        ============================================================ */

        private static MenuDay CloneMenuDay(MenuDay src)
        {
            return new MenuDay
            {
                Id = src.Id,
                Date = src.Date,
                Status = src.Status,
                PublishedAt = src.PublishedAt,
                ClosedAt = src.ClosedAt,
                ConfirmationsCount = src.ConfirmationsCount,
                Dishes = src.Dishes
                    .OrderBy(d => d.Index)
                    .Select(d => new MenuDish
                    {
                        Index = d.Index,
                        MealId = d.MealId,
                        Name = d.Name,
                        Notes = d.Notes
                    })
                    .ToList()
            };
        }
    }
}
