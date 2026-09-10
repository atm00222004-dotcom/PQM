using Microsoft.EntityFrameworkCore;
using PQM.Core.DTOs;
using PQM.Core.Entities;
using PQM.Core.Interfaces.Repositories;

namespace PQM.Infrastructure.Repositories
{
    public class LiveRepository : ILiveRepository
    {
        private readonly DataContext _db;
        public LiveRepository(DataContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }
        public async Task<IEnumerable<Profile>> GetProfilesAsync(CancellationToken cancellationToken = default)
        {
            return await _db.Profiles.ToListAsync(cancellationToken);
        }
        public async Task<IEnumerable<Parameter>> GetVisibleParametersAsync(int? profileId,int? meterTypeId,CancellationToken cancellationToken = default)
        {
            var query = _db.Parameter.Where(p => p.IsVisible);

            if (meterTypeId.HasValue && meterTypeId.Value > 0)
            {
                if (meterTypeId.Value == 1) // ABT
                {
                    query = query.Where(
                        p => p.MeterTypeId == 1 ||
                             p.MeterTypeId == 3 ||
                             p.MeterTypeId == null);
                }
                else if (meterTypeId.Value == 2) // PQ
                {
                    query = query.Where(
                        p => p.MeterTypeId == 2 ||
                             p.MeterTypeId == 3 ||
                             p.MeterTypeId == null);
                }
                // meterTypeId == 3 (Both) sees all parameters
            }

            if (profileId.HasValue && profileId.Value > 0)
            {
                query = query.Where(p => p.ProfileId == profileId.Value);
            }

            return await query.ToListAsync(cancellationToken);
        }
        public async Task<List<LiveScanParameterInfo>> GetParametersForLiveScanAsync(List<int>? profileIds,List<int>? parameterIds,int? meterTypeId,CancellationToken cancellationToken = default)
        {
            var query = _db.Parameter.Where(p => p.ObisCode != null);

            if (parameterIds != null && parameterIds.Count > 0)
            {
                query = query.Where(p => parameterIds.Contains(p.Id));
            }
            else if (profileIds != null && profileIds.Count > 0)
            {
                query = query.Where(p => profileIds.Contains(p.ProfileId));
            }
            else
            {
                query = query
                    .Where(
                        p => p.MeterTypeId == meterTypeId ||
                             p.MeterTypeId == null)
                    .OrderBy(p => p.Id);
            }

            return await query
                .Select(p => new LiveScanParameterInfo
                {
                    Id = p.Id,
                    Name = p.Name,
                    ObisCode = p.ObisCode!,
                    ObjectType = p.ObjectType,
                    AttributeIndex = p.AttributeIndex,
                    Scaler = p.Scaler,
                    Unit = p.Unit
                })
                .ToListAsync(cancellationToken);
        }
    }
}