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

        public bool IsActive { get; set; } = true;
    }
    public enum BillingCycle
    {
        [Display(Name = "1 Month (Monthly)")]
        Monthly = 0,

        [Display(Name = "3 Months (Quarterly)")]
        Quarterly = 1,

        [Display(Name = "6 Months (Semi-Annually)")]
        SemiAnnually = 3,

        [Display(Name = "12 Months (Yearly)")]
        Yearly = 2
    }

}
