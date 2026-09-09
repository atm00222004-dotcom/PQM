using PQM.Core.Entities;

namespace PQM.Core.Interfaces.Repositories
{
    public interface IAuthRepository
    {
        Task<bool> EmailExistsAsync(string email,CancellationToken cancellationToken = default);
        Task<int> AddAsync(User user,CancellationToken cancellationToken = default);
        Task<User?> GetByEmailAndPasswordAsync(string email,string password,CancellationToken cancellationToken = default);
        Task<User?> GetByIdAsync(int id,CancellationToken cancellationToken = default);
    }
}