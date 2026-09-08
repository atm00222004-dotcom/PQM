namespace PQM.Console
{
    public class ConsoleOptions
    {
        public const string SectionName = "DlmsSettings";

        public string DefaultConnection { get; set; } = string.Empty;
        public int MeterCooldownSeconds { get; set; } = 8;
    }
}
