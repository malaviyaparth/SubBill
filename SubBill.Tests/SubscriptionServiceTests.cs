using Microsoft.EntityFrameworkCore;
using SubBill.Data;
using SubBill.Models;
using SubBill.Services;
using Xunit;

namespace SubBill.Tests
{
    public class SubscriptionServiceTests
    {
        private ApplicationDbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }

        private async Task<(SubscriptionService service, ApplicationDbContext context)> CreateServiceWithSeedAsync()
        {
            var context = CreateInMemoryDbContext();

            // Seed Plans with Features & Trials
            var basicPlan = new SubscriptionPlan
            {
                Id = 1,
                Name = "Basic",
                Description = "Basic Plan",
                Price = 10m,
                BillingCycle = BillingCycle.Monthly,
                TrialDays = 14,
                IsActive = true
            };

            var proPlan = new SubscriptionPlan
            {
                Id = 2,
                Name = "Pro",
                Description = "Pro Plan",
                Price = 30m,
                BillingCycle = BillingCycle.Monthly,
                TrialDays = 7,
                IsActive = true
            };

            context.SubscriptionPlans.AddRange(basicPlan, proPlan);

            context.PlanFeatures.AddRange(
                new PlanFeature { Id = 1, PlanId = 1, FeatureName = "Projects", FeatureValue = "5", CreatedAt = DateTime.UtcNow },
                new PlanFeature { Id = 2, PlanId = 1, FeatureName = "Storage", FeatureValue = "10GB", CreatedAt = DateTime.UtcNow },
                new PlanFeature { Id = 3, PlanId = 2, FeatureName = "Projects", FeatureValue = "50", CreatedAt = DateTime.UtcNow },
                new PlanFeature { Id = 4, PlanId = 2, FeatureName = "Storage", FeatureValue = "100GB", CreatedAt = DateTime.UtcNow },
                new PlanFeature { Id = 5, PlanId = 2, FeatureName = "API Access", FeatureValue = "Yes", CreatedAt = DateTime.UtcNow }
            );

            // Seed Users
            context.Users.AddRange(
                new ApplicationUser { Id = "test-user-trial", UserName = "test-user-trial@test.com", Email = "test-user-trial@test.com" },
                new ApplicationUser { Id = "abuse-user-1", UserName = "abuse-user-1@test.com", Email = "abuse-user-1@test.com" },
                new ApplicationUser { Id = "user-upgrade", UserName = "user-upgrade@test.com", Email = "user-upgrade@test.com" },
                new ApplicationUser { Id = "user-downgrade", UserName = "user-downgrade@test.com", Email = "user-downgrade@test.com" },
                new ApplicationUser { Id = "expired-trial-user", UserName = "expired-trial-user@test.com", Email = "expired-trial-user@test.com" },
                new ApplicationUser { Id = "user-features", UserName = "user-features@test.com", Email = "user-features@test.com" }
            );

            await context.SaveChangesAsync();

            var service = new SubscriptionService(context);
            return (service, context);
        }

        [Fact]
        public async Task StartFreeTrial_ValidPlan_StartsTrialingSubscription()
        {
            // Arrange
            var (service, context) = await CreateServiceWithSeedAsync();
            var userId = "test-user-trial";

            // Act
            var sub = await service.StartFreeTrialAsync(userId, 1);

            // Assert
            Assert.NotNull(sub);
            Assert.Equal(SubscriptionStatus.Trialing, sub.Status);
            Assert.True(sub.HasUsedTrial);
            Assert.NotNull(sub.TrialStartDate);
            Assert.NotNull(sub.TrialEndDate);
            Assert.Equal(14, sub.RemainingTrialDays);
            Assert.False(sub.AutoRenew);
        }

        [Fact]
        public async Task StartFreeTrial_AbusePrevention_CannotTakeSecondTrial()
        {
            // Arrange
            var (service, context) = await CreateServiceWithSeedAsync();
            var userId = "abuse-user-1";

            // First trial
            await service.StartFreeTrialAsync(userId, 1);

            // Act & Assert - second trial attempt must be rejected
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await service.StartFreeTrialAsync(userId, 2);
            });
        }

        [Fact]
        public async Task UpgradeSubscription_BasicToPro_RecordsUpgradeHistory()
        {
            // Arrange
            var (service, context) = await CreateServiceWithSeedAsync();
            var userId = "user-upgrade";

            // Subscribe to Basic
            await service.SubscribeAsync(userId, 1);

            // Act
            var upgraded = await service.UpgradeSubscriptionAsync(userId, 2);

            // Assert
            Assert.Equal(2, upgraded.PlanId);

            var histories = await service.GetUserSubscriptionHistoryAsync(userId);
            Assert.Single(histories);
            Assert.Equal(PlanChangeType.Upgrade, histories[0].ChangeType);
            Assert.Equal(1, histories[0].OldPlanId);
            Assert.Equal(2, histories[0].NewPlanId);
        }

        [Fact]
        public async Task DowngradeSubscription_ProToBasic_RecordsDowngradeHistory()
        {
            // Arrange
            var (service, context) = await CreateServiceWithSeedAsync();
            var userId = "user-downgrade";

            // Subscribe to Pro
            await service.SubscribeAsync(userId, 2);

            // Act
            var downgraded = await service.DowngradeSubscriptionAsync(userId, 1);

            // Assert
            Assert.Equal(1, downgraded.PlanId);

            var histories = await service.GetUserSubscriptionHistoryAsync(userId);
            Assert.Single(histories);
            Assert.Equal(PlanChangeType.Downgrade, histories[0].ChangeType);
            Assert.Equal(2, histories[0].OldPlanId);
            Assert.Equal(1, histories[0].NewPlanId);
        }

        [Fact]
        public async Task ProcessExpiredTrials_ExpiredTrial_MarksAsExpired()
        {
            // Arrange
            var (service, context) = await CreateServiceWithSeedAsync();
            var userId = "expired-trial-user";

            // Add an already expired trial
            var sub = new UserSubscription
            {
                UserId = userId,
                PlanId = 1,
                StartDate = DateTime.UtcNow.AddDays(-20),
                CurrentPeriodStart = DateTime.UtcNow.AddDays(-20),
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(-6),
                TrialStartDate = DateTime.UtcNow.AddDays(-20),
                TrialEndDate = DateTime.UtcNow.AddDays(-6), // ended 6 days ago
                Status = SubscriptionStatus.Trialing,
                HasUsedTrial = true,
                AutoRenew = false,
                CreatedAt = DateTime.UtcNow.AddDays(-20)
            };
            context.UserSubscriptions.Add(sub);
            await context.SaveChangesAsync();

            // Act
            var count = await service.ProcessExpiredTrialsAsync();

            // Assert
            Assert.Equal(1, count);
            var updated = await context.UserSubscriptions.FindAsync(sub.Id);
            Assert.Equal(SubscriptionStatus.Expired, updated?.Status);
        }

        [Fact]
        public async Task PlanFeatures_EagerlyLoadedWithSubscription()
        {
            // Arrange
            var (service, context) = await CreateServiceWithSeedAsync();
            var userId = "user-features";

            await service.SubscribeAsync(userId, 2);

            // Act
            var current = await service.GetCurrentSubscriptionAsync(userId);

            // Assert
            Assert.NotNull(current);
            Assert.NotNull(current.Plan);
            Assert.NotNull(current.Plan.Features);
            Assert.Equal(3, current.Plan.Features.Count);
        }
    }
}
