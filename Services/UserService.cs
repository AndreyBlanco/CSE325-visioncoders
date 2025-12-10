/*
  File: UserService.cs
  Description: MongoDB-backed service for managing User entities, including:
               retrieval by email/ID, creation, profile updates, and password changes.
*/

using CSE325_visioncoders.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CSE325_visioncoders.Services
{
    /// <summary>
    /// Class: UserService
    /// Purpose: Encapsulates CRUD-style operations and profile/password updates
    ///          against the "users" collection in MongoDB.
    /// </summary>
    public class UserService
    {
        private readonly IMongoCollection<User> _users;

        /// <summary>
        /// Constructor: UserService
        /// Purpose: Initializes the MongoDB client, database, and users collection using the provided settings.
        /// </summary>
        public UserService(IOptions<MongoDbSettings> settings)
        {
            var client = new MongoClient(settings.Value.ConnectionString);
            var database = client.GetDatabase(settings.Value.DatabaseName);
            _users = database.GetCollection<User>("users");
        }

        // Retrieval Methods
        /// <summary>
        /// Function: GetByEmailAsync
        /// Purpose: Retrieves a user by email. Returns null if not found.
        /// </summary>
        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _users.Find(u => u.Email == email).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Function: GetByIdAsync
        /// Purpose: Retrieves a user by unique identifier. Returns null if not found.
        /// </summary>
        public async Task<User?> GetByIdAsync(string id)
        {
            return await _users.Find(u => u.Id == id).FirstOrDefaultAsync();
        }

        // Creation Methods
        /// <summary>
        /// Function: CreateUserAsync
        /// Purpose: Inserts a new user document in the collection.
        /// </summary>
        public async Task CreateUserAsync(User user)
        {
            await _users.InsertOneAsync(user);
        }

        // Queries
        /// <summary>
        /// Function: GetCustomersAsync
        /// Purpose: Retrieves all users with Role equal to "customer".
        /// </summary>
        public async Task<List<User>> GetCustomersAsync()
        {
            var filter = Builders<User>.Filter.Eq(u => u.Role, "customer");
            return await _users.Find(filter).ToListAsync();
        }

        // Profile Updates
        /// <summary>
        /// Function: UpdateProfileAsync
        /// Purpose: Updates basic profile fields for the specified user.
        /// </summary>
        public async Task UpdateProfileAsync(string id, string name, string? phone, string? address)
        {
            var update = Builders<User>.Update
                .Set(u => u.Name, name)
                .Set(u => u.Phone, phone)
                .Set(u => u.Address, address);

            await _users.UpdateOneAsync(u => u.Id == id, update);
        }

        /// <summary>
        /// Function: UpdateProfileImageAsync
        /// Purpose: Updates the profile image URL for the specified user.
        /// </summary>
        public async Task UpdateProfileImageAsync(string id, string? profileImageUrl)
        {
            var update = Builders<User>.Update
                .Set(u => u.ProfileImageUrl, profileImageUrl);

            await _users.UpdateOneAsync(u => u.Id == id, update);
        }

        // Password Management
        /// <summary>
        /// Function: UpdatePasswordHashAsync
        /// Purpose: Replaces the user's password hash directly.
        /// </summary>
        public async Task UpdatePasswordHashAsync(string id, string newPasswordHash)
        {
            var update = Builders<User>.Update
                .Set(u => u.PasswordHash, newPasswordHash);

            await _users.UpdateOneAsync(u => u.Id == id, update);
        }

        /// <summary>
        /// Function: ChangePasswordAsync
        /// Purpose: Validates the current password and updates to the new password.
        /// </summary>
        public async Task<bool> ChangePasswordAsync(string id, string currentPassword, string newPassword)
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

        /// <summary>
        /// Function: UpdatePasswordAsync
        /// Purpose: Backward-compatible overload that delegates to ChangePasswordAsync.
        /// </summary>
        public Task<bool> UpdatePasswordAsync(string id, string currentPassword, string newPassword)
            => ChangePasswordAsync(id, currentPassword, newPassword);

        /// <summary>
        /// Function: UpdatePasswordAsync
        /// Purpose: Updates password without validating the current password.
        /// </summary>
        public async Task<bool> UpdatePasswordAsync(string id, string newPassword)
        {
            var newHash = PasswordHasher.Hash(newPassword);
            await UpdatePasswordHashAsync(id, newHash);
            return true;
        }
    }
}