using System;

namespace PQM.Core.DTOs
{
    public class ReportSearch : SearchParams
    {
        public string? ObjectType { get; set; }
        public int IntervalMinutes { get; set; } = 15;
    }

    public class SearchParams
    {
        public int DeviceId { get; set; }
        public int? ProfileId { get; set; }
        public int ParameterId { get; set; }
        public List<int>? ParameterIds { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string? EventType { get; set; }
    }
}
