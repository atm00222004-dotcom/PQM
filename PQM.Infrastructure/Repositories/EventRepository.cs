using Microsoft.EntityFrameworkCore;
using PQM.Core.DTOs;
using PQM.Core.Interfaces.Repositories;
using PQM.Infrastructure.Services;

namespace PQM.Infrastructure.Repositories
{
    public class EventRepository : IEventRepository
    {
        private readonly DataContext _db;
        public EventRepository(DataContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }
        public async Task<(IEnumerable<EventSearchDto> Items, int TotalCount)> SearchEventsAsync(int deviceId,DateTime startDate,DateTime endDate,int pageNumber,int pageSize,CancellationToken cancellationToken = default)
        {
            var query = from ev in _db.DeviceEvents
                        join d in _db.Device on ev.DeviceId equals d.Id
                        join p in _db.Parameter on ev.ParameterId equals p.Id
                        where ev.DeviceId == deviceId
                        select new
                        {
                            ev.Id,
                            ev.DeviceId,
                            DeviceName = d.Name,
                            ParameterName = p.Name,
                            ev.RawValue,
                            DateStamp = ev.EventTime
                        };

            if (startDate != default)
            {
                query = query.Where(q => q.DateStamp >= startDate);
            }

            if (endDate != default)
            {
                var adjustedEndDate = endDate.Date.AddDays(1).AddTicks(-1);
                query = query.Where(q => q.DateStamp <= adjustedEndDate);
            }

            var totalCount = await query.CountAsync(cancellationToken);

            var itemsRaw = await query.OrderByDescending(q => q.DateStamp)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            var items = itemsRaw.Select(x => new EventSearchDto
            {
                Id = x.Id,
                DeviceId = x.DeviceId,
                DeviceName = x.DeviceName,
                ParameterName = x.ParameterName,
                Value = ValueFormatter.CleanValue(x.RawValue),
                DateStamp = x.DateStamp
            }).ToList();

            return (items, totalCount);
        }
    }
}
