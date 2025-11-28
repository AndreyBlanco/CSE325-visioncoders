using System;
namespace CSE325_visioncoders.Models
{
    public enum EventCategory
    {
        Purchase,
        Shift,
        Delivery,
        Maintenance,
        Admin,
        Other
    }

    public class CalendarEvent
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime Start { get; set; }

        public DateTime End { get; set; }

        public EventCategory Category { get; set; } = EventCategory.Other;

        public bool AllDay { get; set; } = true;
    }
}
