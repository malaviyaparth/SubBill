using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubBill.Data;
using SubBill.Models;
using SubBill.Services;

namespace SubBill.Controllers
{
    [Authorize(Roles = "Admin")]
    public class PlansController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;

        public PlansController(ApplicationDbContext context, IAuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        // GET: /Plans
        public async Task<IActionResult> Index()
        {
            var plans = await _context.SubscriptionPlans.ToListAsync();

            return View(plans);
        }

        // GET: /Plans/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: /Plans/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SubscriptionPlan plan)
        {
            if (!ModelState.IsValid)
            {
                return View(plan);
            }

            _context.SubscriptionPlans.Add(plan);
            await _context.SaveChangesAsync();

            // Audit Log: Plan Creation by Admin
            var adminEmail = User.Identity?.Name ?? "Admin";
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _auditService.LogAsync(adminEmail, "CreatePlan", "SubscriptionPlan", plan.Id.ToString(),
                $"Created plan '{plan.Name}' (Price: ₹{plan.Price:F2}, Duration: {plan.BillingCycle}, Active: {plan.IsActive})", ip);

            TempData["Message"] = "Plan created successfully.";

            return RedirectToAction(nameof(Index));
        }

        // GET: /Plans/Edit/3
        public async Task<IActionResult> Edit(int id)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(id);

            if (plan == null)
                return NotFound();

            return View(plan);
        }

        // POST: /Plans/Edit/3
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            SubscriptionPlan plan)
        {
            if (id != plan.Id)
                return BadRequest();

            if (!ModelState.IsValid)
            {
                return View(plan);
            }

            var existingPlan =
                await _context.SubscriptionPlans.FindAsync(id);

            if (existingPlan == null)
                return NotFound();

            existingPlan.Name = plan.Name;
            existingPlan.Description = plan.Description;
            existingPlan.Price = plan.Price;
            existingPlan.BillingCycle = plan.BillingCycle;
            existingPlan.IsActive = plan.IsActive;

            await _context.SaveChangesAsync();

            // Audit Log: Plan Modification by Admin
            var adminEmail = User.Identity?.Name ?? "Admin";
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _auditService.LogAsync(adminEmail, "UpdatePlan", "SubscriptionPlan", existingPlan.Id.ToString(),
                $"Updated plan '{existingPlan.Name}' (Price: ₹{existingPlan.Price:F2}, Duration: {existingPlan.BillingCycle}, Active: {existingPlan.IsActive})", ip);

            TempData["Message"] = "Plan updated successfully.";

            return RedirectToAction(nameof(Index));
        }

        // GET: /Plans/Delete/3
        public async Task<IActionResult> Delete(int id)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(id);

            if (plan == null)
                return NotFound();

            return View(plan);
        }

        // POST: /Plans/Delete/3
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(id);

            if (plan == null)
                return NotFound();

            var planName = plan.Name;
            _context.SubscriptionPlans.Remove(plan);
            await _context.SaveChangesAsync();

            // Audit Log: Plan Deletion by Admin
            var adminEmail = User.Identity?.Name ?? "Admin";
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            await _auditService.LogAsync(adminEmail, "DeletePlan", "SubscriptionPlan", id.ToString(),
                $"Deleted plan '{planName}' (ID: #{id})", ip);

            TempData["Message"] = "Plan deleted.";

            return RedirectToAction(nameof(Index));
        }
    }
}