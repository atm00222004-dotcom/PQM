using System;
using System.ComponentModel.DataAnnotations;

namespace PQM.Core.Entities
{
    public class DeviceProfileSyncState
    {
        [Key]
        public int Id { get; set; }
        public DateTime? LastReadTimestampUtc { get; set; }
        public DateTime LastSyncedAt { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int DeviceId { get; set; }
        public Device? Device { get; set; }
        public int ProfileId { get; set; }
        public Profile? Profile { get; set; }
    }
}
