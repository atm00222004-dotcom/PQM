using PQM.Core.Entities;
using PQM.Core.DTOs;

namespace PQM.Core.Interfaces.Repositories
{
    public interface ILiveRepository
    {
        Task<IEnumerable<Profile>> GetProfilesAsync(CancellationToken cancellationToken = default);
        Task<IEnumerable<Parameter>> GetVisibleParametersAsync(int? profileId,int? meterTypeId,CancellationToken cancellationToken = default);
        Task<List<LiveScanParameterInfo>> GetParametersForLiveScanAsync(List<int>? profileIds,List<int>? parameterIds,int? meterTypeId,CancellationToken cancellationToken = default);
    }
}