using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SubBill.Data;
using SubBill.Models;
using SubBill.Services;
using Xunit;

namespace SubBill.Tests
{
    public class InvoiceServiceTests
    {
        private ApplicationDbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }

        private async Task<(InvoiceService service, ApplicationDbContext context, UserSubscription subscription, Payment payment)> CreateTestDataAsync()
        {
            var context = CreateInMemoryDbContext();

            // 1. Seed Plan with Features
            var plan = new SubscriptionPlan
            {
                Id = 1,
                Name = "Pro",
                Description = "Pro Plan Description",
                Price = 999.00m,
                BillingCycle = BillingCycle.Monthly,
                TrialDays = 7,
                IsActive = true
            };
            context.SubscriptionPlans.Add(plan);

            var features = new List<PlanFeature>
            {
                new PlanFeature { Id = 1, PlanId = 1, FeatureName = "Projects", FeatureValue = "50 Projects" },
                new PlanFeature { Id = 2, PlanId = 1, FeatureName = "Storage", FeatureValue = "100 GB Cloud Storage" },
                new PlanFeature { Id = 3, PlanId = 1, FeatureName = "Team Members", FeatureValue = "10 Users" },
                new PlanFeature { Id = 4, PlanId = 1, FeatureName = "Support", FeatureValue = "24/7 Priority Support" }
            };
            context.PlanFeatures.AddRange(features);

            // 2. Seed User & Subscription
            var user = new ApplicationUser
            {
                Id = "user_test_123",
                Email = "testuser@example.com",
                UserName = "testuser@example.com",
                FullName = "Jane Doe"
            };
            context.Users.Add(user);

            var now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            var periodEnd = now.AddMonths(1);

            var subscription = new UserSubscription
            {
                Id = 10,
                UserId = user.Id,
                PlanId = plan.Id,
                Plan = plan,
                User = user,
                StartDate = now,
                CurrentPeriodStart = now,
                CurrentPeriodEnd = periodEnd,
                Status = SubscriptionStatus.Active,
                AutoRenew = true
            };
            context.UserSubscriptions.Add(subscription);

            // 3. Seed Coupon and Coupon Usage
            var coupon = new Coupon
            {
                Id = 1,
                Code = "SAVE200",
                DiscountType = DiscountType.FixedAmount,
                DiscountValue = 200m,
                IsActive = true,
                ValidFrom = now.AddDays(-5),
                ValidUntil = now.AddDays(30)
            };
            context.Coupons.Add(coupon);

            var couponUsage = new CouponUsage
            {
                Id = 1,
                CouponId = coupon.Id,
                Coupon = coupon,
                UserId = user.Id,
                SubscriptionId = subscription.Id,
                DiscountAmount = 200m,
                UsedAt = now
            };
            context.CouponUsages.Add(couponUsage);

            // 4. Seed Successful Payment (Discounted total: 999 - 200 = 799)
            var payment = new Payment
            {
                Id = 20,
                UserId = user.Id,
                SubscriptionId = subscription.Id,
                Amount = 799.00m,
                Currency = "INR",
                PaymentGateway = "Razorpay (Test Sandbox)",
                OrderId = "order_test_999",
                PaymentId = "pay_test_999",
                Status = PaymentStatus.Success,
                PaymentDate = now
            };
            context.Payments.Add(payment);

            await context.SaveChangesAsync();

            var service = new InvoiceService(context, NullLogger<InvoiceService>.Instance);
            return (service, context, subscription, payment);
        }

        [Fact]
        public async Task CreateInvoiceForPaymentAsync_CalculatesProperGstAndNetValues()
        {
            var (service, context, subscription, payment) = await CreateTestDataAsync();

            var invoice = await service.CreateInvoiceForPaymentAsync(payment, subscription, "SAVE200");

            Assert.NotNull(invoice);
            Assert.StartsWith("INV-", invoice.InvoiceNumber);
            Assert.Equal(799.00m, invoice.TotalAmount);

            // GST @ 18% inclusive: 799 * 0.18 / 1.18 = 121.88
            Assert.Equal(121.88m, invoice.TaxAmount);
            // Net Amount: 799 - 121.88 = 677.12
            Assert.Equal(677.12m, invoice.Amount);
            Assert.Equal(invoice.TotalAmount, invoice.Amount + invoice.TaxAmount);

            // CGST (9%) + SGST (9%)
            Assert.Equal(60.94m, invoice.CGST);
            Assert.Equal(60.94m, invoice.SGST);
            Assert.Equal(invoice.TaxAmount, invoice.CGST + invoice.SGST);

            // Coupon
            Assert.Equal("SAVE200", invoice.CouponCode);
            Assert.Equal(200m, invoice.CouponDiscount);
            Assert.Equal(999.00m, invoice.BasePlanPrice);

            // Period
            Assert.Equal(subscription.CurrentPeriodStart, invoice.PeriodStart);
            Assert.Equal(subscription.CurrentPeriodEnd, invoice.PeriodEnd);

            // SAC Code
            Assert.Equal("998313", invoice.SacCode);
        }

        [Fact]
        public async Task GetInvoiceByIdAsync_PopulatesFeaturesPeriodAndCoupon()
        {
            var (service, context, subscription, payment) = await CreateTestDataAsync();
            var created = await service.CreateInvoiceForPaymentAsync(payment, subscription, "SAVE200");

            var fetched = await service.GetInvoiceByIdAsync(created.Id);

            Assert.NotNull(fetched);
            Assert.NotNull(fetched.Subscription);
            Assert.NotNull(fetched.Subscription.Plan);
            Assert.NotNull(fetched.Subscription.Plan.Features);
            Assert.Equal(4, fetched.Subscription.Plan.Features.Count);

            // Verify features
            var featureNames = fetched.Subscription.Plan.Features.Select(f => f.FeatureName).ToList();
            Assert.Contains("Projects", featureNames);
            Assert.Contains("Storage", featureNames);
            Assert.Contains("Team Members", featureNames);
            Assert.Contains("Support", featureNames);

            // Verify period
            Assert.Equal(subscription.CurrentPeriodStart, fetched.PeriodStart);
            Assert.Equal(subscription.CurrentPeriodEnd, fetched.PeriodEnd);

            // Verify coupon
            Assert.Equal("SAVE200", fetched.CouponCode);
            Assert.Equal(200m, fetched.CouponDiscount);

            // Verify GST breakdown
            Assert.Equal(60.94m, fetched.CGST);
            Assert.Equal(60.94m, fetched.SGST);
            Assert.Equal(121.88m, fetched.TaxAmount);
        }

        [Fact]
        public async Task GenerateInvoicePdf_GeneratesValidPdfBytes()
        {
            var (service, context, subscription, payment) = await CreateTestDataAsync();
            var invoice = await service.CreateInvoiceForPaymentAsync(payment, subscription, "SAVE200");

            var pdfBytes = service.GenerateInvoicePdf(invoice);

            Assert.NotNull(pdfBytes);
            Assert.NotEmpty(pdfBytes);
            // PDF file header signature starts with %PDF
            Assert.True(pdfBytes.Length > 100);
            var header = System.Text.Encoding.ASCII.GetString(pdfBytes.Take(4).ToArray());
            Assert.Equal("%PDF", header);
        }
    }
}
