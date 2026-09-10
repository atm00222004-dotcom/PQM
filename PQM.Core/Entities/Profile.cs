using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PQM.Core.Entities
{
    public class Profile
    {
        [Key] 
        public int Id { get; set; }
        public string ObisCode { get; set; } = string.Empty;
        public string? FriendlyName { get; set; }
        public string Category { get; set; } = "TimeSeries";
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public int? MeterTypeId { get; set; }
        public MeterType? MeterType { get; set; }

        public virtual ICollection<Parameter> Parameters { get; set; } = new List<Parameter>();
    }
}
