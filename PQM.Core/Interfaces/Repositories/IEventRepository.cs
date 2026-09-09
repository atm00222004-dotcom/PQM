using PQM.Core.DTOs;

namespace PQM.Core.Interfaces.Repositories
{
    public interface IEventRepository
    {
        Task<(IEnumerable<EventSearchDto> Items, int TotalCount)> SearchEventsAsync(int deviceId,DateTime startDate,DateTime endDate,int pageNumber,int pageSize,CancellationToken cancellationToken = default);
    }
}
