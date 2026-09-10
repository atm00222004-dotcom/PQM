using System.ComponentModel.DataAnnotations;

namespace PQM.Core.Entities
{
    public class ReadingValue
    {
        [Key]
        public long Id { get; set; }
        public required string Value { get; set; }
        public string? RawValue { get; set; }
        public double? ValueNumeric { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public long? SessionId { get; set; }
        public ReadingSession? Session { get; set; }

        public int? ParameterId { get; set; }
        public Parameter? Parameter { get; set; }
    }
}
