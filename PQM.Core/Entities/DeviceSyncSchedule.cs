using System;
using System.ComponentModel.DataAnnotations;
namespace PQM.Core.Entities
{
    public class DeviceSyncSchedule
    {
        [Key]
        public int Id { get; set; }
        public bool IsEnabled { get; set; }
        public TimeSpan ScheduledTime { get; set; }
        public string RepeatMode { get; set; } = "Daily";
        public DateTime? NextRunAtUtc { get; set; }
        public DateTime? LastRunAtUtc { get; set; }
        public string? LastRunStatus { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public ICollection<Device> Devices { get; set; } = new List<Device>();
    }
}
