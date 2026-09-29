using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SubBill.Data;
using SubBill.Models;

namespace SubBill.Services
{
    public class AuditService : IAuditService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AuditService> _logger;

        public AuditService(ApplicationDbContext context, ILogger<AuditService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task LogAsync(string adminEmail, string action, string entityType, string? entityId, string? details, string? ipAddress = null)
        {
            try
            {
                var auditLog = new AuditLog
                {
                    AdminEmail = adminEmail,
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId,
                    Details = details,
                    IpAddress = ipAddress,
                    Timestamp = DateTime.UtcNow
                };

                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Admin Audit: [{Action}] on [{EntityType}:{EntityId}] by {AdminEmail}. Details: {Details}",
                    action, entityType, entityId, adminEmail, details);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist audit log for action {Action} by {AdminEmail}", action, adminEmail);
            }
        }

        public async Task<List<AuditLog>> GetAuditLogsAsync(int page = 1, int pageSize = 20)
        {
            page = Math.Max(1, page);
            return await _context.AuditLogs
                .OrderByDescending(a => a.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<int> GetTotalCountAsync()
        {
            return await _context.AuditLogs.CountAsync();
        }
    }
}
