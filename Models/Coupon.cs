using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubBill.Models
{
    public enum DiscountType
    {
        Percentage,
        FixedAmount
    }

    public class Coupon
    {
        public int Id { get; set; }

        [Required, StringLength(50)]
        public string Code { get; set; } = string.Empty;

        public DiscountType DiscountType { get; set; } = DiscountType.Percentage;

        [Column(TypeName = "decimal(18,2)")]
        [Range(0.01, 100000, ErrorMessage = "Discount value must be greater than zero.")]
        public decimal DiscountValue { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Range(0, 100000)]
        public decimal? MaxDiscount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Range(0, 100000)]
        public decimal MinimumAmount { get; set; } = 0;

        [Range(1, int.MaxValue)]
        public int? UsageLimit { get; set; }

        public int UsedCount { get; set; } = 0;

        public DateTime ValidFrom { get; set; } = DateTime.UtcNow;

        public DateTime ValidUntil { get; set; } = DateTime.UtcNow.AddMonths(1);

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
