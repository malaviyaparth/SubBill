using System.ComponentModel.DataAnnotations;

namespace SubBill.Models
{
    public class AuditLog
    {
        public int Id { get; set; }

        [Required, StringLength(256)]
        public string AdminEmail { get; set; } = string.Empty;

        [Required, StringLength(100)]
        public string Action { get; set; } = string.Empty;

        [Required, StringLength(100)]
        public string EntityType { get; set; } = string.Empty;

        [StringLength(100)]
        public string? EntityId { get; set; }

        [StringLength(1000)]
        public string? Details { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [StringLength(50)]
        public string? IpAddress { get; set; }
    }
}
