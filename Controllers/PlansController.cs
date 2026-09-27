using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubBill.Data;
using SubBill.Models;

namespace SubBill.Controllers
{
    [Authorize]
    public class PlansController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PlansController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Plans
        public async Task<IActionResult> Index()
        {
            var plans = await _context.SubscriptionPlans.ToListAsync();

            return View(plans);
        }

        // GET: /Plans/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: /Plans/Create
        [Authorize(Roles = "Admin")]
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

            TempData["Message"] = "Plan created successfully.";

            return RedirectToAction(nameof(Index));
        }

        // GET: /Plans/Edit/3
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(id);

            if (plan == null)
                return NotFound();

            return View(plan);
        }

        // POST: /Plans/Edit/3
        [Authorize(Roles = "Admin")]
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

            TempData["Message"] = "Plan updated successfully.";

            return RedirectToAction(nameof(Index));
        }

        // GET: /Plans/Delete/3
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(id);

            if (plan == null)
                return NotFound();

            return View(plan);
        }

        // POST: /Plans/Delete/3
        [Authorize(Roles = "Admin")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(id);

            if (plan == null)
                return NotFound();

            _context.SubscriptionPlans.Remove(plan);
            await _context.SaveChangesAsync();

            TempData["Message"] = "Plan deleted.";

            return RedirectToAction(nameof(Index));
        }
    }
}