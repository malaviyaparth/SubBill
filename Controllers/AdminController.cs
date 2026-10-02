using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SubBill.Data;
using SubBill.Models;
using SubBill.Services;

namespace SubBill.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditService _auditService;
        private readonly ICouponService _couponService;
        private readonly IInvoiceService _invoiceService;

        public AdminController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IAuditService auditService,
            ICouponService couponService,
            IInvoiceService invoiceService)
        {
            _context = context;
            _userManager = userManager;
            _auditService = auditService;
            _couponService = couponService;
            _invoiceService = invoiceService;
        }

        // GET: /Admin or /Admin/Index (Dashboard)
        public async Task<IActionResult> Index(string range = "30days")
        {
            var now = DateTime.UtcNow;
            DateTime startDate;

            switch (range?.ToLower())
            {
                case "7days":
                    startDate = now.AddDays(-7);
                    break;
                case "3months":
                    startDate = now.AddMonths(-3);
                    break;
                case "6months":
                    startDate = now.AddMonths(-6);
                    break;
                case "12months":
                    startDate = now.AddYears(-1);
                    break;
                case "30days":
                default:
                    range = "30days";
                    startDate = now.AddDays(-30);
                    break;
            }

            var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var totalUsers = await _userManager.Users.CountAsync();
            var activeSubscriptions = await _context.UserSubscriptions.CountAsync(s => s.Status == SubscriptionStatus.Active);
            var trialingSubscriptions = await _context.UserSubscriptions.CountAsync(s => s.Status == SubscriptionStatus.Trialing);
            var cancelledSubscriptions = await _context.UserSubscriptions.CountAsync(s => s.Status == SubscriptionStatus.Cancelled);
            var expiredSubscriptions = await _context.UserSubscriptions.CountAsync(s => s.Status == SubscriptionStatus.Expired);

            var totalRevenue = await _context.Payments
                .Where(p => p.Status == PaymentStatus.Success)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;

            var currentMonthRevenue = await _context.Payments
                .Where(p => p.Status == PaymentStatus.Success && p.CreatedAt >= startOfMonth)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;

            var failedPayments = await _context.Payments
                .CountAsync(p => p.Status == PaymentStatus.Failed && p.CreatedAt >= startDate);

            var monthlyRevenueQuery = await _context.Payments
                .Where(p => p.Status == PaymentStatus.Success && p.CreatedAt >= startDate)
                .GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    TotalRevenue = g.Sum(p => p.Amount)
                })
                .OrderBy(x => x.Year)
                .ThenBy(x => x.Month)
                .ToListAsync();

            var revenueMonthLabels = monthlyRevenueQuery
                .Select(x => new DateTime(x.Year, x.Month, 1).ToString("MMM yyyy"))
                .ToList();
            var revenueMonthData = monthlyRevenueQuery
                .Select(x => x.TotalRevenue)
                .ToList();

            List<string> growthLabels;
            List<int> growthData;

            if (range == "7days" || range == "30days")
            {
                var dailyGrowth = await _context.UserSubscriptions
                    .Where(s => s.CreatedAt >= startDate)
                    .GroupBy(s => new { s.CreatedAt.Year, s.CreatedAt.Month, s.CreatedAt.Day })
                    .Select(g => new
                    {
                        Year = g.Key.Year,
                        Month = g.Key.Month,
                        Day = g.Key.Day,
                        Count = g.Count()
                    })
                    .OrderBy(x => x.Year)
                    .ThenBy(x => x.Month)
                    .ThenBy(x => x.Day)
                    .ToListAsync();

                growthLabels = dailyGrowth.Select(x => new DateTime(x.Year, x.Month, x.Day).ToString("dd MMM")).ToList();
                growthData = dailyGrowth.Select(x => x.Count).ToList();
            }
            else
            {
                var monthlyGrowth = await _context.UserSubscriptions
                    .Where(s => s.CreatedAt >= startDate)
                    .GroupBy(s => new { s.CreatedAt.Year, s.CreatedAt.Month })
                    .Select(g => new
                    {
                        Year = g.Key.Year,
                        Month = g.Key.Month,
                        Count = g.Count()
                    })
                    .OrderBy(x => x.Year)
                    .ThenBy(x => x.Month)
                    .ToListAsync();

                growthLabels = monthlyGrowth.Select(x => new DateTime(x.Year, x.Month, 1).ToString("MMM yyyy")).ToList();
                growthData = monthlyGrowth.Select(x => x.Count).ToList();
            }

            var statusCounts = await _context.UserSubscriptions
                .GroupBy(s => s.Status)
                .Select(g => new
                {
                    Status = g.Key,
                    Count = g.Count()
                })
                .ToListAsync();

            var subStatusLabels = statusCounts.Select(x => x.Status.ToString()).ToList();
            var subStatusData = statusCounts.Select(x => x.Count).ToList();

            var planDist = await _context.UserSubscriptions
                .Where(s => (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing) && s.Plan != null)
                .GroupBy(s => s.Plan!.Name)
                .Select(g => new
                {
                    PlanName = g.Key,
                    Count = g.Count()
                })
                .ToListAsync();

            var planDistLabels = planDist.Select(x => x.PlanName).ToList();
            var planDistData = planDist.Select(x => x.Count).ToList();

            var paymentStatusCounts = await _context.Payments
                .Where(p => p.CreatedAt >= startDate)
                .GroupBy(p => p.Status)
                .Select(g => new
                {
                    Status = g.Key,
                    Count = g.Count()
                })
                .ToListAsync();

            var payStatusLabels = paymentStatusCounts.Select(x => x.Status.ToString()).ToList();
            var payStatusData = paymentStatusCounts.Select(x => x.Count).ToList();

            var viewModel = new AdminDashboardViewModel
            {
                TimeRange = range,
                StartDate = startDate,
                EndDate = now,
                TotalUsers = totalUsers,
                ActiveSubscriptions = activeSubscriptions,
                TrialingSubscriptions = trialingSubscriptions,
                CancelledSubscriptions = cancelledSubscriptions,
                ExpiredSubscriptions = expiredSubscriptions,
                TotalRevenue = totalRevenue,
                CurrentMonthRevenue = currentMonthRevenue,
                FailedPayments = failedPayments,

                RevenueMonthLabels = revenueMonthLabels,
                RevenueMonthData = revenueMonthData,

                GrowthLabels = growthLabels,
                GrowthData = growthData,

                SubscriptionStatusLabels = subStatusLabels,
                SubscriptionStatusData = subStatusData,

                PlanDistributionLabels = planDistLabels,
                PlanDistributionData = planDistData,

                PaymentStatusLabels = payStatusLabels,
                PaymentStatusData = payStatusData
            };

            return View(viewModel);
        }

        // ==========================================
        // 1. USER MANAGEMENT
        // ==========================================

        // GET: /Admin/Users
        public async Task<IActionResult> Users(string? search, string? status, int page = 1)
        {
            const int pageSize = 10;
            page = Math.Max(1, page);

            var query = _userManager.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(u => (u.Email != null && u.Email.Contains(search)) ||
                                         (u.FullName != null && u.FullName.Contains(search)));
            }

            if (status == "active")
            {
                query = query.Where(u => u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow);
            }
            else if (status == "disabled")
            {
                query = query.Where(u => u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow);
            }

            var totalUsers = await query.CountAsync();
            var users = await query
                .OrderBy(u => u.Email)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var userIds = users.Select(u => u.Id).ToList();

            // Load latest subscription for each user in bulk
            var latestSubs = await _context.UserSubscriptions
                .Include(s => s.Plan)
                .Where(s => userIds.Contains(s.UserId))
                .GroupBy(s => s.UserId)
                .Select(g => g.OrderByDescending(s => s.StartDate).FirstOrDefault())
                .ToDictionaryAsync(s => s!.UserId);

            // Counts for subscriptions, payments, and invoices
            var subCounts = await _context.UserSubscriptions
                .Where(s => userIds.Contains(s.UserId))
                .GroupBy(s => s.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count);

            var paymentCounts = await _context.Payments
                .Where(p => userIds.Contains(p.UserId))
                .GroupBy(p => p.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count);

            var invoiceCounts = await _context.Invoices
                .Where(i => userIds.Contains(i.UserId))
                .GroupBy(i => i.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count);

            var items = new List<AdminUserListItemViewModel>();
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                latestSubs.TryGetValue(user.Id, out var sub);

                var isDisabled = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;
                var regDate = sub?.CreatedAt ?? DateTime.UtcNow;

                items.Add(new AdminUserListItemViewModel
                {
                    Id = user.Id,
                    Name = user.FullName,
                    Email = user.Email ?? string.Empty,
                    Role = roles.FirstOrDefault() ?? "User",
                    IsDisabled = isDisabled,
                    CurrentPlan = sub?.Plan?.Name,
                    SubscriptionStatus = sub?.Status,
                    RegistrationDate = regDate,
                    SubscriptionsCount = subCounts.TryGetValue(user.Id, out var sc) ? sc : 0,
                    PaymentsCount = paymentCounts.TryGetValue(user.Id, out var pc) ? pc : 0,
                    InvoicesCount = invoiceCounts.TryGetValue(user.Id, out var ic) ? ic : 0
                });
            }

            ViewBag.Search = search;
            ViewBag.Status = status;

            return View(new PaginatedList<AdminUserListItemViewModel>(items, totalUsers, page, pageSize));
        }

        // GET: /Admin/UserDetails/5
        public async Task<IActionResult> UserDetails(string id, int subPage = 1, int payPage = 1, int invPage = 1)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            const int pageSize = 5;
            var roles = await _userManager.GetRolesAsync(user);
            var isDisabled = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;

            // Subscriptions
            var subsQuery = _context.UserSubscriptions
                .Include(s => s.Plan)
                .Where(s => s.UserId == id)
                .OrderByDescending(s => s.StartDate);

            var totalSubs = await subsQuery.CountAsync();
            var subs = await subsQuery.Skip((subPage - 1) * pageSize).Take(pageSize).ToListAsync();

            // Active subscription
            var activeSub = await _context.UserSubscriptions
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.UserId == id && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing));

            // Payments
            var payQuery = _context.Payments
                .Include(p => p.Subscription).ThenInclude(s => s!.Plan)
                .Where(p => p.UserId == id)
                .OrderByDescending(p => p.CreatedAt);

            var totalPays = await payQuery.CountAsync();
            var pays = await payQuery.Skip((payPage - 1) * pageSize).Take(pageSize).ToListAsync();

            // Invoices
            var invQuery = _context.Invoices
                .Include(i => i.Subscription).ThenInclude(s => s!.Plan)
                .Where(i => i.UserId == id)
                .OrderByDescending(i => i.InvoiceDate);

            var totalInvs = await invQuery.CountAsync();
            var invs = await invQuery.Skip((invPage - 1) * pageSize).Take(pageSize).ToListAsync();

            // Coupon usages
            var usageQuery = _context.CouponUsages
                .Include(u => u.Coupon)
                .Where(u => u.UserId == id)
                .OrderByDescending(u => u.UsedAt);

            var totalUsages = await usageQuery.CountAsync();
            var usages = await usageQuery.Take(10).ToListAsync();

            var vm = new AdminUserDetailsViewModel
            {
                User = user,
                Role = roles.FirstOrDefault() ?? "User",
                IsDisabled = isDisabled,
                ActiveSubscription = activeSub,
                Subscriptions = new PaginatedList<UserSubscription>(subs, totalSubs, subPage, pageSize),
                Payments = new PaginatedList<Payment>(pays, totalPays, payPage, pageSize),
                Invoices = new PaginatedList<Invoice>(invs, totalInvs, invPage, pageSize),
                CouponUsages = new PaginatedList<CouponUsage>(usages, totalUsages, 1, 10)
            };

            return View(vm);
        }

        // POST: /Admin/ToggleUserStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUserStatus(string id, bool disable, string? reason)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var currentAdminEmail = User.Identity?.Name ?? "Admin";

            // Prevent admin disabling themselves or the master admin account
            if (user.Email?.Equals("admin@subscription.com", StringComparison.OrdinalIgnoreCase) == true)
            {
                TempData["Error"] = "Action rejected: The root System Admin account cannot be disabled.";
                return RedirectToAction(nameof(UserDetails), new { id });
            }

            if (user.Email?.Equals(currentAdminEmail, StringComparison.OrdinalIgnoreCase) == true)
            {
                TempData["Error"] = "Action rejected: You cannot disable your own active administrator account.";
                return RedirectToAction(nameof(UserDetails), new { id });
            }

            if (disable)
            {
                await _userManager.SetLockoutEnabledAsync(user, true);
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
            }
            else
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
            }

            // Important business rule: Audit logging required!
            var actionName = disable ? "DisableUserAccount" : "EnableUserAccount";
            var details = $"Account {(disable ? "disabled" : "enabled")}. Reason: {reason ?? "Admin discretion"}";
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

            await _auditService.LogAsync(currentAdminEmail, actionName, "User", user.Id, details, ip);

            TempData["Message"] = $"User '{user.Email}' has been successfully {(disable ? "disabled" : "enabled")}.";
            return RedirectToAction(nameof(UserDetails), new { id });
        }

        // ==========================================
        // 2. SUBSCRIPTION MANAGEMENT
        // ==========================================

        // GET: /Admin/Subscriptions
        public async Task<IActionResult> Subscriptions(
            string? search,
            SubscriptionStatus? status,
            int? planId,
            DateTime? fromDate,
            DateTime? toDate,
            int page = 1)
        {
            const int pageSize = 10;
            page = Math.Max(1, page);

            var query = _context.UserSubscriptions
                .Include(s => s.User)
                .Include(s => s.Plan)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim().ToLower();
                query = query.Where(s => (s.User != null && (s.User.Email!.ToLower().Contains(search) ||
                                                             (s.User.FullName != null && s.User.FullName.ToLower().Contains(search)))) ||
                                         (s.Plan != null && s.Plan.Name.ToLower().Contains(search)));
            }

            if (status.HasValue)
            {
                query = query.Where(s => s.Status == status.Value);
            }

            if (planId.HasValue)
            {
                query = query.Where(s => s.PlanId == planId.Value);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(s => s.StartDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                var endOfDay = toDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(s => s.StartDate <= endOfDay);
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(s => s.StartDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var plans = await _context.SubscriptionPlans.OrderBy(p => p.Name).ToListAsync();

            var vm = new AdminSubscriptionListViewModel
            {
                Subscriptions = new PaginatedList<UserSubscription>(items, totalCount, page, pageSize),
                Plans = plans,
                Search = search,
                Status = status,
                PlanId = planId,
                FromDate = fromDate,
                ToDate = toDate
            };

            return View(vm);
        }

        // GET: /Admin/SubscriptionDetails/5
        public async Task<IActionResult> SubscriptionDetails(int id)
        {
            var sub = await _context.UserSubscriptions
                .Include(s => s.User)
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sub == null) return NotFound();

            var payments = await _context.Payments
                .Where(p => p.SubscriptionId == id)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            var invoices = await _context.Invoices
                .Where(i => i.SubscriptionId == id)
                .OrderByDescending(i => i.InvoiceDate)
                .ToListAsync();

            var histories = await _context.SubscriptionHistories
                .Include(h => h.OldPlan)
                .Include(h => h.NewPlan)
                .Where(h => h.SubscriptionId == id)
                .OrderByDescending(h => h.ChangedAt)
                .ToListAsync();

            ViewBag.Payments = payments;
            ViewBag.Invoices = invoices;
            ViewBag.Histories = histories;

            return View(sub);
        }

        // ==========================================
        // 3. PAYMENT MANAGEMENT
        // ==========================================

        // GET: /Admin/Payments
        public async Task<IActionResult> Payments(
            string? search,
            PaymentStatus? status,
            DateTime? fromDate,
            DateTime? toDate,
            int page = 1)
        {
            const int pageSize = 10;
            page = Math.Max(1, page);

            var query = _context.Payments
                .Include(p => p.User)
                .Include(p => p.Subscription).ThenInclude(s => s!.Plan)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(p => p.OrderId.Contains(search) ||
                                         (p.PaymentId != null && p.PaymentId.Contains(search)) ||
                                         (p.User != null && p.User.Email != null && p.User.Email.Contains(search)));
            }

            if (status.HasValue)
            {
                query = query.Where(p => p.Status == status.Value);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(p => p.CreatedAt >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                var endOfDay = toDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(p => p.CreatedAt <= endOfDay);
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var vm = new AdminPaymentListViewModel
            {
                Payments = new PaginatedList<Payment>(items, totalCount, page, pageSize),
                Search = search,
                Status = status,
                FromDate = fromDate,
                ToDate = toDate
            };

            return View(vm);
        }

        // ==========================================
        // 4. INVOICE MANAGEMENT
        // ==========================================

        // GET: /Admin/Invoices
        public async Task<IActionResult> Invoices(
            string? search,
            InvoiceStatus? status,
            DateTime? fromDate,
            DateTime? toDate,
            int page = 1)
        {
            const int pageSize = 10;
            page = Math.Max(1, page);

            var query = _context.Invoices
                .Include(i => i.User)
                .Include(i => i.Subscription).ThenInclude(s => s!.Plan)
                .Include(i => i.Payment)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim().ToLower();
                query = query.Where(i => i.InvoiceNumber.ToLower().Contains(search) ||
                                         (i.User != null && ((i.User.Email != null && i.User.Email.ToLower().Contains(search)) ||
                                                             (i.User.FullName != null && i.User.FullName.ToLower().Contains(search)))));
            }

            if (status.HasValue)
            {
                query = query.Where(i => i.Status == status.Value);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(i => i.InvoiceDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                var endOfDay = toDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(i => i.InvoiceDate <= endOfDay);
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(i => i.InvoiceDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var vm = new AdminInvoiceListViewModel
            {
                Invoices = new PaginatedList<Invoice>(items, totalCount, page, pageSize),
                Search = search,
                Status = status,
                FromDate = fromDate,
                ToDate = toDate
            };

            return View(vm);
        }

        // GET: /Admin/DownloadInvoice/5
        public async Task<IActionResult> DownloadInvoice(int id)
        {
            var invoice = await _invoiceService.GetInvoiceByIdAsync(id);
            if (invoice == null) return NotFound();

            var pdfBytes = _invoiceService.GenerateInvoicePdf(invoice);
            return File(pdfBytes, "application/pdf", $"{invoice.InvoiceNumber}.pdf");
        }

        // ==========================================
        // 5. COUPON MANAGEMENT (Phase 9)
        // ==========================================

        // GET: /Admin/Coupons
        public async Task<IActionResult> Coupons(int page = 1, int? couponId = null, int usagePage = 1)
        {
            const int pageSize = 10;
            page = Math.Max(1, page);
            usagePage = Math.Max(1, usagePage);

            var totalCoupons = await _couponService.GetTotalCouponsCountAsync();
            var coupons = await _couponService.GetAllCouponsAsync(page, pageSize);

            var totalUsages = await _couponService.GetTotalCouponUsagesCountAsync(couponId);
            var usages = await _couponService.GetCouponUsagesAsync(couponId, usagePage, pageSize);

            var vm = new AdminCouponsViewModel
            {
                Coupons = new PaginatedList<Coupon>(coupons, totalCoupons, page, pageSize),
                Usages = new PaginatedList<CouponUsage>(usages, totalUsages, usagePage, pageSize),
                SelectedCouponId = couponId
            };

            return View(vm);
        }

        // POST: /Admin/CreateCoupon
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCoupon(Coupon coupon)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Please provide valid coupon details.";
                return RedirectToAction(nameof(Coupons));
            }

            try
            {
                var adminEmail = User.Identity?.Name ?? "Admin";
                await _couponService.CreateCouponAsync(coupon, adminEmail);
                TempData["Message"] = $"Coupon '{coupon.Code.ToUpper()}' was created successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Coupons));
        }

        // POST: /Admin/EditCoupon
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCoupon(Coupon coupon)
        {
            try
            {
                var adminEmail = User.Identity?.Name ?? "Admin";
                await _couponService.UpdateCouponAsync(coupon, adminEmail);
                TempData["Message"] = $"Coupon '{coupon.Code}' updated successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Coupons));
        }

        // POST: /Admin/ToggleCoupon
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleCoupon(int id)
        {
            try
            {
                var adminEmail = User.Identity?.Name ?? "Admin";
                var status = await _couponService.ToggleCouponStatusAsync(id, adminEmail);
                TempData["Message"] = $"Coupon status changed to {(status ? "Active" : "Inactive")}.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Coupons));
        }

        // POST: /Admin/DeleteCoupon
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCoupon(int id)
        {
            try
            {
                var adminEmail = User.Identity?.Name ?? "Admin";
                await _couponService.DeleteCouponAsync(id, adminEmail);
                TempData["Message"] = "Coupon deleted successfully.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(Coupons));
        }

        // ==========================================
        // 6. AUDIT LOGS
        // ==========================================

        // GET: /Admin/AuditLogs
        public async Task<IActionResult> AuditLogs(int page = 1)
        {
            const int pageSize = 20;
            page = Math.Max(1, page);

            var total = await _auditService.GetTotalCountAsync();
            var logs = await _auditService.GetAuditLogsAsync(page, pageSize);

            return View(new PaginatedList<AuditLog>(logs, total, page, pageSize));
        }
    }
}
