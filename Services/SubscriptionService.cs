using Microsoft.EntityFrameworkCore;
using SubBill.Data;
using SubBill.Models;

namespace SubBill.Services
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly ApplicationDbContext _context;

        public SubscriptionService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<UserSubscription?> GetCurrentSubscriptionAsync(string userId)
        {
            var sub = await _context.UserSubscriptions
                .Include(s => s.Plan)
                    .ThenInclude(p => p!.Features)
                .Include(s => s.User)
                .Where(s => s.UserId == userId && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing))
                .OrderByDescending(s => s.StartDate)
                .FirstOrDefaultAsync();

            if (sub != null)
            {
                if (sub.Status == SubscriptionStatus.Trialing)
                {
                    // If trial period has ended, mark as Expired
                    if (sub.TrialEndDate.HasValue && DateTime.UtcNow > sub.TrialEndDate.Value)
                    {
                        sub.Status = SubscriptionStatus.Expired;
                        sub.AutoRenew = false;
                        sub.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                        return null;
                    }
                }
                else if (DateTime.UtcNow > sub.CurrentPeriodEnd)
                {
                    if (sub.AutoRenew)
                    {
                        // Renew period
                        sub.CurrentPeriodStart = sub.CurrentPeriodEnd;
                        sub.CurrentPeriodEnd = CalculatePeriodEnd(sub.CurrentPeriodStart, sub.Plan?.BillingCycle ?? BillingCycle.Monthly);
                        sub.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                    }
                    else
                    {
                        // Grace period ended, mark as Expired
                        sub.Status = SubscriptionStatus.Expired;
                        sub.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                        return null;
                    }
                }
            }

            return sub;
        }

        public async Task<UserSubscription> SubscribeAsync(string userId, int planId)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(planId);
            if (plan == null || !plan.IsActive)
            {
                throw new InvalidOperationException("Selected subscription plan is invalid or inactive.");
            }

            // Prevent multiple active subscriptions by expiring existing ones
            var existingSubs = await _context.UserSubscriptions
                .Where(s => s.UserId == userId && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing))
                .ToListAsync();

            foreach (var existing in existingSubs)
            {
                existing.Status = SubscriptionStatus.Cancelled;
                existing.AutoRenew = false;
                existing.CancelledAt = DateTime.UtcNow;
                existing.CancellationReason = "Replaced by new subscription";
                existing.UpdatedAt = DateTime.UtcNow;
            }

            var now = DateTime.UtcNow;
            var periodEnd = CalculatePeriodEnd(now, plan.BillingCycle);

            var newSub = new UserSubscription
            {
                UserId = userId,
                PlanId = plan.Id,
                StartDate = now,
                CurrentPeriodStart = now,
                CurrentPeriodEnd = periodEnd,
                Status = SubscriptionStatus.Active,
                AutoRenew = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.UserSubscriptions.Add(newSub);
            await _context.SaveChangesAsync();

            return newSub;
        }

        public async Task<UserSubscription> CancelSubscriptionAsync(string userId, int subscriptionId, string? reason)
        {
            var sub = await _context.UserSubscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == userId);

            if (sub == null)
            {
                throw new KeyNotFoundException("Subscription not found.");
            }

            if (sub.Status != SubscriptionStatus.Active && sub.Status != SubscriptionStatus.Trialing)
            {
                throw new InvalidOperationException("Only active or trialing subscriptions can be cancelled.");
            }

            // Subscription stays active until CurrentPeriodEnd, but AutoRenew is disabled
            sub.AutoRenew = false;
            sub.CancelledAt = DateTime.UtcNow;
            sub.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "User requested cancellation" : reason.Trim();
            sub.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return sub;
        }

        public async Task<UserSubscription> RenewSubscriptionAsync(int subscriptionId)
        {
            var sub = await _context.UserSubscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.Id == subscriptionId);

            if (sub == null)
            {
                throw new KeyNotFoundException("Subscription not found.");
            }

            var baseDate = sub.CurrentPeriodEnd > DateTime.UtcNow ? sub.CurrentPeriodEnd : DateTime.UtcNow;
            sub.CurrentPeriodStart = baseDate;
            sub.CurrentPeriodEnd = CalculatePeriodEnd(baseDate, sub.Plan?.BillingCycle ?? BillingCycle.Monthly);
            sub.Status = SubscriptionStatus.Active;
            sub.AutoRenew = true;
            sub.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return sub;
        }

        public async Task<UserSubscription> RenewUserSubscriptionAsync(string userId, int subscriptionId)
        {
            var sub = await _context.UserSubscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == userId);

            if (sub == null)
            {
                throw new KeyNotFoundException("Subscription not found.");
            }

            var baseDate = sub.CurrentPeriodEnd > DateTime.UtcNow ? sub.CurrentPeriodEnd : DateTime.UtcNow;
            sub.CurrentPeriodStart = baseDate;
            sub.CurrentPeriodEnd = CalculatePeriodEnd(baseDate, sub.Plan?.BillingCycle ?? BillingCycle.Monthly);
            sub.Status = SubscriptionStatus.Active;
            sub.AutoRenew = true;
            sub.CancelledAt = null;
            sub.CancellationReason = null;
            sub.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return sub;
        }

        public async Task<UserSubscription> ToggleAutoRenewAsync(string userId, int subscriptionId, bool enable)
        {
            var sub = await _context.UserSubscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == userId);

            if (sub == null)
            {
                throw new KeyNotFoundException("Subscription not found.");
            }

            sub.AutoRenew = enable;
            if (enable && sub.CancelledAt != null)
            {
                sub.CancelledAt = null;
                sub.CancellationReason = null;
                if (sub.Status == SubscriptionStatus.Cancelled && DateTime.UtcNow <= sub.CurrentPeriodEnd)
                {
                    sub.Status = SubscriptionStatus.Active;
                }
            }
            sub.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return sub;
        }

        public async Task<UserSubscription> ExpireSubscriptionAsync(int subscriptionId)
        {
            var sub = await _context.UserSubscriptions.FindAsync(subscriptionId);
            if (sub == null)
            {
                throw new KeyNotFoundException("Subscription not found.");
            }

            sub.Status = SubscriptionStatus.Expired;
            sub.AutoRenew = false;
            sub.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return sub;
        }

        public async Task<SubscriptionStatus> CheckSubscriptionStatusAsync(int subscriptionId)
        {
            var sub = await _context.UserSubscriptions.FindAsync(subscriptionId);
            if (sub == null)
            {
                throw new KeyNotFoundException("Subscription not found.");
            }

            if (DateTime.UtcNow > sub.CurrentPeriodEnd)
            {
                if (!sub.AutoRenew)
                {
                    sub.Status = SubscriptionStatus.Expired;
                    sub.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }

            return sub.Status;
        }

        public async Task<UserSubscription> ChangeSubscriptionPlanAsync(string userId, int newPlanId)
        {
            var currentSub = await GetCurrentSubscriptionAsync(userId);
            if (currentSub == null)
            {
                throw new InvalidOperationException("You must have an active subscription to change plans.");
            }

            if (currentSub.PlanId == newPlanId)
            {
                throw new InvalidOperationException("Cannot change to the same plan currently active.");
            }

            var newPlan = await _context.SubscriptionPlans.FindAsync(newPlanId);
            if (newPlan == null || !newPlan.IsActive)
            {
                throw new InvalidOperationException("Target subscription plan is invalid or inactive.");
            }

            var oldPlanId = currentSub.PlanId;
            var changeType = newPlan.Price > (currentSub.Plan?.Price ?? 0)
                ? PlanChangeType.Upgrade
                : PlanChangeType.Downgrade;

            // Record plan transition history
            var history = new SubscriptionHistory
            {
                SubscriptionId = currentSub.Id,
                OldPlanId = oldPlanId,
                NewPlanId = newPlan.Id,
                ChangeType = changeType,
                ChangedAt = DateTime.UtcNow
            };
            _context.SubscriptionHistories.Add(history);

            // Update subscription to new plan
            currentSub.PlanId = newPlan.Id;
            currentSub.Plan = newPlan;
            currentSub.CurrentPeriodStart = DateTime.UtcNow;
            currentSub.CurrentPeriodEnd = CalculatePeriodEnd(DateTime.UtcNow, newPlan.BillingCycle);
            currentSub.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return currentSub;
        }

        public async Task<UserSubscription> UpgradeSubscriptionAsync(string userId, int newPlanId)
        {
            var currentSub = await GetCurrentSubscriptionAsync(userId);
            if (currentSub == null)
            {
                throw new InvalidOperationException("You must have an active subscription to upgrade.");
            }

            var newPlan = await _context.SubscriptionPlans.FindAsync(newPlanId);
            if (newPlan == null || !newPlan.IsActive)
            {
                throw new InvalidOperationException("Target subscription plan is invalid or inactive.");
            }

            var currentPrice = currentSub.Plan?.Price ?? 0;
            if (newPlan.Price <= currentPrice)
            {
                throw new InvalidOperationException($"'{newPlan.Name}' is not an upgrade. Price must be higher than your current plan.");
            }

            return await ChangeSubscriptionPlanAsync(userId, newPlanId);
        }

        public async Task<UserSubscription> DowngradeSubscriptionAsync(string userId, int newPlanId)
        {
            var currentSub = await GetCurrentSubscriptionAsync(userId);
            if (currentSub == null)
            {
                throw new InvalidOperationException("You must have an active subscription to downgrade.");
            }

            var newPlan = await _context.SubscriptionPlans.FindAsync(newPlanId);
            if (newPlan == null || !newPlan.IsActive)
            {
                throw new InvalidOperationException("Target subscription plan is invalid or inactive.");
            }

            var currentPrice = currentSub.Plan?.Price ?? 0;
            if (newPlan.Price >= currentPrice)
            {
                throw new InvalidOperationException($"'{newPlan.Name}' is not a downgrade. Price must be lower than your current plan.");
            }

            return await ChangeSubscriptionPlanAsync(userId, newPlanId);
        }

        public async Task<bool> CanUserTakeTrialAsync(string userId, int planId)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(planId);
            if (plan == null || !plan.IsActive || plan.TrialDays <= 0)
            {
                return false;
            }

            // Check if user currently has an active paid subscription
            var hasActivePaid = await _context.UserSubscriptions
                .AnyAsync(s => s.UserId == userId && s.Status == SubscriptionStatus.Active);
            if (hasActivePaid)
            {
                return false;
            }

            // Abuse prevention: Check if user has already utilized a free trial on the platform or this plan
            var alreadyUsedTrial = await _context.UserSubscriptions
                .AnyAsync(s => s.UserId == userId && (s.HasUsedTrial || s.Status == SubscriptionStatus.Trialing || s.TrialStartDate != null));

            return !alreadyUsedTrial;
        }

        public async Task<UserSubscription> StartFreeTrialAsync(string userId, int planId)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(planId);
            if (plan == null || !plan.IsActive)
            {
                throw new InvalidOperationException("Selected subscription plan is invalid or inactive.");
            }

            if (plan.TrialDays <= 0)
            {
                throw new InvalidOperationException($"The '{plan.Name}' plan does not offer a free trial.");
            }

            var isCurrentlyActive = await _context.UserSubscriptions
                .AnyAsync(s => s.UserId == userId && s.PlanId == planId && s.Status == SubscriptionStatus.Active);
            if (isCurrentlyActive)
            {
                throw new InvalidOperationException($"You already have an active subscription to the '{plan.Name}' plan.");
            }

            var isCurrentlyTrialing = await _context.UserSubscriptions
                .AnyAsync(s => s.UserId == userId && s.Status == SubscriptionStatus.Trialing && s.TrialEndDate > DateTime.UtcNow);
            if (isCurrentlyTrialing)
            {
                throw new InvalidOperationException("You already have an active free trial in progress. You can upgrade to a paid plan anytime.");
            }

            var alreadyUsedTrial = await _context.UserSubscriptions
                .AnyAsync(s => s.UserId == userId && (s.HasUsedTrial || s.TrialStartDate != null));
            if (alreadyUsedTrial)
            {
                throw new InvalidOperationException("You have already redeemed a free trial on this platform. Free trials are limited to one per account.");
            }

            // Clean up any existing records
            var existingSubs = await _context.UserSubscriptions
                .Where(s => s.UserId == userId && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing))
                .ToListAsync();

            foreach (var existing in existingSubs)
            {
                existing.Status = SubscriptionStatus.Cancelled;
                existing.AutoRenew = false;
                existing.CancelledAt = DateTime.UtcNow;
                existing.CancellationReason = "Cancelled to start trial";
                existing.UpdatedAt = DateTime.UtcNow;
            }

            var now = DateTime.UtcNow;
            var trialEnd = now.AddDays(plan.TrialDays);

            var trialSub = new UserSubscription
            {
                UserId = userId,
                PlanId = plan.Id,
                StartDate = now,
                CurrentPeriodStart = now,
                CurrentPeriodEnd = trialEnd,
                TrialStartDate = now,
                TrialEndDate = trialEnd,
                HasUsedTrial = true,
                Status = SubscriptionStatus.Trialing,
                AutoRenew = false, // Do not charge during trial unless explicitly configured/paid
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.UserSubscriptions.Add(trialSub);
            await _context.SaveChangesAsync();

            trialSub.Plan = plan;
            return trialSub;
        }

        public async Task<int> ProcessExpiredTrialsAsync()
        {
            var now = DateTime.UtcNow;
            var expiredTrials = await _context.UserSubscriptions
                .Where(s => s.Status == SubscriptionStatus.Trialing && s.TrialEndDate.HasValue && s.TrialEndDate.Value <= now)
                .ToListAsync();

            foreach (var sub in expiredTrials)
            {
                sub.Status = SubscriptionStatus.Expired;
                sub.AutoRenew = false;
                sub.UpdatedAt = now;
            }

            if (expiredTrials.Count > 0)
            {
                await _context.SaveChangesAsync();
            }

            return expiredTrials.Count;
        }

        public async Task<List<SubscriptionHistory>> GetUserSubscriptionHistoryAsync(string userId)
        {
            return await _context.SubscriptionHistories
                .Include(h => h.OldPlan)
                .Include(h => h.NewPlan)
                .Include(h => h.Subscription)
                .Where(h => h.Subscription != null && h.Subscription.UserId == userId)
                .OrderByDescending(h => h.ChangedAt)
                .ToListAsync();
        }

        public async Task<List<SubscriptionHistory>> GetAllSubscriptionHistoriesAsync()
        {
            return await _context.SubscriptionHistories
                .Include(h => h.OldPlan)
                .Include(h => h.NewPlan)
                .Include(h => h.Subscription)
                    .ThenInclude(s => s!.User)
                .OrderByDescending(h => h.ChangedAt)
                .ToListAsync();
        }

        public DateTime CalculatePeriodEnd(DateTime startDate, BillingCycle cycle)
        {
            return cycle switch
            {
                BillingCycle.Monthly => startDate.AddMonths(1),
                BillingCycle.Quarterly => startDate.AddMonths(3),
                BillingCycle.Yearly => startDate.AddYears(1),
                _ => startDate.AddMonths(1)
            };
        }
    }
}
