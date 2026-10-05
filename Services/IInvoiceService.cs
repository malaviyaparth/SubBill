using SubBill.Models;

namespace SubBill.Services
{
    public interface IInvoiceService
    {
        Task<Invoice> CreateInvoiceForPaymentAsync(Payment payment, UserSubscription subscription, string? couponCode = null);
        Task<Invoice?> GetInvoiceByIdAsync(int id);
        Task<List<Invoice>> GetUserInvoicesAsync(string userId);
        Task<List<Invoice>> GetAllInvoicesAsync(string? searchTerm = null, InvoiceStatus? status = null);
        byte[] GenerateInvoicePdf(Invoice invoice);
    }
}
