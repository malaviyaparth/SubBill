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
        private readonly IAuditService _auditService;

        public PaymentController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IPaymentService paymentService,
            IAuditService auditService)
        {
            _context = context;
            _userManager = userManager;
            _paymentService = paymentService;
            _auditService = auditService;
        }

        // GET: /Payment/Checkout?planId=2&couponCode=SAVE20
        [Authorize(Roles = "User")]
        public async Task<IActionResult> Checkout(int planId, string? couponCode = null)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            var plan = await _context.SubscriptionPlans.FindAsync(planId);
            if (plan == null || !plan.IsActive) return NotFound();

            var payment = await _paymentService.CreateOrderAsync(userId, planId, couponCode);

            ViewBag.Plan = plan;
            ViewBag.RazorpayKey = _paymentService.GetKeyId();
            ViewBag.CouponCode = couponCode;
            return View(payment);
        }

        // POST: /Payment/ProcessPayment
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "User")]
        public async Task<IActionResult> ProcessPayment(string orderId, string paymentId, string signature, string? couponCode = null, int? planId = null)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            var result = await _paymentService.VerifyAndProcessPaymentAsync(userId, orderId, paymentId, signature, couponCode, planId);
            if (result.Success)
            {
                // Audit Log: User payment checkout subscription activation
                var userEmail = User.Identity?.Name ?? userId;
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                await _auditService.LogAsync(userEmail, "SubscribeWithPayment", "Payment", paymentId,
                    $"Payment successful ({paymentId}) for Order {orderId}. Subscription activated.", ip);

                TempData["Message"] = result.Message;
                return RedirectToAction("MySubscription", "Subscription");
            }

            TempData["Error"] = result.Message;
            return RedirectToAction("History");
        }

        // GET: /Payment/History
        [Authorize(Roles = "User")]
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
