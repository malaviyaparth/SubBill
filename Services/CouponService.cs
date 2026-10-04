using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SubBill.Data;
using SubBill.Models;

namespace SubBill.Services
{
    public class CouponService : ICouponService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;
        private readonly ILogger<CouponService> _logger;

        public CouponService(ApplicationDbContext context, IAuditService auditService, ILogger<CouponService> logger)
        {
            _context = context;
            _auditService = auditService;
            _logger = logger;
        }

        public async Task<CouponValidationResult> ValidateCouponAsync(string code, string userId, decimal orderAmount)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return new CouponValidationResult
                {
                    IsValid = false,
                    Message = "Please enter a coupon code.",
                    OriginalAmount = orderAmount,
                    FinalAmount = orderAmount
                };
            }

            var cleanCode = code.Trim().ToUpperInvariant();
            var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == cleanCode);

            if (coupon == null)
            {
                return new CouponValidationResult
                {
                    IsValid = false,
                    Message = "Invalid coupon code.",
                    OriginalAmount = orderAmount,
                    FinalAmount = orderAmount
                };
            }

            if (!coupon.IsActive)
            {
                return new CouponValidationResult
                {
                    IsValid = false,
                    Message = "This coupon is currently inactive.",
                    Coupon = coupon,
                    OriginalAmount = orderAmount,
                    FinalAmount = orderAmount
                };
            }

            var now = DateTime.UtcNow;
            if (now < coupon.ValidFrom || now > coupon.ValidUntil)
            {
                return new CouponValidationResult
                {
                    IsValid = false,
                    Message = "This coupon has expired or is not yet valid.",
                    Coupon = coupon,
                    OriginalAmount = orderAmount,
                    FinalAmount = orderAmount
                };
            }

            if (coupon.UsageLimit.HasValue && coupon.UsedCount >= coupon.UsageLimit.Value)
            {
                return new CouponValidationResult
                {
                    IsValid = false,
                    Message = "This coupon has reached its maximum usage limit.",
                    Coupon = coupon,
                    OriginalAmount = orderAmount,
                    FinalAmount = orderAmount
                };
            }

            if (orderAmount < coupon.MinimumAmount)
            {
                return new CouponValidationResult
                {
                    IsValid = false,
                    Message = $"Minimum order amount of ₹{coupon.MinimumAmount:F2} is required to apply this coupon.",
                    Coupon = coupon,
                    OriginalAmount = orderAmount,
                    FinalAmount = orderAmount
                };
            }

            if (!string.IsNullOrEmpty(userId))
            {
                var alreadyUsed = await _context.CouponUsages
                    .AnyAsync(u => u.CouponId == coupon.Id && u.UserId == userId);

                if (alreadyUsed)
                {
                    return new CouponValidationResult
                    {
                        IsValid = false,
                        Message = "You have already redeemed this coupon.",
                        Coupon = coupon,
                        OriginalAmount = orderAmount,
                        FinalAmount = orderAmount
                    };
                }
            }

            var discount = CalculateDiscount(coupon, orderAmount);
            var finalAmount = Math.Max(0m, orderAmount - discount);

            return new CouponValidationResult
            {
                IsValid = true,
                Message = "Coupon applied successfully!",
                Coupon = coupon,
                OriginalAmount = orderAmount,
                DiscountAmount = discount,
                FinalAmount = finalAmount
            };
        }

        public decimal CalculateDiscount(Coupon coupon, decimal orderAmount)
        {
            if (coupon == null || orderAmount <= 0) return 0m;

            decimal discount = 0m;
            if (coupon.DiscountType == DiscountType.Percentage)
            {
                discount = Math.Round(orderAmount * (coupon.DiscountValue / 100m), 2);
                if (coupon.MaxDiscount.HasValue && coupon.MaxDiscount.Value > 0 && discount > coupon.MaxDiscount.Value)
                {
                    discount = coupon.MaxDiscount.Value;
                }
            }
            else if (coupon.DiscountType == DiscountType.FixedAmount)
            {
                discount = Math.Min(orderAmount, coupon.DiscountValue);
            }

            return Math.Min(discount, orderAmount);
        }

        public async Task<CouponUsage> ApplyCouponAsync(string code, string userId, int? subscriptionId, decimal orderAmount)
        {
            var validation = await ValidateCouponAsync(code, userId, orderAmount);
            if (!validation.IsValid || validation.Coupon == null)
            {
                throw new InvalidOperationException(validation.Message);
            }

            var coupon = validation.Coupon;
            coupon.UsedCount += 1;

            var usage = new CouponUsage
            {
                CouponId = coupon.Id,
                UserId = userId,
                SubscriptionId = subscriptionId,
                DiscountAmount = validation.DiscountAmount,
                UsedAt = DateTime.UtcNow
            };

            _context.CouponUsages.Add(usage);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Applied coupon {Code} for User {UserId}, Discount: {Discount}", code, userId, validation.DiscountAmount);
            return usage;
        }

        public async Task<Coupon> CreateCouponAsync(Coupon coupon, string adminEmail)
        {
            coupon.Code = coupon.Code.Trim().ToUpperInvariant();
            var exists = await _context.Coupons.AnyAsync(c => c.Code == coupon.Code);
            if (exists)
            {
                throw new InvalidOperationException($"A coupon with code '{coupon.Code}' already exists.");
            }

            // Coupon date validation: Cannot create coupon for yesterday or prior days (must be from today onwards in IST)
            var todayIst = DateTimeExtensions.NowIst().Date;
            var validFromIst = coupon.ValidFrom.ToIst().Date;
            if (validFromIst < todayIst)
            {
                throw new InvalidOperationException("Coupon start date (Valid From) cannot be in the past (yesterday or earlier). It must be from today onwards.");
            }

            if (coupon.ValidUntil <= coupon.ValidFrom)
            {
                throw new InvalidOperationException("Coupon expiry date (Valid Until) must be greater than the Valid From date.");
            }

            coupon.CreatedAt = DateTime.UtcNow;
            _context.Coupons.Add(coupon);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(adminEmail, "CreateCoupon", "Coupon", coupon.Id.ToString(),
                $"Created coupon '{coupon.Code}', Type: {coupon.DiscountType}, Value: {coupon.DiscountValue}");

            return coupon;
        }

        public async Task<Coupon> UpdateCouponAsync(Coupon coupon, string adminEmail)
        {
            var existing = await _context.Coupons.FindAsync(coupon.Id);
            if (existing == null)
            {
                throw new KeyNotFoundException("Coupon not found.");
            }

            var oldCode = existing.Code;
            existing.DiscountType = coupon.DiscountType;
            existing.DiscountValue = coupon.DiscountValue;
            existing.MaxDiscount = coupon.MaxDiscount;
            existing.MinimumAmount = coupon.MinimumAmount;
            existing.UsageLimit = coupon.UsageLimit;
            existing.ValidFrom = coupon.ValidFrom;
            existing.ValidUntil = coupon.ValidUntil;
            existing.IsActive = coupon.IsActive;

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(adminEmail, "UpdateCoupon", "Coupon", existing.Id.ToString(),
                $"Updated coupon '{oldCode}'. Type: {existing.DiscountType}, Value: {existing.DiscountValue}, Active: {existing.IsActive}");

            return existing;
        }

        public async Task<bool> DeleteCouponAsync(int id, string adminEmail)
        {
            var coupon = await _context.Coupons.FindAsync(id);
            if (coupon == null) return false;

            var code = coupon.Code;
            _context.Coupons.Remove(coupon);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(adminEmail, "DeleteCoupon", "Coupon", id.ToString(),
                $"Deleted coupon '{code}'");

            return true;
        }

        public async Task<bool> ToggleCouponStatusAsync(int id, string adminEmail)
        {
            var coupon = await _context.Coupons.FindAsync(id);
            if (coupon == null) return false;

            coupon.IsActive = !coupon.IsActive;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(adminEmail, coupon.IsActive ? "ActivateCoupon" : "DeactivateCoupon", "Coupon", id.ToString(),
                $"Toggled coupon '{coupon.Code}' status to {(coupon.IsActive ? "Active" : "Inactive")}");

            return coupon.IsActive;
        }

        public async Task<Coupon?> GetCouponByIdAsync(int id)
        {
            return await _context.Coupons.FindAsync(id);
        }

        public async Task<Coupon?> GetCouponByCodeAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            var cleanCode = code.Trim().ToUpperInvariant();
            return await _context.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == cleanCode);
        }

        public async Task<List<Coupon>> GetAllCouponsAsync(int page = 1, int pageSize = 10)
        {
            page = Math.Max(1, page);
            return await _context.Coupons
                .OrderByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> GetTotalCouponsCountAsync()
        {
            return await _context.Coupons.CountAsync();
        }

        public async Task<List<CouponUsage>> GetCouponUsagesAsync(int? couponId = null, int page = 1, int pageSize = 10)
        {
            page = Math.Max(1, page);
            var query = _context.CouponUsages
                .Include(u => u.Coupon)
                .Include(u => u.User)
                .Include(u => u.Subscription)
                    .ThenInclude(s => s!.Plan)
                .AsQueryable();

            if (couponId.HasValue)
            {
                query = query.Where(u => u.CouponId == couponId.Value);
            }

            return await query
                .OrderByDescending(u => u.UsedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> GetTotalCouponUsagesCountAsync(int? couponId = null)
        {
            var query = _context.CouponUsages.AsQueryable();
            if (couponId.HasValue)
            {
                query = query.Where(u => u.CouponId == couponId.Value);
            }
            return await query.CountAsync();
        }
    }
}
