using System.Collections.Generic;
using System.Linq;

namespace PQM.Infrastructure.Services
{
    /// <summary>
    /// Catalogue of all known meter profile OBIS codes, split into two groups:
    ///
    /// TimeSeriesProfiles   — produce time-stamped rows whose watermark (EntryTimestampUtc)
    ///                        can advance with each sync. These are the profiles that drive
    ///                        incremental sync in Stage 4.
    ///
    /// StaticOrMetadataProfiles — either read once (nameplate, manufacturer info) or hold
    ///                            scaler/unit metadata rather than time-series measurement
    ///                            history. These are synced less frequently or on demand.
    ///
    /// CLASSIFICATION NOTE: The five scaler profiles (1.0.94.91.3-7.255) belong in
    /// StaticOrMetadataProfiles. They were previously misclassified as time-series in
    /// an earlier revision and have been corrected here. Do not move them back.
    /// </summary>
    public static class ProfileCatalog
    {
        public static readonly Dictionary<string, string> TimeSeriesProfiles = new()
        {
            { "1.0.99.1.0.255", "Block Load" },
            { "1.0.99.2.0.255", "Daily Load" },
            { "1.0.98.1.0.255", "Billing" },
            { "0.0.99.98.0.255", "Voltage Events" },
            { "0.0.99.98.1.255", "Current Events" },
            { "0.0.99.98.2.255", "Power Events" },
            { "0.0.99.98.3.255", "Transaction Events" },
            { "0.0.99.98.4.255", "Other Tamper Events" }
        };

        public static readonly Dictionary<string, string> StaticOrMetadataProfiles = new()
        {
            { "0.0.94.91.10.255", "Nameplate" },
            { "1.0.94.91.0.255",  "Instantaneous" },
            { "1.0.94.91.3.255",  "Scaler: Instantaneous" }, // Metadata — NOT time-series
            { "1.0.94.91.4.255",  "Scaler: Block Load" },     // Metadata — NOT time-series
            { "1.0.94.91.5.255",  "Scaler: Daily Load" },     // Metadata — NOT time-series
            { "1.0.94.91.6.255",  "Scaler: Billing" },        // Metadata — NOT time-series
            { "1.0.94.91.7.255",  "Scaler: Events" },         // Metadata — NOT time-series
            { "0.128.187.0.128.255", "Manufacturer specific" },
            { "1.0.128.7.90.255",    "Man. specific" }
        };

        /// <summary>All profiles merged — used to pre-populate GXDLMSClient.Objects
        /// with any profiles not returned by the meter's association view.</summary>
        public static Dictionary<string, string> AllProfiles =>TimeSeriesProfiles.Concat(StaticOrMetadataProfiles).ToDictionary(x => x.Key, x => x.Value);
    }
}
