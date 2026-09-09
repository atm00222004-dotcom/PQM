using System;
using System.ComponentModel.DataAnnotations;

namespace PQM.Core.Entities
{
    public class Device
    {
        [Key]
        public int Id { get; set; }
        public required string Name { get; set; }
        public required string IP { get; set; }
        public int PORT { get; set; }
        public string? SerialNumber { get; set; }
        public string? ConsumerNumber { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? LastSync { get; set; }

        public int? ClientAddress { get; set; } 
        public int? ServerAddress { get; set; } 
        public int? AuthenticationTypeId { get; set; } 

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string? Authentication
        {
            get => AuthenticationTypeId switch
            {
                0 => "None",
                1 => "Low",
                2 => "High",
                3 => "HighMd5",
                4 => "HighSha1",
                5 => "HighGmac",
                6 => "HighSha256",
                7 => "HighEcdsa",
                _ => "None"
            };
            set => AuthenticationTypeId = value?.ToLower() switch
            {
                "none" => 0,
                "low" => 1,
                "high" => 2,
                "highmd5" => 3,
                "highsha1" => 4,
                "highgmac" => 5,
                "highsha256" => 6,
                "highecdsa" => 7,
                _ => 0
            };
        }

        public string? Password { get; set; }
        public int? Timeout { get; set; } = 30000;
       

        public string? TimeZoneId { get; set; }
        public string Status { get; set; } = "Offline";

        public int? MeterTypeId { get; set; }
        public MeterType? MeterType { get; set; }

        public int? DeviceSyncScheduleId { get; set; }
        public DeviceSyncSchedule? DeviceSyncSchedule { get; set; }

    }
}
