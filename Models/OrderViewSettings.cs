namespace CSE325_visioncoders.Models;

using System;

public class OrderViewSettings
{
   public TimeOnly DayStart { get; set; } = new(8, 0);

    public TimeOnly NoonSwitch { get; set; } = new(12, 0);


    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
}