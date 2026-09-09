using Microsoft.EntityFrameworkCore;
using PQM.Core.Entities;
using PQM.Core.Interfaces.Repositories;

namespace PQM.Infrastructure.Repositories
{
    public class AuthRepository : IAuthRepository
    {
        private readonly DataContext _db;
        public AuthRepository(DataContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }
        public async Task<bool> EmailExistsAsync(string email,CancellationToken cancellationToken = default)
        {
            return await _db.User.AnyAsync(
                u => u.Email == email,
                cancellationToken);
        }
        public async Task<int> AddAsync(User user,CancellationToken cancellationToken = default)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            user.CreatedDate = DateTime.UtcNow;

            await _db.User.AddAsync(user, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return user.Id;
        }
        public async Task<User?> GetByEmailAndPasswordAsync(string email,string password,CancellationToken cancellationToken = default)
        {
            return await _db.User.FirstOrDefaultAsync(
                u => u.Email == email &&
                     u.Password == password,
                cancellationToken);
        }
        public async Task<User?> GetByIdAsync(int id,CancellationToken cancellationToken = default)
        {
            return await _db.User.FirstOrDefaultAsync(
                u => u.Id == id,
                cancellationToken);
        }
    }
}