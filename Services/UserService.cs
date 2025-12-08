using CSE325_visioncoders.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

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

        public async Task CreateUserAsync(User user)
        {
            await _users.InsertOneAsync(user);
        }
        
        public async Task<List<User>> GetCustomersAsync()
        {
            var filter = Builders<User>.Filter.Eq(u => u.Role, "customer");
            return await _users.Find(filter).ToListAsync();
        }
    }
}
