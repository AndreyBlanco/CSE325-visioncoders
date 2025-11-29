using System;

namespace CSE325_visioncoders.Models
{
    public enum ConfirmationStatus
    {
        Pending,
        Confirmed,
        Canceled
    }

    public class Confirmation
    {
        public int Id { get; set; }

        // Fecha del menú al que pertenece la confirmación
        public DateTime Date { get; set; }

        // Plato específico: 1,2,3
        public int DishIndex { get; set; }

        // Identificador del cliente
        public string ClientId { get; set; } = string.Empty;

        public ConfirmationStatus Status { get; set; } = ConfirmationStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? CanceledAt { get; set; }
    }
}
