using SubBill.Models;

namespace SubBill.Services
{
    public interface ISubscriptionService
    {
        Task<UserSubscription?> GetCurrentSubscriptionAsync(string userId);
        Task<UserSubscription> SubscribeAsync(string userId, int planId);
        Task<UserSubscription> CancelSubscriptionAsync(string userId, int subscriptionId, string? reason);
        Task<UserSubscription> RenewSubscriptionAsync(int subscriptionId);
        Task<UserSubscription> RenewUserSubscriptionAsync(string userId, int subscriptionId);
        Task<UserSubscription> ToggleAutoRenewAsync(string userId, int subscriptionId, bool enable);
        Task<UserSubscription> ExpireSubscriptionAsync(int subscriptionId);
        Task<SubscriptionStatus> CheckSubscriptionStatusAsync(int subscriptionId);
        Task<UserSubscription> ChangeSubscriptionPlanAsync(string userId, int newPlanId);
        Task<List<SubscriptionHistory>> GetUserSubscriptionHistoryAsync(string userId);
        Task<List<SubscriptionHistory>> GetAllSubscriptionHistoriesAsync();
        DateTime CalculatePeriodEnd(DateTime startDate, BillingCycle cycle);
    }
}
