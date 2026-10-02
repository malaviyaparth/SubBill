using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubBill.Models
{
    public class PlanFeature
    {
        public int Id { get; set; }

        [Required]
        public int PlanId { get; set; }

        [ForeignKey("PlanId")]
        public SubscriptionPlan? Plan { get; set; }

        [Required(ErrorMessage = "Feature name is required.")]
        [StringLength(100, ErrorMessage = "Feature name cannot exceed 100 characters.")]
        public string FeatureName { get; set; } = string.Empty;

        [StringLength(100, ErrorMessage = "Feature value cannot exceed 100 characters.")]
        public string FeatureValue { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
