using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace CSE325_visioncoders.Models
{
    public class Meal
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string CookId { get; set; } = default!;

        [Required]
        [BsonElement("name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [BsonElement("description")]
        public string Description { get; set; } = string.Empty;

        [BsonElement("ingredients")]
        public string? Ingredients { get; set; }

        [Required]
        [Range(0.01, 9999)]
        [BsonElement("base_price")]
        public decimal Price { get; set; }

        [BsonElement("image_url")]
        public string? ImageUrl { get; set; }

        [BsonElement("cook_name")]
        public string? CookName { get; set; }

        [BsonElement("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        [BsonElement("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("is_active")]
        public bool IsActive { get; set; } = true;
    }
}
