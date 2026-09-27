using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubBill.Data;
using SubBill.Models;

namespace SubBill.Controllers
{
    [Authorize(Roles = "User")]
    public class SubscriptionController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public SubscriptionController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: /Subscription
        public async Task<IActionResult> Index()
        {
            var plans = await _context.SubscriptionPlans.ToListAsync();
            return View(plans);
        }

        // GET: /Subscription/Subscribe/5
        public async Task<IActionResult> Subscribe(int id)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(id);
            if (plan == null) return NotFound();
            return View(plan);
        }

        // POST: /Subscription/Subscribe/5
        [HttpPost, ActionName("Subscribe")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubscribeConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User);

            var plan = await _context.SubscriptionPlans.FindAsync(id);
            if (plan == null) return NotFound();

            var existingActive = await _context.UserSubscriptions
                .Where(s => s.UserId == userId && s.Status == "Active")
                .ToListAsync();

            foreach (var sub in existingActive)
                sub.Status = "Cancelled";

            var newSub = new UserSubscription
            {
                UserId = userId,
                PlanId = plan.Id,
                StartDate = DateTime.UtcNow,
                ExpiryDate = plan.BillingCycle == BillingCycle.Yearly
                    ? DateTime.UtcNow.AddYears(1)
                    : DateTime.UtcNow.AddMonths(1),
                Status = "Active"
            };

            _context.UserSubscriptions.Add(newSub);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(MySubscription));
        }

        // GET: /Subscription/MySubscription
        public async Task<IActionResult> MySubscription()
        {
            var userId = _userManager.GetUserId(User);

            var sub = await _context.UserSubscriptions
                .Include(s => s.Plan)
                .Where(s => s.UserId == userId && s.Status == "Active")
                .OrderByDescending(s => s.StartDate)
                .FirstOrDefaultAsync();

            return View(sub);
        }
    }
}
