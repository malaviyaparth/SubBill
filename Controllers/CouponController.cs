using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SubBill.Models;
using SubBill.Services;

namespace SubBill.Controllers
{
    [Authorize]
    public class CouponController : Controller
    {
        private readonly ICouponService _couponService;
        private readonly UserManager<ApplicationUser> _userManager;

        public CouponController(ICouponService couponService, UserManager<ApplicationUser> userManager)
        {
            _couponService = couponService;
            _userManager = userManager;
        }

        // POST: /Coupon/Validate
        [HttpPost]
        public async Task<IActionResult> Validate(string code, decimal amount)
        {
            var userId = _userManager.GetUserId(User) ?? string.Empty;
            var result = await _couponService.ValidateCouponAsync(code, userId, amount);

            return Json(new
            {
                isValid = result.IsValid,
                message = result.Message,
                discount = result.DiscountAmount,
                finalAmount = result.FinalAmount,
                originalAmount = result.OriginalAmount,
                code = result.Coupon?.Code ?? code
            });
        }
    }
}
