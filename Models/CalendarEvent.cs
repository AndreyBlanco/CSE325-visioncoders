using System;
using System.Collections.Generic;

namespace CSE325_visioncoders.Models
{
    /// <summary>
    /// High-level classification for events displayed in the calendar.
    /// Categories help the UI group and filter different operational activities.
    /// </summary>
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

    /// <summary>
    /// Represents how important or urgent an event is.
    /// Used for color-coding or prioritizing items in the calendar UI.
    /// </summary>
    public enum CalendarEventPriority
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// Defines the lifecycle state of a calendar event.
    /// Useful for tracking work progress or operational steps.
    /// </summary>
    public enum CalendarEventStatus
    {
        Planned,
        InProgress,
        Done,
        Canceled
    }

    /// <summary>
    /// Represents a scheduled event displayed on the calendar.
    /// This model is flexible and supports operational tasks,
    /// deliveries, shifts, meetings, or other internal activities.
    /// </summary>
    public class CalendarEvent
    {
        /// <summary>
        /// Auto-incremented unique identifier assigned by the service layer.
        /// Not a MongoDB ObjectId, since events are managed in-memory.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Short title describing the event. Displayed prominently in UI.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Optional long description providing event details.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Start timestamp for the event (local datetime).
        /// </summary>
        public DateTime Start { get; set; }

        /// <summary>
        /// End timestamp for the event (local datetime).
        /// Must be validated to exceed Start.
        /// </summary>
        public DateTime End { get; set; }

        /// <summary>
        /// Category used for grouping, filtering, or color coding events.
        /// </summary>
        public CalendarEventCategory Category { get; set; } = CalendarEventCategory.Other;

        /// <summary>
        /// Importance indicator used by the UI for prioritization.
        /// </summary>
        public CalendarEventPriority Priority { get; set; } = CalendarEventPriority.Medium;

        /// <summary>
        /// Tracks the current progress of the event (planned → done).
        /// </summary>
        public CalendarEventStatus Status { get; set; } = CalendarEventStatus.Planned;

        /// <summary>
        /// List of user IDs assigned to this event.
        /// Supports multiple participants or responsible team members.
        /// </summary>
        public List<string> Assignees { get; set; } = new();

        /// <summary>
        /// Optional recurrence rule (e.g., "weekly", "monthly").
        /// Enables repeated events without storing duplicates.
        /// </summary>
        public string? RecurrenceRule { get; set; }
    }
}
