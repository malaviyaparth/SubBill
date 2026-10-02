namespace SubBill.Models
{
    public class UserBillingDashboardViewModel
    {
        public UserSubscription? CurrentSubscription { get; set; }
        public List<SubscriptionPlan> AvailablePlans { get; set; } = new();

        // Usage / Subscription summary
        public BillingUsageSummary Summary { get; set; } = new();

        // Paginated tables
        public PaginatedList<PaymentHistoryItemViewModel> Payments { get; set; } = new();
        public PaginatedList<Invoice> Invoices { get; set; } = new();
        public PaginatedList<SubscriptionHistory> Histories { get; set; } = new();

        // Active tab tracking
        public string ActiveTab { get; set; } = "overview";
    }

    public class BillingUsageSummary
    {
        public int DaysActive { get; set; }
        public int DaysRemainingInPeriod { get; set; }
        public int TotalPeriodDays { get; set; }
        public int PeriodProgressPercent { get; set; }
        public decimal TotalSpent { get; set; }
        public string Currency { get; set; } = "INR";
        public int CompletedPaymentsCount { get; set; }
        public int TotalInvoicesCount { get; set; }
        public int PlanChangesCount { get; set; }
        public string MemberSinceFormatted { get; set; } = string.Empty;
    }

    public class PaymentHistoryItemViewModel
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "INR";
        public PaymentStatus Status { get; set; }
        public string? OrderId { get; set; }
        public string? PaymentId { get; set; }
        public int? InvoiceId { get; set; }
        public string? InvoiceNumber { get; set; }
    }

    public class PaginatedList<T>
    {
        public List<T> Items { get; set; } = new();
        public int PageIndex { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public int TotalCount { get; set; } = 0;
        public int PageSize { get; set; } = 5;

        public bool HasPreviousPage => PageIndex > 1;
        public bool HasNextPage => PageIndex < TotalPages;

        public PaginatedList() { }

        public PaginatedList(List<T> items, int count, int pageIndex, int pageSize)
        {
            PageIndex = pageIndex;
            TotalCount = count;
            PageSize = pageSize;
            TotalPages = (int)Math.Ceiling(count / (double)pageSize);
            if (TotalPages < 1) TotalPages = 1;
            Items = items;
        }
    }
}
