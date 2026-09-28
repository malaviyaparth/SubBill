using SubBill.Models;

namespace SubBill.Services
{
    public interface IPaymentService
    {
        Task<Payment> CreateOrderAsync(string userId, int planId);
        Task<PaymentResult> VerifyAndProcessPaymentAsync(string userId, string orderId, string paymentId, string signature);
        Task<List<Payment>> GetUserPaymentsAsync(string userId, PaymentStatus? status = null);
        Task<List<Payment>> GetAllPaymentsAsync(PaymentStatus? status = null);
        string GetKeyId();
    }

    public class PaymentResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public Payment? Payment { get; set; }
        public UserSubscription? Subscription { get; set; }
    }
}
