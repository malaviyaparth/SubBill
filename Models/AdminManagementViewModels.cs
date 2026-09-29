namespace SubBill.Models
{
    public class AdminUserListItemViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "User";
        public bool IsDisabled { get; set; }
        public string AccountStatus => IsDisabled ? "Disabled" : "Active";
        public string? CurrentPlan { get; set; }
        public SubscriptionStatus? SubscriptionStatus { get; set; }
        public DateTime RegistrationDate { get; set; }
        public int SubscriptionsCount { get; set; }
        public int PaymentsCount { get; set; }
        public int InvoicesCount { get; set; }
    }

    public class AdminUserDetailsViewModel
    {
        public ApplicationUser User { get; set; } = null!;
        public string Role { get; set; } = "User";
        public bool IsDisabled { get; set; }
        public UserSubscription? ActiveSubscription { get; set; }
        public PaginatedList<UserSubscription> Subscriptions { get; set; } = new();
        public PaginatedList<Payment> Payments { get; set; } = new();
        public PaginatedList<Invoice> Invoices { get; set; } = new();
        public PaginatedList<CouponUsage> CouponUsages { get; set; } = new();
    }

    public class AdminSubscriptionListViewModel
    {
        public PaginatedList<UserSubscription> Subscriptions { get; set; } = new();
        public List<SubscriptionPlan> Plans { get; set; } = new();
        public string? Search { get; set; }
        public SubscriptionStatus? Status { get; set; }
        public int? PlanId { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public class AdminPaymentListViewModel
    {
        public PaginatedList<Payment> Payments { get; set; } = new();
        public string? Search { get; set; }
        public PaymentStatus? Status { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public class AdminInvoiceListViewModel
    {
        public PaginatedList<Invoice> Invoices { get; set; } = new();
        public string? Search { get; set; }
        public InvoiceStatus? Status { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }

    public class AdminCouponsViewModel
    {
        public PaginatedList<Coupon> Coupons { get; set; } = new();
        public PaginatedList<CouponUsage> Usages { get; set; } = new();
        public int? SelectedCouponId { get; set; }
    }
}
