using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SubBill.Data;
using SubBill.Models;
using SubBill.Services;
using Xunit;

namespace SubBill.Tests
{
    public class CouponServiceTests
    {
        private ApplicationDbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }

        private (CouponService service, ApplicationDbContext context) CreateService()
        {
            var context = CreateInMemoryDbContext();
            var auditService = new AuditService(context, NullLogger<AuditService>.Instance);
            var service = new CouponService(context, auditService, NullLogger<CouponService>.Instance);
            return (service, context);
        }

        [Fact]
        public async Task ValidateCoupon_ValidCoupon_ReturnsSuccess()
        {
            // Arrange
            var (service, context) = CreateService();
            var coupon = new Coupon
            {
                Code = "WELCOME20",
                DiscountType = DiscountType.Percentage,
                DiscountValue = 20,
                IsActive = true,
                ValidFrom = DateTime.UtcNow.AddDays(-1),
                ValidUntil = DateTime.UtcNow.AddDays(10),
                MinimumAmount = 50,
                UsageLimit = 100,
                UsedCount = 5
            };
            context.Coupons.Add(coupon);
            await context.SaveChangesAsync();

            // Act
            var result = await service.ValidateCouponAsync("WELCOME20", "user-123", 100m);

            // Assert
            Assert.True(result.IsValid);
            Assert.Equal(20m, result.DiscountAmount);
            Assert.Equal(80m, result.FinalAmount);
        }

        [Fact]
        public async Task ValidateCoupon_ExpiredCoupon_ReturnsInvalid()
        {
            // Arrange
            var (service, context) = CreateService();
            var coupon = new Coupon
            {
                Code = "EXPIRED50",
                DiscountType = DiscountType.Percentage,
                DiscountValue = 50,
                IsActive = true,
                ValidFrom = DateTime.UtcNow.AddDays(-30),
                ValidUntil = DateTime.UtcNow.AddDays(-1) // expired yesterday
            };
            context.Coupons.Add(coupon);
            await context.SaveChangesAsync();

            // Act
            var result = await service.ValidateCouponAsync("EXPIRED50", "user-123", 100m);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("expired", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0m, result.DiscountAmount);
            Assert.Equal(100m, result.FinalAmount);
        }

        [Fact]
        public async Task ValidateCoupon_InvalidCoupon_ReturnsNotFound()
        {
            // Arrange
            var (service, _) = CreateService();

            // Act
            var result = await service.ValidateCouponAsync("NONEXISTENT", "user-123", 100m);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("Invalid coupon code", result.Message);
            Assert.Equal(100m, result.FinalAmount);
        }

        [Fact]
        public async Task ValidateCoupon_UsageLimitExceeded_ReturnsInvalid()
        {
            // Arrange
            var (service, context) = CreateService();
            var coupon = new Coupon
            {
                Code = "LIMITED10",
                DiscountType = DiscountType.FixedAmount,
                DiscountValue = 10,
                IsActive = true,
                ValidFrom = DateTime.UtcNow.AddDays(-5),
                ValidUntil = DateTime.UtcNow.AddDays(5),
                UsageLimit = 3,
                UsedCount = 3 // limit reached
            };
            context.Coupons.Add(coupon);
            await context.SaveChangesAsync();

            // Act
            var result = await service.ValidateCouponAsync("LIMITED10", "user-123", 100m);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("maximum usage limit", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CalculateDiscount_PercentageDiscount_CalculatesCorrectly()
        {
            // Arrange
            var (service, _) = CreateService();
            var coupon = new Coupon
            {
                DiscountType = DiscountType.Percentage,
                DiscountValue = 25 // 25%
            };

            // Act
            var discount = service.CalculateDiscount(coupon, 200m);

            // Assert
            Assert.Equal(50m, discount); // 25% of 200 = 50
        }

        [Fact]
        public void CalculateDiscount_FixedDiscount_CalculatesCorrectly()
        {
            // Arrange
            var (service, _) = CreateService();
            var coupon = new Coupon
            {
                DiscountType = DiscountType.FixedAmount,
                DiscountValue = 35m // ₹35 flat discount
            };

            // Act
            var discount = service.CalculateDiscount(coupon, 100m);

            // Assert
            Assert.Equal(35m, discount);
        }

        [Fact]
        public void CalculateDiscount_MaximumDiscountCap_EnforcesLimit()
        {
            // Arrange
            var (service, _) = CreateService();
            var coupon = new Coupon
            {
                DiscountType = DiscountType.Percentage,
                DiscountValue = 50, // 50% of 1000 would be 500
                MaxDiscount = 150m   // Cap at 150
            };

            // Act
            var discount = service.CalculateDiscount(coupon, 1000m);

            // Assert
            Assert.Equal(150m, discount); // Capped at MaxDiscount
        }

        [Fact]
        public async Task ValidateCoupon_UserAbusePrevention_CannotReuseSingleUseCoupon()
        {
            // Arrange
            var (service, context) = CreateService();
            var coupon = new Coupon
            {
                Id = 1,
                Code = "ONEPERUSER",
                DiscountType = DiscountType.FixedAmount,
                DiscountValue = 20,
                IsActive = true,
                ValidFrom = DateTime.UtcNow.AddDays(-1),
                ValidUntil = DateTime.UtcNow.AddDays(5)
            };
            context.Coupons.Add(coupon);

            // Add previous usage by user "user-123"
            context.CouponUsages.Add(new CouponUsage
            {
                CouponId = 1,
                UserId = "user-123",
                DiscountAmount = 20,
                UsedAt = DateTime.UtcNow.AddHours(-1)
            });
            await context.SaveChangesAsync();

            // Act
            var result = await service.ValidateCouponAsync("ONEPERUSER", "user-123", 100m);

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains("already redeemed", result.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
