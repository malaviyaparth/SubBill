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
    public class SubscriptionController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ISubscriptionService _subscriptionService;

        public SubscriptionController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ISubscriptionService subscriptionService)
        {
            _context = context;
            _userManager = userManager;
            _subscriptionService = subscriptionService;
        }

        // GET: /Subscription
        [AllowAnonymous]
        public async Task<IActionResult> Index()
        {
            var plans = await _context.SubscriptionPlans
                .Where(p => p.IsActive)
                .ToListAsync();

            var userId = _userManager.GetUserId(User);
            if (!string.IsNullOrEmpty(userId))
            {
                ViewBag.CurrentSubscription = await _subscriptionService.GetCurrentSubscriptionAsync(userId);
            }

            return View(plans);
        }

        // GET: /Subscription/Subscribe/5
        [Authorize(Roles = "User")]
        public async Task<IActionResult> Subscribe(int id)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(id);
            if (plan == null || !plan.IsActive) return NotFound();
            return View(plan);
        }

        // POST: /Subscription/Subscribe/5
        [HttpPost, ActionName("Subscribe")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "User")]
        public async Task<IActionResult> SubscribeConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            var plan = await _context.SubscriptionPlans.FindAsync(id);
            if (plan == null || !plan.IsActive) return NotFound();

            // Redirect to Payment Checkout for Phase 4 flow
            return RedirectToAction("Checkout", "Payment", new { planId = id });
        }

        // POST: /Subscription/DirectSubscribe (Test/Sandbox fallback to directly activate without payment)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "User")]
        public async Task<IActionResult> DirectSubscribe(int id)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            try
            {
                await _subscriptionService.SubscribeAsync(userId, id);
                TempData["Message"] = "Subscription activated successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(MySubscription));
        }

        // GET: /Subscription/MySubscription
        [Authorize(Roles = "User")]
        public async Task<IActionResult> MySubscription()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            var sub = await _subscriptionService.GetCurrentSubscriptionAsync(userId);
            return View(sub);
        }

        // GET: /Subscription/Cancel/5
        [Authorize(Roles = "User")]
        public async Task<IActionResult> Cancel(int id)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            var sub = await _context.UserSubscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

            if (sub == null) return NotFound();

            return View(sub);
        }

        // POST: /Subscription/Cancel/5
        [HttpPost, ActionName("Cancel")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "User")]
        public async Task<IActionResult> CancelConfirmed(int id, string? reason)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            try
            {
                await _subscriptionService.CancelSubscriptionAsync(userId, id, reason);
                TempData["Message"] = "Your subscription has been cancelled. You retain full access until the end of the current billing period.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(MySubscription));
        }

        // GET: /Subscription/ChangePlan
        [Authorize(Roles = "User")]
        public async Task<IActionResult> ChangePlan()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            var currentSub = await _subscriptionService.GetCurrentSubscriptionAsync(userId);
            if (currentSub == null)
            {
                TempData["Error"] = "You do not have an active subscription to upgrade or downgrade.";
                return RedirectToAction(nameof(Index));
            }

            var plans = await _context.SubscriptionPlans
                .Where(p => p.IsActive)
                .ToListAsync();

            ViewBag.CurrentSubscription = currentSub;
            return View(plans);
        }

        // POST: /Subscription/ConfirmChangePlan
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "User")]
        public async Task<IActionResult> ConfirmChangePlan(int newPlanId)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            try
            {
                var updated = await _subscriptionService.ChangeSubscriptionPlanAsync(userId, newPlanId);
                TempData["Message"] = $"Your subscription plan has been successfully changed to {updated.Plan?.Name}!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(MySubscription));
        }

        // GET: /Subscription/History
        [Authorize(Roles = "User")]
        public async Task<IActionResult> History()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            var histories = await _subscriptionService.GetUserSubscriptionHistoryAsync(userId);
            return View(histories);
        }

        // GET: /Subscription/AdminHistory
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminHistory()
        {
            var histories = await _subscriptionService.GetAllSubscriptionHistoriesAsync();
            return View(histories);
        }
    }
}
