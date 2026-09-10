using System;
using System.Collections.Generic;

namespace PQM.Core.DTOs
{
    public class DueScheduleItem
    {
        public int ScheduleId { get; set; }

        public TimeSpan ScheduledTime { get; set; }

        public string RepeatMode { get; set; } = "Daily";

        public string TimeZoneId { get; set; } = "India Standard Time";

        public List<int> DeviceIds { get; set; } = new();
    }

    public class UpdateScheduleRequest
    {
        public bool IsEnabled { get; set; }

        public string ScheduledTime { get; set; }= "00:00";

        public string RepeatMode { get; set; }= "Daily";
    }
}

