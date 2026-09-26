using SubBill.Models;

namespace SubBill.Services
{
    public class InMemorySubscriptionPlanService : ISubscriptionPlanService
    {
        private readonly List<SubscriptionPlan> _plans = new();
        private int _nextId = 1;
        private readonly object _lock = new();

        public InMemorySubscriptionPlanService()
        {
            // seed some sample data
            Create(new SubscriptionPlan { Name = "Basic", Description = "Entry-level plan", Price = 9.99m, BillingCycle = BillingCycle.Monthly });
            Create(new SubscriptionPlan { Name = "Pro", Description = "For growing teams", Price = 29.99m, BillingCycle = BillingCycle.Monthly });
            Create(new SubscriptionPlan { Name = "Enterprise", Description = "Full feature set", Price = 299.99m, BillingCycle = BillingCycle.Yearly });
        }

        public List<SubscriptionPlan> GetAll()
        {
            lock (_lock)
            {
                return _plans.OrderBy(p => p.Id).ToList();
            }
        }

        public SubscriptionPlan? GetById(int id)
        {
            lock (_lock)
            {
                return _plans.FirstOrDefault(p => p.Id == id);
            }
        }

        public SubscriptionPlan Create(SubscriptionPlan plan)
        {
            lock (_lock)
            {
                plan.Id = _nextId++;
                _plans.Add(plan);
                return plan;
            }
        }

        public bool Update(SubscriptionPlan plan)
        {
            lock (_lock)
            {
                var existing = _plans.FirstOrDefault(p => p.Id == plan.Id);
                if (existing == null) return false;

                existing.Name = plan.Name;
                existing.Description = plan.Description;
                existing.Price = plan.Price;
                existing.BillingCycle = plan.BillingCycle;
                existing.IsActive = plan.IsActive;
                return true;
            }
        }

        public bool Delete(int id)
        {
            lock (_lock)
            {
                var existing = _plans.FirstOrDefault(p => p.Id == id);
                if (existing == null) return false;
                return _plans.Remove(existing);
            }
        }
    }
}
