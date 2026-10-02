using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubBill.Data;
using SubBill.Models;

namespace SubBill.Controllers
{
    [Authorize(Roles = "Admin")]
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
            var plans = await _context.SubscriptionPlans
                .Include(p => p.Features)
                .OrderBy(p => p.Price)
                .ToListAsync();

            return View(plans);
        }

        // GET: /Plans/Create
        public IActionResult Create()
        {
            return View(new SubscriptionPlan { TrialDays = 0 });
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

            TempData["Message"] = $"Plan '{plan.Name}' created successfully. You can now configure its features below.";

            return RedirectToAction(nameof(Features), new { id = plan.Id });
        }

        // GET: /Plans/Edit/3
        public async Task<IActionResult> Edit(int id)
        {
            var plan = await _context.SubscriptionPlans
                .Include(p => p.Features)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (plan == null)
                return NotFound();

            return View(plan);
        }

        // POST: /Plans/Edit/3
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, SubscriptionPlan plan)
        {
            if (id != plan.Id)
                return BadRequest();

            if (!ModelState.IsValid)
            {
                return View(plan);
            }

            var existingPlan = await _context.SubscriptionPlans.FindAsync(id);

            if (existingPlan == null)
                return NotFound();

            existingPlan.Name = plan.Name;
            existingPlan.Description = plan.Description;
            existingPlan.Price = plan.Price;
            existingPlan.BillingCycle = plan.BillingCycle;
            existingPlan.TrialDays = plan.TrialDays;
            existingPlan.IsActive = plan.IsActive;

            await _context.SaveChangesAsync();

            TempData["Message"] = $"Plan '{existingPlan.Name}' updated successfully.";

            return RedirectToAction(nameof(Index));
        }

        // GET: /Plans/Delete/3
        public async Task<IActionResult> Delete(int id)
        {
            var plan = await _context.SubscriptionPlans
                .Include(p => p.Features)
                .FirstOrDefaultAsync(p => p.Id == id);

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

            _context.SubscriptionPlans.Remove(plan);
            await _context.SaveChangesAsync();

            TempData["Message"] = $"Plan '{plan.Name}' deleted.";

            return RedirectToAction(nameof(Index));
        }

        // ==========================================
        // PHASE 2: PLAN FEATURES MANAGEMENT
        // ==========================================

        // GET: /Plans/Features/3
        public async Task<IActionResult> Features(int id)
        {
            var plan = await _context.SubscriptionPlans
                .Include(p => p.Features.OrderBy(f => f.FeatureName))
                .FirstOrDefaultAsync(p => p.Id == id);

            if (plan == null)
                return NotFound();

            return View(plan);
        }

        // POST: /Plans/AddFeature
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddFeature(int planId, string featureName, string featureValue)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(planId);
            if (plan == null)
            {
                TempData["Error"] = "Plan not found.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(featureName))
            {
                TempData["Error"] = "Feature name is required.";
                return RedirectToAction(nameof(Features), new { id = planId });
            }

            featureName = featureName.Trim();
            featureValue = string.IsNullOrWhiteSpace(featureValue) ? "Yes" : featureValue.Trim();

            // Validate no duplicate feature name within the same plan
            var exists = await _context.PlanFeatures
                .AnyAsync(f => f.PlanId == planId && f.FeatureName.ToLower() == featureName.ToLower());

            if (exists)
            {
                TempData["Error"] = $"A feature with the name '{featureName}' already exists in this plan.";
                return RedirectToAction(nameof(Features), new { id = planId });
            }

            var feature = new PlanFeature
            {
                PlanId = planId,
                FeatureName = featureName,
                FeatureValue = featureValue,
                CreatedAt = DateTime.UtcNow
            };

            _context.PlanFeatures.Add(feature);
            await _context.SaveChangesAsync();

            TempData["Message"] = $"Feature '{featureName}' added successfully.";
            return RedirectToAction(nameof(Features), new { id = planId });
        }

        // POST: /Plans/EditFeature
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditFeature(int id, string featureName, string featureValue)
        {
            var feature = await _context.PlanFeatures.FindAsync(id);
            if (feature == null)
            {
                TempData["Error"] = "Feature not found.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(featureName))
            {
                TempData["Error"] = "Feature name cannot be empty.";
                return RedirectToAction(nameof(Features), new { id = feature.PlanId });
            }

            featureName = featureName.Trim();
            featureValue = string.IsNullOrWhiteSpace(featureValue) ? "Yes" : featureValue.Trim();

            // Check for duplicates excluding this feature
            var isDuplicate = await _context.PlanFeatures
                .AnyAsync(f => f.PlanId == feature.PlanId && f.Id != id && f.FeatureName.ToLower() == featureName.ToLower());

            if (isDuplicate)
            {
                TempData["Error"] = $"Another feature named '{featureName}' already exists in this plan.";
                return RedirectToAction(nameof(Features), new { id = feature.PlanId });
            }

            feature.FeatureName = featureName;
            feature.FeatureValue = featureValue;
            await _context.SaveChangesAsync();

            TempData["Message"] = $"Feature '{featureName}' updated successfully.";
            return RedirectToAction(nameof(Features), new { id = feature.PlanId });
        }

        // POST: /Plans/DeleteFeature
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFeature(int id)
        {
            var feature = await _context.PlanFeatures.FindAsync(id);
            if (feature == null)
            {
                TempData["Error"] = "Feature not found.";
                return RedirectToAction(nameof(Index));
            }

            var planId = feature.PlanId;
            var featureName = feature.FeatureName;

            _context.PlanFeatures.Remove(feature);
            await _context.SaveChangesAsync();

            TempData["Message"] = $"Feature '{featureName}' was removed.";
            return RedirectToAction(nameof(Features), new { id = planId });
        }
    }
}