using EReader_API.Domain.Entities.Identity;

namespace EReader_API.Domain.Interfaces
{
    public interface IUserRepository
    {
        Task<IEnumerable<User>> GetUsersAsync();
        Task<User> GetByIdAsync(int? id);
        Task<User> CreateAsync(User user);
        Task<User> UpdateAsync(User user);
        Task<User> RemoveAsync(User user);
    }
}
