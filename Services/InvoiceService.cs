using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SubBill.Data;
using SubBill.Models;

namespace SubBill.Services
{
    public class InvoiceService : IInvoiceService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<InvoiceService> _logger;

        public InvoiceService(ApplicationDbContext context, ILogger<InvoiceService> logger)
        {
            _context = context;
            _logger = logger;
            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.UseSystemFonts = true;
        }

        public async Task<Invoice> CreateInvoiceForPaymentAsync(Payment payment, UserSubscription subscription)
        {
            // Prevent duplicate invoice generation for the same successful payment
            var existing = await _context.Invoices
                .Include(i => i.User)
                .Include(i => i.Subscription)
                    .ThenInclude(s => s!.Plan)
                .Include(i => i.Payment)
                .FirstOrDefaultAsync(i => i.PaymentId == payment.Id);

            if (existing != null)
            {
                return existing;
            }

            var year = DateTime.UtcNow.Year;
            var currentCount = await _context.Invoices.CountAsync() + 1;
            var invoiceNumber = $"INV-{year}-{currentCount:D6}";

            // GST Inclusive calculation (18% standard rate)
            decimal totalAmount = payment.Amount;
            decimal taxAmount = Math.Round(totalAmount * 0.18m / 1.18m, 2);
            decimal netAmount = totalAmount - taxAmount;

            var invoice = new Invoice
            {
                InvoiceNumber = invoiceNumber,
                UserId = payment.UserId,
                SubscriptionId = subscription.Id,
                PaymentId = payment.Id,
                Amount = netAmount,
                TaxAmount = taxAmount,
                TotalAmount = totalAmount,
                Currency = payment.Currency ?? "INR",
                InvoiceDate = DateTime.UtcNow,
                DueDate = DateTime.UtcNow.AddDays(15),
                Status = InvoiceStatus.Paid,
                CreatedAt = DateTime.UtcNow
            };

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Generated Invoice {InvoiceNumber} for Payment {PaymentId}", invoiceNumber, payment.Id);
            return invoice;
        }

        public async Task<Invoice?> GetInvoiceByIdAsync(int id)
        {
            return await _context.Invoices
                .Include(i => i.User)
                .Include(i => i.Payment)
                .Include(i => i.Subscription)
                    .ThenInclude(s => s!.Plan)
                .FirstOrDefaultAsync(i => i.Id == id);
        }

        public async Task<List<Invoice>> GetUserInvoicesAsync(string userId)
        {
            return await _context.Invoices
                .Include(i => i.Subscription)
                    .ThenInclude(s => s!.Plan)
                .Include(i => i.Payment)
                .Where(i => i.UserId == userId)
                .OrderByDescending(i => i.InvoiceDate)
                .ToListAsync();
        }

        public async Task<List<Invoice>> GetAllInvoicesAsync(string? searchTerm = null, InvoiceStatus? status = null)
        {
            var query = _context.Invoices
                .Include(i => i.User)
                .Include(i => i.Subscription)
                    .ThenInclude(s => s!.Plan)
                .Include(i => i.Payment)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                searchTerm = searchTerm.Trim().ToLower();
                query = query.Where(i => i.InvoiceNumber.ToLower().Contains(searchTerm) ||
                                         (i.User != null && (i.User.Email!.ToLower().Contains(searchTerm) ||
                                                             (i.User.FullName != null && i.User.FullName.ToLower().Contains(searchTerm)))));
            }

            if (status.HasValue)
            {
                query = query.Where(i => i.Status == status.Value);
            }

            return await query.OrderByDescending(i => i.InvoiceDate).ToListAsync();
        }

        public byte[] GenerateInvoicePdf(Invoice invoice)
        {
            var planName = invoice.Subscription?.Plan?.Name ?? "Subscription Plan";
            var cycle = invoice.Subscription?.Plan?.BillingCycle.ToString() ?? "Monthly";
            var customerName = string.IsNullOrWhiteSpace(invoice.User?.FullName) 
                ? (invoice.User?.UserName ?? "Valued Customer") 
                : invoice.User.FullName;
            var customerEmail = invoice.User?.Email ?? "customer@example.com";

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    // Header
                    page.Header().Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("SubBill Inc.").Bold().FontSize(22).FontColor(Colors.Blue.Darken2);
                            col.Item().Text("Subscription & Billing Platform").FontSize(10).FontColor(Colors.Grey.Medium);
                            col.Item().Text("support@subbill.com | www.subbill.com").FontSize(9).FontColor(Colors.Grey.Medium);
                        });

                        row.RelativeItem().AlignRight().Column(col =>
                        {
                            col.Item().Text("TAX INVOICE").Bold().FontSize(18).FontColor(Colors.Grey.Darken3);
                            col.Item().Text($"Invoice #: {invoice.InvoiceNumber}").Bold();
                            col.Item().Text($"Date: {invoice.InvoiceDate:dd MMM yyyy}");
                            col.Item().Text($"Status: {invoice.Status}").FontColor(invoice.Status == InvoiceStatus.Paid ? Colors.Green.Darken2 : Colors.Red.Darken2).Bold();
                        });
                    });

                    // Content
                    page.Content().PaddingVertical(1, Unit.Centimetre).Column(col =>
                    {
                        col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                        col.Item().PaddingVertical(10).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Billed To:").Bold().FontSize(11);
                                c.Item().Text(customerName);
                                c.Item().Text(customerEmail);
                            });

                            r.RelativeItem().AlignRight().Column(c =>
                            {
                                c.Item().Text("Payment Details:").Bold().FontSize(11);
                                c.Item().Text($"Gateway: {invoice.Payment?.PaymentGateway ?? "Online Gateway"}");
                                c.Item().Text($"Order ID: {invoice.Payment?.OrderId ?? "N/A"}");
                                c.Item().Text($"Payment Ref: {invoice.Payment?.PaymentId ?? "N/A"}");
                            });
                        });

                        col.Item().PaddingTop(15).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(40);
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("#").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Plan / Description").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Billing Cycle").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Amount").Bold();
                            });

                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text("1");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text($"{planName} Subscription");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(cycle);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text($"{invoice.Currency} {invoice.Amount:F2}");
                        });

                        col.Item().PaddingTop(15).AlignRight().Column(summary =>
                        {
                            summary.Item().Row(r =>
                            {
                                r.ConstantItem(120).Text("Net Amount:");
                                r.ConstantItem(100).AlignRight().Text($"{invoice.Currency} {invoice.Amount:F2}");
                            });
                            summary.Item().Row(r =>
                            {
                                r.ConstantItem(120).Text("GST / Tax (18%):");
                                r.ConstantItem(100).AlignRight().Text($"{invoice.Currency} {invoice.TaxAmount:F2}");
                            });
                            summary.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                            summary.Item().Row(r =>
                            {
                                r.ConstantItem(120).Text("Total Paid:").Bold().FontSize(12);
                                r.ConstantItem(100).AlignRight().Text($"{invoice.Currency} {invoice.TotalAmount:F2}").Bold().FontSize(12).FontColor(Colors.Blue.Darken2);
                            });
                        });

                        col.Item().PaddingTop(30).Text("Thank you for choosing SubBill! For questions, contact support@subbill.com.").FontSize(9).FontColor(Colors.Grey.Medium);
                    });

                    // Footer
                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.Span("Page ");
                        t.CurrentPageNumber();
                        t.Span(" of ");
                        t.TotalPages();
                    });
                });
            });

            return doc.GeneratePdf();
        }
    }
}
