using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PQM.Server.Models;
using System.Data;
using System.Text;
using PQM.Core.DTOs;

namespace PQM.Server.Controllers
{
    [ApiController]
    [Route("api/report")]
    public class ReportController : ControllerBase
    {
        private readonly APIResponse _apiResponse = new();
        private readonly ILogger<ReportController> _logger;
        private readonly string _connectionString;

        public ReportController(ILogger<ReportController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string not found.");
        }

        [HttpGet("aggregate")]
        public IActionResult GetAggregatedReport([FromQuery] ReportSearch searchParams)
        {
            try
            {
                if (searchParams.DeviceId <= 0)
                {
                    _apiResponse.Status = false;
                    _apiResponse.StatusCode = System.Net.HttpStatusCode.BadRequest;
                    _apiResponse.Errors = new List<string> { "DeviceId is required." };
                    return Ok(_apiResponse);
                }

                int interval = searchParams.IntervalMinutes > 0 ? searchParams.IntervalMinutes : 15;
                int pageNumber = searchParams.PageNumber > 0 ? searchParams.PageNumber : 1;
                int pageSize = searchParams.PageSize > 0 ? searchParams.PageSize : 20;

                var (totalTimestamps, results) = ExecuteAggregation(searchParams, interval, pageNumber, pageSize);

                var result = new ParameterValueSearchResult
                {
                    DeviceLogSearch = results,
                    TotalCount = totalTimestamps
                };

                _apiResponse.Status = true;
                _apiResponse.StatusCode = System.Net.HttpStatusCode.OK;
                _apiResponse.Data = result;
                return Ok(_apiResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing aggregated report query");
                _apiResponse.Status = false;
                _apiResponse.StatusCode = System.Net.HttpStatusCode.InternalServerError;
                _apiResponse.Errors = new List<string> { ex.Message };
                return Ok(_apiResponse);
            }
        }

        [HttpGet("export")]
        public IActionResult ExportAggregatedReport([FromQuery] ReportSearch searchParams)
        {
            try
            {
                if (searchParams.DeviceId <= 0)
                {
                    return BadRequest(new { status = false, message = "DeviceId is required." });
                }

                int interval = searchParams.IntervalMinutes > 0 ? searchParams.IntervalMinutes : 15;
                var (_, readings) = ExecuteAggregation(searchParams, interval, pageNumber: 1, pageSize: int.MaxValue);

                var timestamps = readings
                    .Where(r => r.DateStamp.HasValue)
                    .Select(r => r.DateStamp!.Value)
                    .Distinct()
                    .OrderBy(t => t)
                    .ToList();

                var paramsMap = new Dictionary<int, string>();
                foreach (var r in readings)
                {
                    if (!paramsMap.ContainsKey(r.ParameterId))
                        paramsMap[r.ParameterId] = r.ParameterName;
                }

                var cellLookup = new Dictionary<int, Dictionary<DateTime, string>>();
                foreach (var r in readings)
                {
                    if (!r.DateStamp.HasValue) continue;
                    if (!cellLookup.TryGetValue(r.ParameterId, out var dict))
                    {
                        dict = new Dictionary<DateTime, string>();
                        cellLookup[r.ParameterId] = dict;
                    }
                    dict[r.DateStamp.Value] = r.Value;
                }

                var sb = new StringBuilder();

                // Report Header Metadata
                sb.AppendLine($"\"Report Type\",\"Interval Aggregated Report ({interval} min buckets)\"");
                sb.AppendLine($"\"Generated At\",\"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\"");
                sb.AppendLine();

                // Table Header
                sb.Append("\"Parameter\"");
                foreach (var ts in timestamps)
                {
                    sb.Append($",\"{ts:yyyy-MM-dd HH:mm:ss}\"");
                }
                sb.AppendLine();

                // Data Rows
                foreach (var kvp in paramsMap)
                {
                    int pId = kvp.Key;
                    string pName = kvp.Value;

                    sb.Append($"\"{pName.Replace("\"", "\"\"")}\"");
                    foreach (var ts in timestamps)
                    {
                        string val = cellLookup.TryGetValue(pId, out var dict) && dict.TryGetValue(ts, out var v) ? v : "";
                        sb.Append($",\"{val.Replace("\"", "\"\"")}\"");
                    }
                    sb.AppendLine();
                }

                string fileName = $"AggregatedReport_{interval}min_{DateTime.Now:yyyyMMdd_HHmmss}.xls";
                byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();

                return File(bytes, "application/vnd.ms-excel", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting aggregated report");
                return BadRequest(new { status = false, message = ex.Message });
            }
        }

        private (int TotalTimestamps, List<ParameterValueSearch> Results) ExecuteAggregation(ReportSearch searchParams, int intervalMinutes, int pageNumber, int pageSize)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            // Lookup device timezone for UTC conversion
            TimeZoneInfo deviceTz = TimeZoneInfo.Utc;
            if (searchParams.DeviceId > 0)
            {
                using var tzCmd = new SqlCommand("SELECT TimeZoneId FROM Devices WHERE Id = @Id AND IsDeleted = 0", conn);
                tzCmd.Parameters.AddWithValue("@Id", searchParams.DeviceId);
                var tzObj = tzCmd.ExecuteScalar();
                if (tzObj != null && tzObj != DBNull.Value && !string.IsNullOrWhiteSpace(tzObj.ToString()))
                {
                    try { deviceTz = TimeZoneInfo.FindSystemTimeZoneById(tzObj.ToString()!); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to resolve TimeZoneId {TimeZoneId} for device {DeviceId}, falling back to UTC.", tzObj, searchParams.DeviceId);
                    }
                }
            }

            DateTime startDate = searchParams.StartDate != default 
                ? searchParams.StartDate 
                : new DateTime(2000, 1, 1);

            DateTime endDate = searchParams.EndDate != default 
                ? (searchParams.EndDate.TimeOfDay == TimeSpan.Zero ? searchParams.EndDate.Date.AddDays(1).AddTicks(-1) : searchParams.EndDate)
                : DateTime.UtcNow.AddDays(1);

            var unspecifiedStart = DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified);
            DateTime startUtc = startDate.Kind == DateTimeKind.Utc
                ? startDate
                : TimeZoneInfo.ConvertTimeToUtc(unspecifiedStart, deviceTz);

            var unspecifiedEnd = DateTime.SpecifyKind(endDate, DateTimeKind.Unspecified);
            DateTime endUtc = endDate.Kind == DateTimeKind.Utc
                ? endDate
                : TimeZoneInfo.ConvertTimeToUtc(unspecifiedEnd, deviceTz);

            string paramIdsCsv = "";
            if (searchParams.ParameterIds != null && searchParams.ParameterIds.Count > 0)
            {
                paramIdsCsv = string.Join(",", searchParams.ParameterIds.Where(id => id > 0));
            }

            string sql = @"
                WITH ScaledReadings AS (
                    SELECT 
                        p.Id AS ParameterId,
                        p.Name AS ParameterName,
                        p.ObjectType,
                        p.AggregationType,
                        CASE 
                            WHEN p.Scaler IS NOT NULL AND p.Scaler <> 0 
                                THEN rv.ValueNumeric * POWER(10E0, CAST(p.Scaler AS FLOAT))
                            ELSE rv.ValueNumeric
                        END AS ScaledValueNumeric,
                        DATEADD(minute, 
                            (DATEDIFF(minute, '2000-01-01', rs.EntryTimestampUtc) / @IntervalMinutes) * @IntervalMinutes, 
                            '2000-01-01') AS BucketTimestamp
                    FROM ReadingValues rv
                    INNER JOIN Parameters p ON rv.ParameterId = p.Id
                    INNER JOIN ReadingSessions rs ON rv.SessionId = rs.Id
                    WHERE rs.DeviceId = @DeviceId
                      AND (@ProfileId IS NULL OR p.ProfileId = @ProfileId)
                      AND (@ObjectType IS NULL OR p.ObjectType = @ObjectType)
                      AND (@ParamIdsCsv = '' OR p.Id IN (SELECT CAST(value AS INT) FROM STRING_SPLIT(@ParamIdsCsv, ',')))
                      AND rs.EntryTimestampUtc >= @StartUtc
                      AND rs.EntryTimestampUtc <= @EndUtc
                      AND rv.ValueNumeric IS NOT NULL
                ),
                AggregatedBuckets AS (
                    SELECT 
                        ParameterId,
                        ParameterName,
                        BucketTimestamp,
                        CASE 
                            WHEN AggregationType = 'Max' OR ParameterName LIKE 'Cum%' OR ParameterName LIKE 'Cumulative%' 
                                THEN CAST(ROUND(MAX(ScaledValueNumeric), 2) AS VARCHAR(50))
                            ELSE CAST(ROUND(AVG(ScaledValueNumeric), 2) AS VARCHAR(50))
                        END AS AggregatedValue
                    FROM ScaledReadings
                    GROUP BY ParameterId, ParameterName, AggregationType, BucketTimestamp
                )
                SELECT 
                    ParameterId,
                    ParameterName,
                    BucketTimestamp AS DateStamp,
                    AggregatedValue AS Value
                FROM AggregatedBuckets
                ORDER BY ParameterId, BucketTimestamp;";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@DeviceId", searchParams.DeviceId);
            cmd.Parameters.AddWithValue("@ProfileId", (object?)searchParams.ProfileId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ObjectType", (object?)searchParams.ObjectType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ParamIdsCsv", paramIdsCsv);
            cmd.Parameters.AddWithValue("@StartUtc", startUtc);
            cmd.Parameters.AddWithValue("@EndUtc", endUtc);
            cmd.Parameters.AddWithValue("@IntervalMinutes", intervalMinutes);

            var allReadings = new List<ParameterValueSearch>();
            using var reader = cmd.ExecuteReader();
            long rowId = 1;
            while (reader.Read())
            {
                allReadings.Add(new ParameterValueSearch
                {
                    Id = rowId++,
                    ParameterId = reader.GetInt32(0),
                    ParameterName = reader.GetString(1),
                    DateStamp = reader.GetDateTime(2),
                    Value = reader.GetString(3),
                    DeviceName = ""
                });
            }

            var timestamps = allReadings
                .Where(r => r.DateStamp.HasValue)
                .Select(r => r.DateStamp!.Value)
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            int totalTimestamps = timestamps.Count;
            if (totalTimestamps == 0)
            {
                return (0, new List<ParameterValueSearch>());
            }

            int validPageNumber = pageNumber;
            if ((validPageNumber - 1) * pageSize >= totalTimestamps)
            {
                validPageNumber = 1;
            }

            var pagedTimestamps = pageSize == int.MaxValue
                ? timestamps
                : timestamps.Skip((validPageNumber - 1) * pageSize).Take(pageSize).ToList();

            var pagedTimestampsSet = new HashSet<DateTime>(pagedTimestamps);
            var pagedReadings = allReadings
                .Where(r => r.DateStamp.HasValue && pagedTimestampsSet.Contains(r.DateStamp.Value))
                .ToList();

            return (totalTimestamps, pagedReadings);
        }

        public class ParameterValueSearch
        {
            public long Id { get; set; }
            public required string Value { get; set; }
            public DateTime? DateStamp { get; set; }
            public required string DeviceName { get; set; }
            public required string ParameterName { get; set; }
            public int ParameterId { get; set; }
        }

        public class ParameterValueSearchResult
        {
            public int TotalCount { get; set; }
            public List<ParameterValueSearch> DeviceLogSearch { get; set; } = new();
        }
    }
}
