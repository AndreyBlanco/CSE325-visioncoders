using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CSE325_visioncoders.Models
{
    public class InventoryItem
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        // Owner cook's ID
        [BsonElement("cookId")]
        public string? CookId { get; set; } 

        [BsonElement("name")]
        public string Name { get; set; } = string.Empty;

        [BsonElement("quantity")]
        public decimal Quantity { get; set; }

        [BsonElement("unit")]
        public string Unit { get; set; } = "pcs";

        [BsonElement("lowStockThreshold")]
        public decimal? LowStockThreshold { get; set; }

        [BsonElement("lastUpdated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        [BsonElement("notes")]
        public string? Notes { get; set; }
    }
}
