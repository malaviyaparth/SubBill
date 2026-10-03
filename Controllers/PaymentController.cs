using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubBill.Data;
using SubBill.Models;
using SubBill.Services;

namespace SubBill.Controllers
{
    [Authorize]
    public class PaymentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IPaymentService _paymentService;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IPaymentService paymentService,
            ILogger<PaymentController> logger)
        {
            _context = context;
            _userManager = userManager;
            _paymentService = paymentService;
            _logger = logger;
        }

        // GET: /Payment/Checkout?planId=2&couponCode=SAVE20&isUpgrade=true
        [Authorize]
        public async Task<IActionResult> Checkout(int planId, string? couponCode = null, bool isUpgrade = false)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            var plan = await _context.SubscriptionPlans.FindAsync(planId);
            if (plan == null || !plan.IsActive) return NotFound();

            var payment = await _paymentService.CreateOrderAsync(userId, planId, couponCode);

            ViewBag.Plan = plan;
            ViewBag.RazorpayKey = _paymentService.GetKeyId();
            ViewBag.CouponCode = couponCode;
            ViewBag.IsUpgrade = isUpgrade;
            return View(payment);
        }

        // POST: /Payment/ProcessPayment
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> ProcessPayment(
            string orderId,
            string paymentId,
            string signature,
            int planId,
            string? couponCode = null,
            bool isUpgrade = false,
            bool simulateFailure = false)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            try
            {
                var result = await _paymentService.VerifyAndProcessPaymentAsync(
                    userId, orderId, paymentId, signature, couponCode, planId, isUpgrade, simulateFailure);

                if (result.Success)
                {
                    TempData["Message"] = result.Message;
                    return RedirectToAction("MySubscription", "Subscription");
                }

                TempData["Error"] = result.Message;
                return RedirectToAction("History");
            }
            catch (Exception ex)
            {
                var detailedMsg = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
                _logger.LogError(ex, "Exception during payment verification for Order {OrderId}, User {UserId}", orderId, userId);
                TempData["Error"] = $"Payment processing notice: {detailedMsg}";
                return RedirectToAction("MySubscription", "Subscription");
            }
        }

        // GET: /Payment/History
        [Authorize]
        public async Task<IActionResult> History(PaymentStatus? status)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            var payments = await _paymentService.GetUserPaymentsAsync(userId, status);
            ViewBag.CurrentStatus = status;
            return View(payments);
        }

        // GET: /Payment/AdminPayments
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminPayments(PaymentStatus? status)
        {
            var payments = await _paymentService.GetAllPaymentsAsync(status);
            ViewBag.CurrentStatus = status;
            return View(payments);
        }
    }
}
