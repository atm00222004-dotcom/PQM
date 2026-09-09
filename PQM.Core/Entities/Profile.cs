using System.Collections.Generic;

namespace PQM.Core.Entities
{
    public class Profile
    {
        public int ProfileId { get; set; }
        public string ObisCode { get; set; } = string.Empty;
        public string? FriendlyName { get; set; }
        public string Category { get; set; } = "TimeSeries";

        public int? MeterTypeId { get; set; }
        public MeterType? MeterType { get; set; }

        public virtual ICollection<Parameter> Parameters { get; set; } = new List<Parameter>();
    }
}
