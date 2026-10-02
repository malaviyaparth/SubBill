namespace SubBill.Models
{
    public class AdminDashboardViewModel
    {
        public string TimeRange { get; set; } = "30days";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; } = DateTime.UtcNow;

        // KPI Metric Cards
        public int TotalUsers { get; set; }
        public int ActiveSubscriptions { get; set; }
        public int TrialingSubscriptions { get; set; }
        public int CancelledSubscriptions { get; set; }
        public int ExpiredSubscriptions { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal CurrentMonthRevenue { get; set; }
        public int FailedPayments { get; set; }

        // Chart 1: Revenue by month
        public List<string> RevenueMonthLabels { get; set; } = new();
        public List<decimal> RevenueMonthData { get; set; } = new();

        // Chart 2: Subscription growth
        public List<string> GrowthLabels { get; set; } = new();
        public List<int> GrowthData { get; set; } = new();

        // Chart 3: Active vs cancelled subscriptions
        public List<string> SubscriptionStatusLabels { get; set; } = new();
        public List<int> SubscriptionStatusData { get; set; } = new();

        // Chart 4: Plan distribution
        public List<string> PlanDistributionLabels { get; set; } = new();
        public List<int> PlanDistributionData { get; set; } = new();

        // Chart 5: Payment success vs failure
        public List<string> PaymentStatusLabels { get; set; } = new();
        public List<int> PaymentStatusData { get; set; } = new();
    }
}
