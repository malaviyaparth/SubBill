using SubBill.Models;

namespace SubBill.Services
{
    public interface ISubscriptionPlanService
    {
        List<SubscriptionPlan> GetAll();
        SubscriptionPlan? GetById(int id);
        SubscriptionPlan Create(SubscriptionPlan plan);
        bool Update(SubscriptionPlan plan);
        bool Delete(int id);
    }
}
