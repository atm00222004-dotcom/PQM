using PQM.Core.Entities;

namespace PQM.Core.Interfaces.Repositories
{
    public interface IScheduleRepository
    {
        Task<int> AddAsync(DeviceSyncSchedule schedule,CancellationToken cancellationToken = default);
        Task<bool> UpdateAsync(DeviceSyncSchedule schedule,CancellationToken cancellationToken = default);
        Task<DeviceSyncSchedule?> GetByIdAsync(int id,CancellationToken cancellationToken = default);
        Task<IEnumerable<DeviceSyncSchedule>> GetAllAsync(CancellationToken cancellationToken = default);
    }
}