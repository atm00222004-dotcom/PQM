namespace PQM.Core.DTOs
{
    public class LiveScanRequest
    {
        public List<int>? ProfileIds { get; set; }
        public List<int>? ParameterIds { get; set; }
    }

    public class LiveScanItemResult
    {
        public int ParameterId { get; set; }
        public string ParameterName { get; set; } = "";
        public string ObisCode { get; set; } = "";
        public string Value { get; set; } = "";
        public string? Unit { get; set; }
        public string? Error { get; set; }
    }

    public class LiveScanParameterInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ObisCode { get; set; } = string.Empty;
        public string? ObjectType { get; set; }
        public int? AttributeIndex { get; set; }
        public int? Scaler { get; set; }
        public string? Unit { get; set; }
    }
}