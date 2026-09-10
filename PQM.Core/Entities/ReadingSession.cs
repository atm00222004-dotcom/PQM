using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PQM.Core.Entities
{
    public class ReadingSession
    {
        [Key]
        public long Id { get; set; }
        public DateTime? ReadTime { get; set; }
        public DateTime? EntryTimestampUtc { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public int DeviceId { get; set; }
        public Device? Device { get; set; }

        public int ProfileId { get; set; }
        public Profile? Profile { get; set; }
        public virtual ICollection<ReadingValue> Values { get; set; } = new List<ReadingValue>();
    }
}
