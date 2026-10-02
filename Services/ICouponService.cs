using SubBill.Models;

namespace SubBill.Services
{
    public interface ICouponService
    {
        Task<CouponValidationResult> ValidateCouponAsync(string code, string userId, decimal orderAmount);
        decimal CalculateDiscount(Coupon coupon, decimal orderAmount);
        Task<CouponUsage> ApplyCouponAsync(string code, string userId, int? subscriptionId, decimal orderAmount);
        Task<Coupon> CreateCouponAsync(Coupon coupon, string adminEmail);
        Task<Coupon> UpdateCouponAsync(Coupon coupon, string adminEmail);
        Task<bool> DeleteCouponAsync(int id, string adminEmail);
        Task<bool> ToggleCouponStatusAsync(int id, string adminEmail);
        Task<Coupon?> GetCouponByIdAsync(int id);
        Task<Coupon?> GetCouponByCodeAsync(string code);
        Task<List<Coupon>> GetAllCouponsAsync(int page = 1, int pageSize = 10);
        Task<int> GetTotalCouponsCountAsync();
        Task<List<CouponUsage>> GetCouponUsagesAsync(int? couponId = null, int page = 1, int pageSize = 10);
        Task<int> GetTotalCouponUsagesCountAsync(int? couponId = null);
    }

    public class CouponValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
        public Coupon? Coupon { get; set; }
        public decimal OriginalAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal FinalAmount { get; set; }
    }
}
