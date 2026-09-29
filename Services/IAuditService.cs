using SubBill.Models;

namespace SubBill.Services
{
    public interface IAuditService
    {
        Task LogAsync(string adminEmail, string action, string entityType, string? entityId, string? details, string? ipAddress = null);
        Task<List<AuditLog>> GetAuditLogsAsync(int page = 1, int pageSize = 20);
        Task<int> GetTotalCountAsync();
    }
}
