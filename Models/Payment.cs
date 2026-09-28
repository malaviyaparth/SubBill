using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubBill.Models
{
    public class Payment
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        public int? SubscriptionId { get; set; }
        [ForeignKey("SubscriptionId")]
        public UserSubscription? Subscription { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [MaxLength(10)]
        public string Currency { get; set; } = "INR";

        [MaxLength(50)]
        public string PaymentGateway { get; set; } = "Razorpay";

        [MaxLength(100)]
        public string OrderId { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? PaymentId { get; set; }

        [MaxLength(255)]
        public string? Signature { get; set; }

        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

        public DateTime? PaymentDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
