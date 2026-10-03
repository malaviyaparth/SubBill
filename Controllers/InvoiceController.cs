using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SubBill.Models;
using SubBill.Services;

namespace SubBill.Controllers
{
    [Authorize]
    public class InvoiceController : Controller
    {
        private readonly IInvoiceService _invoiceService;
        private readonly UserManager<ApplicationUser> _userManager;

        public InvoiceController(IInvoiceService invoiceService, UserManager<ApplicationUser> userManager)
        {
            _invoiceService = invoiceService;
            _userManager = userManager;
        }

        // GET: /Invoice
        [Authorize]
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            var invoices = await _invoiceService.GetUserInvoicesAsync(userId);
            return View(invoices);
        }

        // GET: /Invoice/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var invoice = await _invoiceService.GetInvoiceByIdAsync(id);
            if (invoice == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var isAdmin = User.IsInRole("Admin");

            if (!isAdmin && invoice.UserId != userId)
            {
                return Forbid();
            }

            return View(invoice);
        }

        // GET: /Invoice/Download/5
        public async Task<IActionResult> Download(int id)
        {
            var invoice = await _invoiceService.GetInvoiceByIdAsync(id);
            if (invoice == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var isAdmin = User.IsInRole("Admin");

            if (!isAdmin && invoice.UserId != userId)
            {
                return Forbid();
            }

            var pdfBytes = _invoiceService.GenerateInvoicePdf(invoice);
            var fileName = $"{invoice.InvoiceNumber}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }

        // GET: /Invoice/AdminIndex
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AdminIndex(string? searchTerm, InvoiceStatus? status)
        {
            var invoices = await _invoiceService.GetAllInvoicesAsync(searchTerm, status);
            ViewBag.SearchTerm = searchTerm;
            ViewBag.Status = status;
            return View(invoices);
        }
    }
}
