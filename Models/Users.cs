using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CSE325_visioncoders.Models
{
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("name")]
        public string Name { get; set; } = string.Empty;

        [BsonElement("email")]
        public string Email { get; set; } = string.Empty;

        [BsonElement("passwordHash")]
        public string PasswordHash { get; set; } = string.Empty;

        [BsonElement("role")]
        public string Role { get; set; } = "customer";

        [BsonElement("phone")]
        public string? Phone { get; set; }

        [BsonElement("address")]
        public string? Address { get; set; }
    }
}
