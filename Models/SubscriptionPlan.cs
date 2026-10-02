using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace SubBill.Models
{
    public class SubscriptionPlan
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(300)]
        public string Description { get; set; } = string.Empty;

        [Range(0, 100000)]
        [DataType(DataType.Currency)]
        public decimal Price { get; set; }

        public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

        [Range(0, 365, ErrorMessage = "Trial days must be between 0 and 365.")]
        [Display(Name = "Free Trial Days")]
        public int TrialDays { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        public ICollection<PlanFeature> Features { get; set; } = new List<PlanFeature>();
    }
    public enum BillingCycle
    {
        Monthly,
        Quarterly,
        Yearly
    }
}
