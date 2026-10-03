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
                .Include(p => p.Features)
                .Where(p => p.IsActive)
                .OrderBy(p => p.Price)
                .ToListAsync();

            var userId = _userManager.GetUserId(User);
            if (!string.IsNullOrEmpty(userId))
            {
                ViewBag.CurrentSubscription = await _subscriptionService.GetCurrentSubscriptionAsync(userId);

                var trialEligibility = new Dictionary<int, bool>();
                foreach (var plan in plans)
                {
                    trialEligibility[plan.Id] = await _subscriptionService.CanUserTakeTrialAsync(userId, plan.Id);
                }
                ViewBag.TrialEligibility = trialEligibility;
            }

            return View(plans);
        }

        // GET: /Subscription/Subscribe/5
        [Authorize]
        public async Task<IActionResult> Subscribe(int id)
        {
            var plan = await _context.SubscriptionPlans
                .Include(p => p.Features)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (plan == null || !plan.IsActive) return NotFound();

            var userId = _userManager.GetUserId(User);
            if (!string.IsNullOrEmpty(userId))
            {
                ViewBag.CanTakeTrial = await _subscriptionService.CanUserTakeTrialAsync(userId, id);
            }

            return View(plan);
        }

        // GET & POST: /Subscription/StartTrial/5 (Phase 10 Free Trial)
        [AcceptVerbs("GET", "POST")]
        [Authorize]
        public async Task<IActionResult> StartTrial(int id)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            try
            {
                var trialSub = await _subscriptionService.StartFreeTrialAsync(userId, id);
                TempData["Message"] = $"Your {trialSub.Plan?.Name} free trial is now active! Enjoy full access for {trialSub.Plan?.TrialDays} days.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(MySubscription));
        }

        // POST: /Subscription/Subscribe/5
        [HttpPost, ActionName("Subscribe")]
        [ValidateAntiForgeryToken]
        [Authorize]
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
        [Authorize]
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
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> MySubscription(int paymentsPage = 1, int invoicesPage = 1, int historyPage = 1, string tab = "overview")
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            var currentSub = await _subscriptionService.GetCurrentSubscriptionAsync(userId);
            var availablePlans = await _context.SubscriptionPlans
                .Where(p => p.IsActive)
                .OrderBy(p => p.Price)
                .ToListAsync();

            const int pageSize = 5;
            paymentsPage = Math.Max(1, paymentsPage);
            invoicesPage = Math.Max(1, invoicesPage);
            historyPage = Math.Max(1, historyPage);

            // Usage & Subscription Summary
            var summary = new BillingUsageSummary();
            if (currentSub != null)
            {
                var totalPeriodTime = (currentSub.CurrentPeriodEnd - currentSub.CurrentPeriodStart).TotalDays;
                var elapsedPeriodTime = (DateTime.UtcNow - currentSub.CurrentPeriodStart).TotalDays;
                var remainingDays = (currentSub.CurrentPeriodEnd - DateTime.UtcNow).TotalDays;

                summary.TotalPeriodDays = Math.Max(1, (int)Math.Ceiling(totalPeriodTime));
                summary.DaysRemainingInPeriod = Math.Max(0, (int)Math.Ceiling(remainingDays));
                summary.PeriodProgressPercent = Math.Clamp((int)((elapsedPeriodTime / Math.Max(1, totalPeriodTime)) * 100), 0, 100);
                summary.DaysActive = Math.Max(0, (int)(DateTime.UtcNow - currentSub.StartDate).TotalDays);
                summary.MemberSinceFormatted = currentSub.StartDate.ToString("dd MMM yyyy");
            }

            // Lifetime & Aggregates for user
            summary.CompletedPaymentsCount = await _context.Payments
                .CountAsync(p => p.UserId == userId && p.Status == PaymentStatus.Success);

            summary.TotalSpent = await _context.Payments
                .Where(p => p.UserId == userId && p.Status == PaymentStatus.Success)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;

            summary.TotalInvoicesCount = await _context.Invoices
                .CountAsync(i => i.UserId == userId);

            summary.PlanChangesCount = await _context.SubscriptionHistories
                .CountAsync(h => h.Subscription != null && h.Subscription.UserId == userId);

            // Paginated Payments with associated invoice linking
            var paymentsQuery = _context.Payments
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.CreatedAt);

            var totalPaymentsCount = await paymentsQuery.CountAsync();
            var paymentEntities = await paymentsQuery
                .Skip((paymentsPage - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var paymentIds = paymentEntities.Select(p => p.Id).ToList();
            var invoiceList = await _context.Invoices
                .Where(i => i.PaymentId.HasValue && paymentIds.Contains(i.PaymentId.Value))
                .ToListAsync();
            var invoicesForPayments = invoiceList
                .GroupBy(i => i.PaymentId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var paymentItems = paymentEntities.Select(p => new PaymentHistoryItemViewModel
            {
                Id = p.Id,
                Date = p.PaymentDate ?? p.CreatedAt,
                Amount = p.Amount,
                Currency = p.Currency,
                Status = p.Status,
                OrderId = p.OrderId,
                PaymentId = p.PaymentId,
                InvoiceId = invoicesForPayments.TryGetValue(p.Id, out var inv) ? inv.Id : null,
                InvoiceNumber = inv?.InvoiceNumber
            }).ToList();

            var pagedPayments = new PaginatedList<PaymentHistoryItemViewModel>(paymentItems, totalPaymentsCount, paymentsPage, pageSize);

            // Paginated Invoices
            var invoicesQuery = _context.Invoices
                .Include(i => i.Subscription)
                    .ThenInclude(s => s!.Plan)
                .Where(i => i.UserId == userId)
                .OrderByDescending(i => i.InvoiceDate);

            var totalInvoicesCount = await invoicesQuery.CountAsync();
            var invoiceItems = await invoicesQuery
                .Skip((invoicesPage - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var pagedInvoices = new PaginatedList<Invoice>(invoiceItems, totalInvoicesCount, invoicesPage, pageSize);

            // Paginated Subscription Transitions
            var historiesQuery = _context.SubscriptionHistories
                .Include(h => h.OldPlan)
                .Include(h => h.NewPlan)
                .Where(h => h.Subscription != null && h.Subscription.UserId == userId)
                .OrderByDescending(h => h.ChangedAt);

            var totalHistoriesCount = await historiesQuery.CountAsync();
            var historyItems = await historiesQuery
                .Skip((historyPage - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var pagedHistories = new PaginatedList<SubscriptionHistory>(historyItems, totalHistoriesCount, historyPage, pageSize);

            var viewModel = new UserBillingDashboardViewModel
            {
                CurrentSubscription = currentSub,
                AvailablePlans = availablePlans,
                Summary = summary,
                Payments = pagedPayments,
                Invoices = pagedInvoices,
                Histories = pagedHistories,
                ActiveTab = tab
            };

            return View(viewModel);
        }

        // GET: /Subscription/Dashboard
        [Authorize]
        public async Task<IActionResult> Dashboard(int paymentsPage = 1, int invoicesPage = 1, int historyPage = 1, string tab = "overview")
        {
            return await MySubscription(paymentsPage, invoicesPage, historyPage, tab);
        }

        // POST: /Subscription/ToggleAutoRenew
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> ToggleAutoRenew(int id, bool enable)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            try
            {
                await _subscriptionService.ToggleAutoRenewAsync(userId, id, enable);
                TempData["Message"] = enable ? "Auto-renew has been successfully enabled." : "Auto-renew has been disabled.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(MySubscription));
        }

        // POST: /Subscription/Renew
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Renew(int id)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            try
            {
                await _subscriptionService.RenewUserSubscriptionAsync(userId, id);
                TempData["Message"] = "Your subscription has been successfully renewed!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(MySubscription));
        }

        // GET: /Subscription/Cancel/5
        [Authorize]
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
        [Authorize]
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
        [Authorize]
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
                .Include(p => p.Features)
                .Where(p => p.IsActive)
                .OrderBy(p => p.Price)
                .ToListAsync();

            ViewBag.CurrentSubscription = currentSub;
            return View(plans);
        }

        // GET or POST: /Subscription/Upgrade
        [AcceptVerbs("GET", "POST")]
        [Authorize]
        public IActionResult Upgrade(int newPlanId)
        {
            // Upgrading plan requires payment checkout first!
            return RedirectToAction("Checkout", "Payment", new { planId = newPlanId, isUpgrade = true });
        }

        // POST: /Subscription/Downgrade
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Downgrade(int newPlanId)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            try
            {
                var updated = await _subscriptionService.DowngradeSubscriptionAsync(userId, newPlanId);
                TempData["Message"] = $"Your subscription plan has been changed to {updated.Plan?.Name}.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(MySubscription));
        }

        // POST: /Subscription/ConfirmChangePlan
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> ConfirmChangePlan(int newPlanId)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            var currentSub = await _subscriptionService.GetCurrentSubscriptionAsync(userId);
            var newPlan = await _context.SubscriptionPlans.FindAsync(newPlanId);

            if (newPlan == null)
            {
                TempData["Error"] = "Target subscription plan not found.";
                return RedirectToAction(nameof(ChangePlan));
            }

            // If it's an upgrade (new plan price > current plan price), ask for payment via checkout!
            if (currentSub?.Plan == null || newPlan.Price > currentSub.Plan.Price)
            {
                return RedirectToAction("Checkout", "Payment", new { planId = newPlanId, isUpgrade = true });
            }

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
        [Authorize]
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
