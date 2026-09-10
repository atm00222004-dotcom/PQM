using Microsoft.EntityFrameworkCore;
using PQM.Core.Entities;
using PQM.Core.Interfaces.Repositories;

namespace PQM.Infrastructure.Repositories
{
    public class DeviceRepository : IDeviceRepository
    {
        private readonly DataContext _db;

        public DeviceRepository(DataContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<IEnumerable<Device>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _db.Device
                .Include(d => d.MeterType)
                .Where(d => !d.IsDeleted && d.IsActive)
                .OrderBy(d => d.Id)
                .ToListAsync(cancellationToken);
        }
        public async Task<Device?> GetByIdAsync(int id,CancellationToken cancellationToken = default)
        {
            return await _db.Device
                .Include(d => d.MeterType)
                .FirstOrDefaultAsync(
                    d => d.Id == id && !d.IsDeleted,
                    cancellationToken);
        }
        public async Task<int> AddAsync(Device device,CancellationToken cancellationToken = default)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));

            device.CreatedAt = DateTime.UtcNow;

           
            // Resolve MeterType by name if only the name was supplied.
            if (device.MeterTypeId == null &&
                device.MeterType != null &&
                !string.IsNullOrWhiteSpace(device.MeterType.Name))
            {
                var meterType = await _db.Set<MeterType>()
                    .FirstOrDefaultAsync(
                        m => m.Name == device.MeterType.Name,
                        cancellationToken);

                if (meterType != null)
                {
                    device.MeterTypeId = meterType.Id;
                }
            }

            // Do not attach an existing navigation object accidentally.
            device.MeterType = null;

            await _db.Device.AddAsync(device, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return device.Id;
        }
        public async Task<bool> UpdateAsync(Device device,CancellationToken cancellationToken = default)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));

            var existing = await _db.Device.FirstOrDefaultAsync(d => d.Id == device.Id && !d.IsDeleted,cancellationToken);

            if (existing == null)
                return false;

            existing.Name = device.Name;
            existing.IP = device.IP;
            existing.PORT = device.PORT;
            existing.SerialNumber = device.SerialNumber;
            existing.ConsumerNumber = device.ConsumerNumber;
            existing.IsActive = device.IsActive;
            existing.ClientAddress = device.ClientAddress;
            existing.ServerAddress = device.ServerAddress;
            existing.Authentication = device.Authentication;
            existing.Password = device.Password;
            existing.Timeout = device.Timeout;
            existing.TimeZoneId = device.TimeZoneId;
            existing.MeterTypeId = device.MeterTypeId;

            // IMPORTANT:
            // Device -> Schedule is many-to-one.
            // Therefore DeviceSyncScheduleId belongs here.
            existing.DeviceSyncScheduleId = device.DeviceSyncScheduleId;

            // If status is intentionally managed by the device/service,
            // don't overwrite it from the edit screen.

            await _db.SaveChangesAsync(cancellationToken);

            return true;
        }
        public async Task<bool> DeleteAsync(int id,CancellationToken cancellationToken = default)
        {
            var existing = await _db.Device.FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted,cancellationToken);

            if (existing == null)
                return false;

            // Soft delete
            existing.IsDeleted = true;
            existing.IsActive = false;

            await _db.SaveChangesAsync(cancellationToken);

            return true;
        }
        public async Task<IEnumerable<MeterType>> GetMeterTypesAsync(CancellationToken cancellationToken = default)
        {
            return await _db.Set<MeterType>()
                .OrderBy(m => m.Name)
                .ToListAsync(cancellationToken);
        }
    }
}