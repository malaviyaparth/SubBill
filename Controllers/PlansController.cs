using Microsoft.AspNetCore.Mvc;
using SubBill.Models;
using SubBill.Services;

namespace SubBill.Controllers
{
    public class PlansController : Controller
    {
        private readonly ISubscriptionPlanService _service;

        public PlansController(ISubscriptionPlanService service)
        {
            _service = service;
        }

        // GET: /Plans
        public IActionResult Index()
        {
            var plans = _service.GetAll();
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
        public IActionResult Create(SubscriptionPlan plan)
        {
            if (!ModelState.IsValid)
            {
                return View(plan);
            }

            _service.Create(plan);
            TempData["Message"] = "Plan created successfully.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Plans/Edit/3
        public IActionResult Edit(int id)
        {
            var plan = _service.GetById(id);
            if (plan == null) return NotFound();

            return View(plan);
        }

        // POST: /Plans/Edit/3
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, SubscriptionPlan plan)
        {
            if (id != plan.Id) return BadRequest();

            if (!ModelState.IsValid)
            {
                return View(plan);
            }

            var updated = _service.Update(plan);
            if (!updated) return NotFound();

            TempData["Message"] = "Plan updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Plans/Delete/3
        public IActionResult Delete(int id)
        {
            var plan = _service.GetById(id);
            if (plan == null) return NotFound();

            return View(plan);
        }

        // POST: /Plans/Delete/3
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int id)
        {
            _service.Delete(id);
            TempData["Message"] = "Plan deleted.";
            return RedirectToAction(nameof(Index));
        }
    }
}
