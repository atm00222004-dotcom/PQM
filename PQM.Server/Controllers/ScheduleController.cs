using Microsoft.AspNetCore.Mvc;
using PQM.Core.Entities;
using PQM.Core.Helpers;
using PQM.Core.Interfaces.Repositories;
using PQM.Server.Models;
using PQM.Core.DTOs;

namespace PQM.Server.Controllers
{
    [ApiController]
    [Route("api/device")]
    public class ScheduleController : ControllerBase
    {
        private readonly APIResponse _apiResponse;
        private readonly IScheduleRepository _scheduleRepository;

        public ScheduleController(IScheduleRepository scheduleRepository)
        {
            _apiResponse = new APIResponse();
            _scheduleRepository = scheduleRepository?? throw new ArgumentNullException(nameof(scheduleRepository));
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

        [HttpPost("schedule")]
        public async Task<ActionResult> CreateSchedule([FromBody] UpdateScheduleRequest request,CancellationToken cancellationToken)
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
                    ? ScheduleHelper.ComputeNextRunAtUtc(
                        scheduledTime,
                        timeZoneId,
                        nowUtc)
                    : null;

                var schedule = new DeviceSyncSchedule
                {
                    IsEnabled = request.IsEnabled,
                    ScheduledTime = scheduledTime,
                    RepeatMode = request.RepeatMode ?? "Daily",
                    NextRunAtUtc = nextRunAtUtc
                };

                int scheduleId = await _scheduleRepository.AddAsync(
                    schedule,
                    cancellationToken);

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

        [HttpPut("schedule/{id:int}")]
        public async Task<ActionResult> UpdateSchedule(int id,[FromBody] UpdateScheduleRequest request,CancellationToken cancellationToken)
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
                    ? ScheduleHelper.ComputeNextRunAtUtc(
                        scheduledTime,
                        timeZoneId,
                        nowUtc)
                    : null;

                var schedule = new DeviceSyncSchedule
                {
                    Id = id,
                    IsEnabled = request.IsEnabled,
                    ScheduledTime = scheduledTime,
                    RepeatMode = request.RepeatMode ?? "Daily",
                    NextRunAtUtc = nextRunAtUtc
                };

                bool updated = await _scheduleRepository.UpdateAsync(
                    schedule,
                    cancellationToken);

                if (!updated)
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

        [HttpGet("schedule/{id:int}")]
        public async Task<ActionResult> GetSchedule(int id,CancellationToken cancellationToken)
        {
            try
            {
                var schedule = await _scheduleRepository.GetByIdAsync(
                    id,
                    cancellationToken);

                if (schedule == null)
                {
                    return NotFound(new
                    {
                        error = $"Schedule {id} not found."
                    });
                }

                var data = new
                {
                    id = schedule.Id,

                    isEnabled = schedule.IsEnabled,

                    scheduledTime =
                        schedule.ScheduledTime
                            .ToString(@"hh\:mm"),

                    repeatMode = schedule.RepeatMode,

                    nextRunAtUtc =
                        FormatUtcIso(schedule.NextRunAtUtc),

                    lastRunAtUtc =
                        FormatUtcIso(schedule.LastRunAtUtc),

                    lastRunStatus = schedule.LastRunStatus
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

        [HttpGet("schedules")]
        public async Task<ActionResult> GetAllSchedules(CancellationToken cancellationToken)
        {
            try
            {
                var schedules = await _scheduleRepository.GetAllAsync(
                    cancellationToken);

                var list = new List<object>();

                foreach (var schedule in schedules)
                {
                    list.Add(new
                    {
                        id = schedule.Id,

                        isEnabled = schedule.IsEnabled,

                        scheduledTime =
                            schedule.ScheduledTime
                                .ToString(@"hh\:mm"),

                        repeatMode = schedule.RepeatMode,

                        nextRunAtUtc =
                            FormatUtcIso(schedule.NextRunAtUtc),

                        lastRunAtUtc =
                            FormatUtcIso(schedule.LastRunAtUtc),

                        lastRunStatus = schedule.LastRunStatus
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
        
    }
}