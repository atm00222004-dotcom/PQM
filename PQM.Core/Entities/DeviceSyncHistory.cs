using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PQM.Core.Entities
{
    public class DeviceSyncHistory
    {
        [Key]
        public long Id { get; set; }

        public int DeviceId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public required string Status { get; set; }  // 'Running', 'Success', 'Failed', 'TimedOut'
        public string? ErrorMessage { get; set; }
        public int? ProfilesRead { get; set; }
        public int? RowsWritten { get; set; }
    }
}
