using PQM.Core.Entities;


namespace PQM.Core.Interfaces.Repositories
{
    public interface IDeviceRepository
    {
        Task<IEnumerable<Device>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<Device?> GetByIdAsync(int id,CancellationToken cancellationToken = default);
        Task<int> AddAsync(Device device,CancellationToken cancellationToken = default);
        Task<bool> UpdateAsync(Device device,CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id,CancellationToken cancellationToken = default);
        Task<IEnumerable<MeterType>> GetMeterTypesAsync(CancellationToken cancellationToken = default);
    }
}