using CSE325_visioncoders.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

// Service for managing users in the MongoDB database
namespace CSE325_visioncoders.Services
{
    public class UserService
    {
        private readonly IMongoCollection<User> _users;

        public UserService(IOptions<MongoDbSettings> settings)
        {
            var client = new MongoClient(settings.Value.ConnectionString);
            var database = client.GetDatabase(settings.Value.DatabaseName);
            _users = database.GetCollection<User>("users");
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _users.Find(u => u.Email == email).FirstOrDefaultAsync();
        }

        public async Task<User?> GetByIdAsync(string id)
        {
            return await _users.Find(u => u.Id == id).FirstOrDefaultAsync();
        }

        public async Task CreateUserAsync(User user)
        {
            await _users.InsertOneAsync(user);
        }

        public async Task<List<User>> GetCustomersAsync()
        {
            var filter = Builders<User>.Filter.Eq(u => u.Role, "customer");
            return await _users.Find(filter).ToListAsync();
        }

        // FINAL METHOD USED BY PROFILE PAGE
        public async Task UpdateProfileAsync(string id, string name, string? phone, string? address)
        {
            var update = Builders<User>.Update
                .Set(u => u.Name, name)
                .Set(u => u.Phone, phone)
                .Set(u => u.Address, address);

            await _users.UpdateOneAsync(u => u.Id == id, update);
        }

        public async Task UpdateProfileImageAsync(string id, string?        profileImageUrl)
        {
            var update = Builders<User>.Update
                .Set(u => u.ProfileImageUrl, profileImageUrl);

            await _users.UpdateOneAsync(u => u.Id == id, update);
        }

        public async Task UpdatePasswordHashAsync(string id, string         newPasswordHash)
        {
            var update = Builders<User>.Update
                .Set(u => u.PasswordHash, newPasswordHash);

            await _users.UpdateOneAsync(u => u.Id == id, update);
        }

        public async Task<bool> ChangePasswordAsync(string id, string       currentPassword, string newPassword)
        {
            var user = await GetByIdAsync(id);
            if (user is null)
                return false;

            if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
                return false;

            var newHash = PasswordHasher.Hash(newPassword);
            await UpdatePasswordHashAsync(id, newHash);
            return true;
        }

        // Backwards-compat overloads, if you still need them elsewhere
        public Task<bool> UpdatePasswordAsync(string id, string currentPassword,        string newPassword)
            => ChangePasswordAsync(id, currentPassword, newPassword);

        public async Task<bool> UpdatePasswordAsync(string id, string newPassword)
        {
            var newHash = PasswordHasher.Hash(newPassword);
            await UpdatePasswordHashAsync(id, newHash);
            return true;
        }
    }
}
