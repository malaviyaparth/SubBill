using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SubBill.Data;
using SubBill.Models;

namespace SubBill.Services
{
    public class PaymentService : IPaymentService
    {
        private readonly ApplicationDbContext _context;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IInvoiceService _invoiceService;
        private readonly ICouponService _couponService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PaymentService> _logger;

        public PaymentService(
            ApplicationDbContext context,
            ISubscriptionService subscriptionService,
            IInvoiceService invoiceService,
            ICouponService couponService,
            IConfiguration configuration,
            ILogger<PaymentService> logger)
        {
            _context = context;
            _subscriptionService = subscriptionService;
            _invoiceService = invoiceService;
            _couponService = couponService;
            _configuration = configuration;
            _logger = logger;
        }

        public string GetKeyId()
        {
            return _configuration["Razorpay:KeyId"] ?? "rzp_test_subbill_sandbox_key";
        }

        private string GetKeySecret()
        {
            return _configuration["Razorpay:KeySecret"] ?? "subbill_test_secret_key_12345";
        }

        public async Task<Payment> CreateOrderAsync(string userId, int planId, string? couponCode = null)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(planId);
            if (plan == null || !plan.IsActive)
            {
                throw new InvalidOperationException("Invalid or inactive plan.");
            }

            decimal payableAmount = plan.Price;
            if (!string.IsNullOrWhiteSpace(couponCode))
            {
                var validation = await _couponService.ValidateCouponAsync(couponCode, userId, plan.Price);
                if (validation.IsValid)
                {
                    payableAmount = validation.FinalAmount;
                    _logger.LogInformation("Applied coupon {Code} to Order. Original: {Orig}, Discount: {Disc}, Final: {Final}",
                        couponCode, plan.Price, validation.DiscountAmount, payableAmount);
                }
            }

            var orderId = $"order_plan_{planId}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Random.Shared.Next(1000, 9999)}";

            var payment = new Payment
            {
                UserId = userId,
                Amount = payableAmount,
                Currency = "INR",
                PaymentGateway = "Razorpay (Test Sandbox)",
                OrderId = orderId,
                Status = PaymentStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created test payment order {OrderId} for User {UserId}, Plan {PlanId}, Amount {Amount}", orderId, userId, planId, payableAmount);
            return payment;
        }

        public async Task<PaymentResult> VerifyAndProcessPaymentAsync(string userId, string orderId, string paymentId, string signature, string? couponCode = null, int? planId = null)
        {
            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.OrderId == orderId && p.UserId == userId);

            if (payment == null)
            {
                return new PaymentResult { Success = false, Message = "Order not found." };
            }

            // Idempotency: If payment was already completed successfully, return without duplicating
            if (payment.Status == PaymentStatus.Success)
            {
                var existingSub = payment.SubscriptionId.HasValue 
                    ? await _context.UserSubscriptions.FindAsync(payment.SubscriptionId.Value) 
                    : null;
                return new PaymentResult
                {
                    Success = true,
                    Message = "Payment already processed.",
                    Payment = payment,
                    Subscription = existingSub
                };
            }

            // Signature verification
            bool isValidSignature = VerifySignature(orderId, paymentId, signature, GetKeySecret());
            if (!isValidSignature)
            {
                payment.Status = PaymentStatus.Failed;
                payment.PaymentId = paymentId;
                payment.Signature = signature;
                await _context.SaveChangesAsync();

                _logger.LogWarning("Payment verification failed for Order {OrderId}. Invalid signature.", orderId);
                return new PaymentResult { Success = false, Message = "Payment verification failed: Invalid signature.", Payment = payment };
            }

            // Mark payment success
            payment.Status = PaymentStatus.Success;
            payment.PaymentId = paymentId;
            payment.Signature = signature;
            payment.PaymentDate = DateTime.UtcNow;

            // Resolve plan strictly by PlanId (NOT by discounted payment amount, avoiding wrong plan assignment when coupon is applied)
            SubscriptionPlan? plan = null;

            // 1. Try to extract planId from OrderId (order_plan_{planId}_...)
            if (payment.OrderId.StartsWith("order_plan_"))
            {
                var parts = payment.OrderId.Split('_');
                if (parts.Length >= 3 && int.TryParse(parts[2], out int extractedPlanId))
                {
                    plan = await _context.SubscriptionPlans.FindAsync(extractedPlanId);
                }
            }

            // 2. If not found, use explicitly passed planId
            if (plan == null && planId.HasValue && planId.Value > 0)
            {
                plan = await _context.SubscriptionPlans.FindAsync(planId.Value);
            }

            // 3. Fallback: match by price or first plan
            if (plan == null)
            {
                plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Price == payment.Amount && p.IsActive)
                           ?? await _context.SubscriptionPlans.FirstOrDefaultAsync();
            }

            if (plan == null)
            {
                await _context.SaveChangesAsync();
                return new PaymentResult { Success = false, Message = "Unable to resolve active plan for payment.", Payment = payment };
            }

            // Activate subscription
            var subscription = await _subscriptionService.SubscribeAsync(userId, plan.Id);
            payment.SubscriptionId = subscription.Id;
            await _context.SaveChangesAsync();

            // Record coupon usage if a coupon was applied
            if (!string.IsNullOrWhiteSpace(couponCode))
            {
                try
                {
                    await _couponService.ApplyCouponAsync(couponCode, userId, subscription.Id, plan.Price);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to record coupon usage for coupon {CouponCode}", couponCode);
                }
            }

            // Automatically generate Invoice for successful payment (Phase 5 requirement)
            try
            {
                await _invoiceService.CreateInvoiceForPaymentAsync(payment, subscription);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to automatically generate invoice for Payment {PaymentId}", payment.Id);
            }

            _logger.LogInformation("Successfully verified and processed payment for Order {OrderId}, User {UserId}", orderId, userId);

            return new PaymentResult
            {
                Success = true,
                Message = "Payment verified and subscription activated successfully!",
                Payment = payment,
                Subscription = subscription
            };
        }

        public async Task<List<Payment>> GetUserPaymentsAsync(string userId, PaymentStatus? status = null)
        {
            var query = _context.Payments
                .Include(p => p.Subscription)
                    .ThenInclude(s => s!.Plan)
                .Where(p => p.UserId == userId);

            if (status.HasValue)
            {
                query = query.Where(p => p.Status == status.Value);
            }

            return await query.OrderByDescending(p => p.CreatedAt).ToListAsync();
        }

        public async Task<List<Payment>> GetAllPaymentsAsync(PaymentStatus? status = null)
        {
            var query = _context.Payments
                .Include(p => p.User)
                .Include(p => p.Subscription)
                    .ThenInclude(s => s!.Plan)
                .AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(p => p.Status == status.Value);
            }

            return await query.OrderByDescending(p => p.CreatedAt).ToListAsync();
        }

        private bool VerifySignature(string orderId, string paymentId, string signature, string secret)
        {
            if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(paymentId) || string.IsNullOrWhiteSpace(signature))
            {
                return false;
            }

            // In sandbox test mode, allow predefined test token or HMAC-SHA256 signature
            if (signature == "test_signature_valid" || signature == "sandbox_bypass_signature")
            {
                return true;
            }

            try
            {
                var payload = $"{orderId}|{paymentId}";
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var computedSignature = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                return string.Equals(computedSignature, signature.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
