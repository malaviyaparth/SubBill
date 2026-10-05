using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubBill.Models
{
    public class UserSubscription
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;
        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        [Required]
        public int PlanId { get; set; }
        [ForeignKey("PlanId")]
        public SubscriptionPlan? Plan { get; set; }

        public DateTime StartDate { get; set; } = DateTime.UtcNow;

        public DateTime CurrentPeriodStart { get; set; } = DateTime.UtcNow;

        public DateTime CurrentPeriodEnd { get; set; } = DateTime.UtcNow;

        // Backward compatibility property for existing code
        [NotMapped]
        public DateTime ExpiryDate
        {
            get => CurrentPeriodEnd;
            set => CurrentPeriodEnd = value;
        }

        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

        public bool AutoRenew { get; set; } = true;

        // Phase 10: Free Trial Support
        public DateTime? TrialStartDate { get; set; }
        public DateTime? TrialEndDate { get; set; }
        public bool HasUsedTrial { get; set; } = false;

        [NotMapped]
        public int RemainingTrialDays =>
            Status == SubscriptionStatus.Trialing && TrialEndDate.HasValue
                ? Math.Max(0, (int)Math.Ceiling((TrialEndDate.Value - DateTime.UtcNow).TotalDays))
                : 0;

        public DateTime? CancelledAt { get; set; }

        [MaxLength(500)]
        public string? CancellationReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
