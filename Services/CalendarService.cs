using System;
using System.Collections.Generic;
using System.Linq;
using CSE325_visioncoders.Models;

namespace CSE325_visioncoders.Services
{
    /// <summary>
    /// Core service responsible for managing menu days, confirmations,
    /// and calendar events inside LunchMate.  
    /// This service operates entirely in-memory and simulates persistence
    /// for demo and UI development purposes.
    /// </summary>
    public class CalendarService
    {
        // Simulated data storage (in a real application this would be a database)
        private readonly List<MenuDay> _menuDays = new();
        private readonly List<Confirmation> _confirmations = new();
        private readonly List<Client> _clients = new();
        private readonly List<CalendarEvent> _events = new();

        // Internal counters used to generate unique IDs
        private int _nextMenuDayId = 1;
        private int _nextConfirmationId = 1;
        private int _nextEventId = 1;

        // Daily order cutoff time: after 08:00 AM customers cannot confirm/cancel
        private readonly TimeSpan CutoffTime = new(8, 0, 0);

        /// <summary>
        /// Initializes the service with example clients to support the UI workflow.
        /// This avoids empty states while the team builds the front-end experience.
        /// </summary>
        public CalendarService()
        {
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
           CUT-OFF LOGIC
           Rules that determine when a customer can or cannot modify
           a meal confirmation.
        ============================================================ */

        /// <summary>
        /// Determines whether a given date should be considered closed
        /// for customer confirmations based on cutoff rules.
        /// Past dates are always closed; today closes after 08:00 AM.
        /// </summary>
        public bool IsDayClosed(DateTime date)
        {
            date = date.Date;
            var now = DateTime.Now;

            if (date < DateTime.Today)
                return true;

            if (date == DateTime.Today && now.TimeOfDay >= CutoffTime)
                return true;

            return false;
        }

        /// <summary>
        /// Automatically updates a MenuDay’s status if the cutoff time has passed.
        /// Ensures UI always stays consistent without requiring user actions.
        /// </summary>
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

        /// <summary>
        /// Retrieves all MenuDay entries within a date range.  
        /// Each MenuDay is normalized before returning:
        /// - Ensures exactly 3 dish slots
        /// - Recounts confirmations
        /// - Applies cutoff rules
        /// A clone is returned to protect internal state.
        /// </summary>
        public IEnumerable<MenuDay> GetMenuForRange(DateTime start, DateTime end)
        {
            var s = start.Date;
            var e = end.Date;

            var list = _menuDays
                .Where(m => m.Date >= s && m.Date <= e)
                .OrderBy(m => m.Date)
                .ToList();

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

        /// <summary>
        /// Gets an existing MenuDay or creates a new draft day automatically.
        /// Used heavily by the UI to ensure the calendar is always populated.
        /// </summary>
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

        /// <summary>
        /// Saves a MenuDay after the cook publishes or updates the dishes.
        /// Enforces cutoff rules and captures publish timestamps.
        /// </summary>
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

                // Ensures stable ordering of dishes
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

                // Record when the day transitions into "Published"
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

        /// <summary>
        /// Adds a customer confirmation for a specific dish of a MenuDay.
        /// Enforces cutoff logic to prevent late confirmations.
        /// </summary>
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

        /// <summary>
        /// Cancels a confirmation if the day is not yet closed.
        /// Helps maintain accurate meal counts for the kitchen.
        /// </summary>
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

        /// <summary>
        /// Retrieves all active (non-canceled) confirmations for a specific day.
        /// Used by the kitchen to know how many meals to prepare.
        /// </summary>
        public IEnumerable<Confirmation> GetConfirmationsByDay(DateTime date)
        {
            var d = date.Date;
            return _confirmations
                .Where(c => c.Date == d && c.Status == ConfirmationStatus.Confirmed)
                .ToList();
        }

        /// <summary>
        /// Returns a count of confirmations grouped by dish index.
        /// Supports dish badges in the UI and informs kitchen planning.
        /// </summary>
        public Dictionary<int, int> GetConfirmationsByDish(DateTime date)
        {
            var d = date.Date;

            return _confirmations
                .Where(c => c.Date == d && c.Status == ConfirmationStatus.Confirmed)
                .GroupBy(c => c.DishIndex)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        /// <summary>
        /// Updates the total confirmation count displayed for a MenuDay.
        /// </summary>
        private void UpdateConfirmationCounters(MenuDay menuDay)
        {
            var d = menuDay.Date;

            menuDay.ConfirmationsCount = _confirmations
                .Count(c => c.Date == d && c.Status == ConfirmationStatus.Confirmed);
        }

        /* ============================================================
           EVENTS
        ============================================================ */

        /// <summary>
        /// Creates a new calendar event (non-meal related).  
        /// Basic validation ensures the event has a valid time range.
        /// </summary>
        public CalendarEvent CreateEvent(CalendarEvent ev)
        {
            ValidateEvent(ev);

            ev.Id = _nextEventId++;
            _events.Add(ev);
            return ev;
        }

        /// <summary>
        /// Retrieves events that overlap with a specific day.
        /// Supports additional UX components like schedule overlays.
        /// </summary>
        public IEnumerable<CalendarEvent> GetEventsByDay(DateTime date)
        {
            var d = date.Date;

            return _events
                .Where(e => e.Start.Date <= d && e.End.Date >= d)
                .OrderBy(e => e.Start)
                .ToList();
        }

        /// <summary>
        /// Retrieves all events within a date range.
        /// </summary>
        public IEnumerable<CalendarEvent> GetEventsRange(DateTime start, DateTime end)
        {
            var s = start.Date;
            var e = end.Date;

            return _events
                .Where(ev => ev.Start.Date <= e && ev.End.Date >= s)
                .OrderBy(ev => ev.Start)
                .ToList();
        }

        /// <summary>
        /// Updates a stored calendar event with new metadata.
        /// </summary>
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

        /// <summary>
        /// Removes an event permanently.
        /// </summary>
        public bool DeleteEvent(int id)
        {
            var ev = _events.FirstOrDefault(x => x.Id == id);
            if (ev == null) return false;

            _events.Remove(ev);
            return true;
        }

        /// <summary>
        /// Ensures event times are valid.  
        /// If an end time is earlier than the start, the system defaults to 1-hour duration.
        /// </summary>
        private void ValidateEvent(CalendarEvent ev)
        {
            if (ev.End < ev.Start)
            {
                ev.End = ev.Start.AddHours(1);
            }
        }

        /* ============================================================
           INTERNAL CLONE
           Protects internal data from being modified by UI components.
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
