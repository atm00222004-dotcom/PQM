using Microsoft.EntityFrameworkCore;
using PQM.Core.Entities;
using PQM.Core.Interfaces.Repositories;

namespace PQM.Infrastructure.Repositories
{
    public class ScheduleRepository : IScheduleRepository
    {
        private readonly DataContext _db;
        public ScheduleRepository(DataContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }
        public async Task<int> AddAsync(DeviceSyncSchedule schedule,CancellationToken cancellationToken = default)
        {
            if (schedule == null)
                throw new ArgumentNullException(nameof(schedule));

            if (string.IsNullOrWhiteSpace(schedule.RepeatMode))
            {
                schedule.RepeatMode = "Daily";
            }

            await _db.DeviceSyncSchedules.AddAsync(schedule, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return schedule.Id;
        }
        public async Task<bool> UpdateAsync(DeviceSyncSchedule schedule,CancellationToken cancellationToken = default)
        {
            if (schedule == null)
                throw new ArgumentNullException(nameof(schedule));

            var existing = await _db.DeviceSyncSchedules
                .FirstOrDefaultAsync(
                    s => s.Id == schedule.Id,
                    cancellationToken);

            if (existing == null)
                return false;

            existing.IsEnabled = schedule.IsEnabled;
            existing.ScheduledTime = schedule.ScheduledTime;
            existing.RepeatMode = schedule.RepeatMode ?? "Daily";
            existing.NextRunAtUtc = schedule.NextRunAtUtc;

            // LastRunAtUtc / LastRunStatus are intentionally left alone here —
            // they should be updated by the sync job itself, not the edit screen.

            await _db.SaveChangesAsync(cancellationToken);

            return true;
        }
        public async Task<DeviceSyncSchedule?> GetByIdAsync(int id,CancellationToken cancellationToken = default)
        {
            return await _db.DeviceSyncSchedules
                .FirstOrDefaultAsync(
                    s => s.Id == id,
                    cancellationToken);
        }
        public async Task<IEnumerable<DeviceSyncSchedule>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _db.DeviceSyncSchedules
                .OrderBy(s => s.ScheduledTime)
                .ToListAsync(cancellationToken);
        }
    }
}