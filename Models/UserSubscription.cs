using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SubBill.Models
{
    public class UserSubscription
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; }
        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; }

        [Required]
        public int PlanId { get; set; }
        [ForeignKey("PlanId")]
        public SubscriptionPlan Plan { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime ExpiryDate { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } // "Active" / "Expired" / "Cancelled"
    }
}
