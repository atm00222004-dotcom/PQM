using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using PQM.Server.Models;

namespace PQM.Server.Controllers
{
    [ApiController]
    [Route("api/device")]
    public class ScheduleController : ControllerBase
    {
        private readonly APIResponse _apiResponse;
        private readonly string _connectionString;

    public ScheduleController(IConfiguration configuration)
        {
            _apiResponse = new APIResponse();

            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "Connection string DefaultConnection not found.");
        }

        private static string? FormatUtcIso(DateTime? dt)
        {
            if (!dt.HasValue)
                return null;

            var utc = DateTime.SpecifyKind(
                dt.Value,
                DateTimeKind.Utc);

            return utc.ToString("o");
        }

        // Create a new device sync schedule
        [HttpPost("schedule")]
        public async Task<ActionResult> CreateSchedule(
            [FromBody] UpdateScheduleRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new
                    {
                        error = "Request body is required."
                    });
                }

                if (!TimeSpan.TryParse(
                    request.ScheduledTime,
                    out var scheduledTime))
                {
                    return BadRequest(new
                    {
                        error = "Invalid ScheduledTime format. Expected HH:mm or HH:mm:ss."
                    });
                }

                DateTime nowUtc = DateTime.UtcNow;

                string timeZoneId = "India Standard Time";

                DateTime? nextRunAtUtc = request.IsEnabled
                    ? PQM.Core.Helpers.ScheduleHelper.ComputeNextRunAtUtc(
                        scheduledTime,
                        timeZoneId,
                        nowUtc)
                    : null;

                using var conn =
                    new SqlConnection(_connectionString);

                await conn.OpenAsync(cancellationToken);

                using var cmd = conn.CreateCommand();

                cmd.CommandText = @"
                INSERT INTO DeviceSyncSchedule
                (
                    IsEnabled,
                    ScheduledTime,
                    RepeatMode,
                    NextRunAtUtc
                )
                VALUES
                (
                    @isEnabled,
                    @scheduledTime,
                    @repeatMode,
                    @nextRunAtUtc
                );

                SELECT CAST(SCOPE_IDENTITY() AS INT);";

                cmd.Parameters.AddWithValue(
                    "@isEnabled",
                    request.IsEnabled);

                cmd.Parameters.AddWithValue(
                    "@scheduledTime",
                    scheduledTime);

                cmd.Parameters.AddWithValue(
                    "@repeatMode",
                    request.RepeatMode ?? "Daily");

                cmd.Parameters.AddWithValue(
                    "@nextRunAtUtc",
                    (object?)nextRunAtUtc ?? DBNull.Value);

                var result =
                    await cmd.ExecuteScalarAsync(cancellationToken);

                int scheduleId = Convert.ToInt32(result);

                _apiResponse.Status = true;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.OK;

                _apiResponse.Data = new
                {
                    id = scheduleId,
                    isEnabled = request.IsEnabled,
                    scheduledTime =
                        scheduledTime.ToString(@"hh\:mm"),
                    repeatMode =
                        request.RepeatMode ?? "Daily",
                    nextRunAtUtc =
                        FormatUtcIso(nextRunAtUtc)
                };

                _apiResponse.Errors.Clear();

                return Ok(_apiResponse);
            }
            catch (Exception ex)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.BadRequest;

                _apiResponse.Data = null;

                _apiResponse.Errors =
                    new List<string>
                    {
                    ex.Message
                    };

                return Ok(_apiResponse);
            }
        }

        // Update an existing device sync schedule
        [HttpPut("schedule/{id:int}")]
        public async Task<ActionResult> UpdateSchedule(
            int id,
            [FromBody] UpdateScheduleRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new
                    {
                        error = "Request body is required."
                    });
                }

                if (!TimeSpan.TryParse(
                    request.ScheduledTime,
                    out var scheduledTime))
                {
                    return BadRequest(new
                    {
                        error = "Invalid ScheduledTime format. Expected HH:mm or HH:mm:ss."
                    });
                }

                DateTime nowUtc = DateTime.UtcNow;

                string timeZoneId = "India Standard Time";

                DateTime? nextRunAtUtc = request.IsEnabled
                    ? PQM.Core.Helpers.ScheduleHelper.ComputeNextRunAtUtc(
                        scheduledTime,
                        timeZoneId,
                        nowUtc)
                    : null;

                using var conn =
                    new SqlConnection(_connectionString);

                await conn.OpenAsync(cancellationToken);

                using var cmd = conn.CreateCommand();

                cmd.CommandText = @"
                UPDATE DeviceSyncSchedule
                SET
                    IsEnabled = @isEnabled,
                    ScheduledTime = @scheduledTime,
                    RepeatMode = @repeatMode,
                    NextRunAtUtc = @nextRunAtUtc
                WHERE Id = @id;";

                cmd.Parameters.AddWithValue(
                    "@id",
                    id);

                cmd.Parameters.AddWithValue(
                    "@isEnabled",
                    request.IsEnabled);

                cmd.Parameters.AddWithValue(
                    "@scheduledTime",
                    scheduledTime);

                cmd.Parameters.AddWithValue(
                    "@repeatMode",
                    request.RepeatMode ?? "Daily");

                cmd.Parameters.AddWithValue(
                    "@nextRunAtUtc",
                    (object?)nextRunAtUtc ?? DBNull.Value);

                int rowsAffected =
                    await cmd.ExecuteNonQueryAsync(
                        cancellationToken);

                if (rowsAffected == 0)
                {
                    return NotFound(new
                    {
                        error = $"Schedule {id} not found."
                    });
                }

                _apiResponse.Status = true;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.OK;

                _apiResponse.Data = new
                {
                    id,
                    isEnabled = request.IsEnabled,
                    scheduledTime =
                        scheduledTime.ToString(@"hh\:mm"),
                    repeatMode =
                        request.RepeatMode ?? "Daily",
                    nextRunAtUtc =
                        FormatUtcIso(nextRunAtUtc)
                };

                _apiResponse.Errors.Clear();

                return Ok(_apiResponse);
            }
            catch (Exception ex)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.BadRequest;

                _apiResponse.Data = null;

                _apiResponse.Errors =
                    new List<string>
                    {
                    ex.Message
                    };

                return Ok(_apiResponse);
            }
        }

        // Get one schedule by ID
        [HttpGet("schedule/{id:int}")]
        public async Task<ActionResult> GetSchedule(
            int id,
            CancellationToken cancellationToken)
        {
            try
            {
                using var conn =
                    new SqlConnection(_connectionString);

                await conn.OpenAsync(cancellationToken);

                using var cmd = conn.CreateCommand();

                cmd.CommandText = @"
                SELECT
                    Id,
                    IsEnabled,
                    ScheduledTime,
                    RepeatMode,
                    NextRunAtUtc,
                    LastRunAtUtc,
                    LastRunStatus
                FROM DeviceSyncSchedule
                WHERE Id = @id;";

                cmd.Parameters.AddWithValue(
                    "@id",
                    id);

                using var reader =
                    await cmd.ExecuteReaderAsync(
                        cancellationToken);

                if (!await reader.ReadAsync(
                    cancellationToken))
                {
                    return NotFound(new
                    {
                        error = $"Schedule {id} not found."
                    });
                }

                var data = new
                {
                    id = reader.GetInt32(0),

                    isEnabled =
                        reader.GetBoolean(1),

                    scheduledTime =
                        reader.GetTimeSpan(2)
                            .ToString(@"hh\:mm"),

                    repeatMode =
                        reader.GetString(3),

                    nextRunAtUtc =
                        reader.IsDBNull(4)
                            ? null
                            : FormatUtcIso(
                                reader.GetDateTime(4)),

                    lastRunAtUtc =
                        reader.IsDBNull(5)
                            ? null
                            : FormatUtcIso(
                                reader.GetDateTime(5)),

                    lastRunStatus =
                        reader.IsDBNull(6)
                            ? null
                            : reader.GetString(6)
                };

                _apiResponse.Status = true;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.OK;

                _apiResponse.Data = data;

                _apiResponse.Errors.Clear();

                return Ok(_apiResponse);
            }
            catch (Exception ex)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.BadRequest;

                _apiResponse.Data = null;

                _apiResponse.Errors =
                    new List<string>
                    {
                    ex.Message
                    };

                return Ok(_apiResponse);
            }
        }

        // Get all schedules
        [HttpGet("schedules")]
        public async Task<ActionResult> GetAllSchedules(
            CancellationToken cancellationToken)
        {
            try
            {
                var list = new List<object>();

                using var conn =
                    new SqlConnection(_connectionString);

                await conn.OpenAsync(cancellationToken);

                using var cmd = conn.CreateCommand();

                cmd.CommandText = @"
                SELECT
                    Id,
                    IsEnabled,
                    ScheduledTime,
                    RepeatMode,
                    NextRunAtUtc,
                    LastRunAtUtc,
                    LastRunStatus
                FROM DeviceSyncSchedule
                ORDER BY ScheduledTime ASC;";

                using var reader =
                    await cmd.ExecuteReaderAsync(
                        cancellationToken);

                while (await reader.ReadAsync(
                    cancellationToken))
                {
                    list.Add(new
                    {
                        id = reader.GetInt32(0),

                        isEnabled =
                            reader.GetBoolean(1),

                        scheduledTime =
                            reader.GetTimeSpan(2)
                                .ToString(@"hh\:mm"),

                        repeatMode =
                            reader.GetString(3),

                        nextRunAtUtc =
                            reader.IsDBNull(4)
                                ? null
                                : FormatUtcIso(
                                    reader.GetDateTime(4)),

                        lastRunAtUtc =
                            reader.IsDBNull(5)
                                ? null
                                : FormatUtcIso(
                                    reader.GetDateTime(5)),

                        lastRunStatus =
                            reader.IsDBNull(6)
                                ? null
                                : reader.GetString(6)
                    });
                }

                _apiResponse.Status = true;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.OK;

                _apiResponse.Data = list;

                _apiResponse.Errors.Clear();

                return Ok(_apiResponse);
            }
            catch (Exception ex)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode =
                    System.Net.HttpStatusCode.BadRequest;

                _apiResponse.Data = null;

                _apiResponse.Errors =
                    new List<string>
                    {
                    ex.Message
                    };

                return Ok(_apiResponse);
            }
        }

        public class UpdateScheduleRequest
        {
            public bool IsEnabled { get; set; }

            public string ScheduledTime { get; set; }
                = "00:00";

            public string RepeatMode { get; set; }
                = "Daily";
        }
    }
}
