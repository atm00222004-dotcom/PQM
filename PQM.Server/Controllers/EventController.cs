using Microsoft.AspNetCore.Mvc;
using PQM.Infrastructure;
using PQM.Infrastructure.Services;
using PQM.Core.DTOs;
using PQM.Server.Models;

namespace PQM.Server.Controllers
{
    [ApiController]
    [Route("api/eventslog")]
    public class EventController : ControllerBase
    {
        private readonly APIResponse _apiResponse = new();
        private readonly ILogger<EventController> _logger;
        private readonly DataContext _db;

        public EventController(ILogger<EventController> logger, DataContext db)
        {
            _logger = logger;
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        [HttpGet("Search")]
        public ActionResult Search([FromQuery] SearchParams searchParams)
        {
            try
            {
                // Rerouted from legacy db.Event to db.DeviceEvents.
                // DateStamp = ev.EventTime: this is the actual timestamp recorded by the meter
                // for the event (not the sync-execution time), so it is correct to expose as
                // the "when did this event occur" display value per the display contract.
                var query = from ev in _db.DeviceEvents
                            join d in _db.Device on ev.DeviceId equals d.Id
                            join p in _db.Parameter on ev.ParameterId equals p.Id
                            where ev.DeviceId == searchParams.DeviceId
                            select new EventSearchDto
                            {
                                Id = ev.Id,
                                DeviceId = ev.DeviceId,
                                DeviceName = d.Name,
                                ParameterName = p.Name,
                                Value = ValueFormatter.CleanValue(ev.RawValue),
                                DateStamp = ev.EventTime
                            };

                if (searchParams.StartDate != default)
                {
                    query = query.Where(q => q.DateStamp >= searchParams.StartDate);
                }

                if (searchParams.EndDate != default)
                {
                    var endDate = searchParams.EndDate.Date.AddDays(1).AddTicks(-1);
                    query = query.Where(q => q.DateStamp <= endDate);
                }

                var totalCount = query.Count();
                var items = query.OrderByDescending(q => q.DateStamp)
                                 .Skip((searchParams.PageNumber - 1) * searchParams.PageSize)
                                 .Take(searchParams.PageSize)
                                 .ToList();

                var result = new EventSearchResult
                {
                    EventLogSearch = items,
                    TotalCount = totalCount
                };

                _apiResponse.Status = true;
                _apiResponse.StatusCode = System.Net.HttpStatusCode.OK;
                _apiResponse.Data = result;
                return Ok(_apiResponse);
            }
            catch (Exception ex)
            {
                _apiResponse.Status = false;
                _apiResponse.StatusCode = System.Net.HttpStatusCode.BadRequest;
                _apiResponse.Data = null;
                _apiResponse.Errors = new List<string> { ex.Message };
                return Ok(_apiResponse);
            }
        }
    }

    public class EventSearchDto
    {
        public long Id { get; set; }
        public int DeviceId { get; set; }
        public required string DeviceName { get; set; }
        public required string ParameterName { get; set; }
        public required string Value { get; set; }
        public DateTime DateStamp { get; set; }
    }

    public class EventSearchResult
    {
        public int TotalCount { get; set; }
        public List<EventSearchDto> EventLogSearch { get; set; } = new();
    }
}
