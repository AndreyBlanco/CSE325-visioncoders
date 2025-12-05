using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CSE325_visioncoders.Models
{
    public class Order
    {
        [BsonId, BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string CookId { get; set; } = default!;

        [BsonRepresentation(BsonType.ObjectId)]
        public string CustomerId { get; set; } = default!;

        [BsonRepresentation(BsonType.ObjectId)]
        public string MealId { get; set; } = default!;

        public DateTime DeliveryDateUtc { get; set; }

        public DateTime CancelUntilUtc { get; set; }

        public string TimeZone { get; set; } = "America/Bogota";

        public decimal PriceAtOrder { get; set; }

        public OrderStatus Status { get; set; } = OrderStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public string? Notes { get; set; }
    }

    public enum OrderStatus
    {
        Pending,
        Cancelled,
        Delivered
    }
}