using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubBill.Models
{
    public class Invoice
    {
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string InvoiceNumber { get; set; } = string.Empty;

        [Required]
        public string UserId { get; set; } = string.Empty;
        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        public int? SubscriptionId { get; set; }
        [ForeignKey("SubscriptionId")]
        public UserSubscription? Subscription { get; set; }

        public int? PaymentId { get; set; }
        [ForeignKey("PaymentId")]
        public Payment? Payment { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TaxAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        [MaxLength(10)]
        public string Currency { get; set; } = "INR";

        public DateTime InvoiceDate { get; set; } = DateTime.UtcNow;

        public DateTime DueDate { get; set; } = DateTime.UtcNow;

        public InvoiceStatus Status { get; set; } = InvoiceStatus.Paid;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Computed / Non-persisted metadata for rich presentation & PDF rendering
        [NotMapped]
        public string? CouponCode { get; set; }

        [NotMapped]
        public decimal CouponDiscount { get; set; }

        [NotMapped]
        public decimal BasePlanPrice { get; set; }

        [NotMapped]
        public decimal CGST => Math.Round(TaxAmount / 2m, 2);

        [NotMapped]
        public decimal SGST => TaxAmount - CGST;

        [NotMapped]
        public string SacCode => "998313";

        [NotMapped]
        public string ServiceDescription => "Cloud Software as a Service (SaaS) Platform Subscription";

        [NotMapped]
        public DateTime PeriodStart => Subscription?.CurrentPeriodStart ?? InvoiceDate;

        [NotMapped]
        public DateTime PeriodEnd => Subscription?.CurrentPeriodEnd ?? DueDate;
    }
}
