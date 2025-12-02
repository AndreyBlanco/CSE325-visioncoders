using System;
using System.Collections.Generic;

namespace CSE325_visioncoders.Models
{
    public enum CalendarEventCategory
    {
        Purchase,
        Delivery,
        Shift,
        Maintenance,
        Admin,
        Meeting,
        Other
    }

    public enum CalendarEventPriority
    {
        Low,
        Medium,
        High
    }

    public enum CalendarEventStatus
    {
        Planned,
        InProgress,
        Done,
        Canceled
    }

    public class CalendarEvent
    {
        public int Id { get; set; }

        public string Title { get; set; } = "";
        public string Description { get; set; } = "";

        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        public CalendarEventCategory Category { get; set; } = CalendarEventCategory.Other;
        public CalendarEventPriority Priority { get; set; } = CalendarEventPriority.Medium;
        public CalendarEventStatus Status { get; set; } = CalendarEventStatus.Planned;

        public List<string> Assignees { get; set; } = new();

        public string? RecurrenceRule { get; set; }
    }
}

