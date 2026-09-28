using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubBill.Models
{
    public class SubscriptionHistory
    {
        public int Id { get; set; }

        [Required]
        public int SubscriptionId { get; set; }
        [ForeignKey("SubscriptionId")]
        public UserSubscription? Subscription { get; set; }

        [Required]
        public int OldPlanId { get; set; }
        [ForeignKey("OldPlanId")]
        public SubscriptionPlan? OldPlan { get; set; }

        [Required]
        public int NewPlanId { get; set; }
        [ForeignKey("NewPlanId")]
        public SubscriptionPlan? NewPlan { get; set; }

        public PlanChangeType ChangeType { get; set; }

        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    }
}
