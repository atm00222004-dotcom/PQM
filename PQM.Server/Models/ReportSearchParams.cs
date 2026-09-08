using System;

namespace PQM.Server.Models
{
    public class ReportSearchParams : SearchParams
    {
        public string? ObjectType { get; set; }
        public int IntervalMinutes { get; set; } = 15;
    }
}
