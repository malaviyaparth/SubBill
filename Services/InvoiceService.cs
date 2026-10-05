using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

        public async Task<Invoice> CreateInvoiceForPaymentAsync(Payment payment, UserSubscription subscription, string? couponCode = null)
        {
            // Prevent duplicate invoice generation for the same successful payment
            var existing = await _context.Invoices
                .Include(i => i.User)
                .Include(i => i.Subscription)
                    .ThenInclude(s => s!.Plan)
                        .ThenInclude(p => p!.Features)
                .Include(i => i.Payment)
                .FirstOrDefaultAsync(i => i.PaymentId == payment.Id);

            if (existing != null)
            {
                await PopulateInvoiceDetailsAsync(existing);
                return existing;
            }

            var year = DateTime.UtcNow.Year;
            var currentCount = await _context.Invoices.CountAsync() + 1;
            string invoiceNumber;
            do
            {
                invoiceNumber = $"INV-{year}-{currentCount:D6}";
                currentCount++;
            } while (await _context.Invoices.AnyAsync(i => i.InvoiceNumber == invoiceNumber));

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

            if (!string.IsNullOrWhiteSpace(couponCode))
            {
                invoice.CouponCode = couponCode;
            }
            invoice.Subscription = subscription;
            await PopulateInvoiceDetailsAsync(invoice);

            _logger.LogInformation("Generated Invoice {InvoiceNumber} for Payment {PaymentId}", invoiceNumber, payment.Id);
            return invoice;
        }

        public async Task<Invoice?> GetInvoiceByIdAsync(int id)
        {
            var invoice = await _context.Invoices
                .Include(i => i.User)
                .Include(i => i.Payment)
                .Include(i => i.Subscription)
                    .ThenInclude(s => s!.Plan)
                        .ThenInclude(p => p!.Features)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (invoice != null)
            {
                await PopulateInvoiceDetailsAsync(invoice);
            }

            return invoice;
        }

        public async Task<List<Invoice>> GetUserInvoicesAsync(string userId)
        {
            var invoices = await _context.Invoices
                .Include(i => i.Subscription)
                    .ThenInclude(s => s!.Plan)
                        .ThenInclude(p => p!.Features)
                .Include(i => i.Payment)
                .Where(i => i.UserId == userId)
                .OrderByDescending(i => i.InvoiceDate)
                .ToListAsync();

            foreach (var inv in invoices)
            {
                await PopulateInvoiceDetailsAsync(inv);
            }

            return invoices;
        }

        public async Task<List<Invoice>> GetAllInvoicesAsync(string? searchTerm = null, InvoiceStatus? status = null)
        {
            var query = _context.Invoices
                .Include(i => i.User)
                .Include(i => i.Subscription)
                    .ThenInclude(s => s!.Plan)
                        .ThenInclude(p => p!.Features)
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

            var invoices = await query.OrderByDescending(i => i.InvoiceDate).ToListAsync();
            foreach (var inv in invoices)
            {
                await PopulateInvoiceDetailsAsync(inv);
            }

            return invoices;
        }

        private async Task PopulateInvoiceDetailsAsync(Invoice invoice)
        {
            if (invoice == null) return;

            // 1. Ensure Subscription and Plan with Features are loaded
            if (invoice.SubscriptionId.HasValue && invoice.Subscription == null)
            {
                invoice.Subscription = await _context.UserSubscriptions
                    .Include(s => s.Plan)
                        .ThenInclude(p => p!.Features)
                    .FirstOrDefaultAsync(s => s.Id == invoice.SubscriptionId.Value);
            }
            else if (invoice.Subscription?.Plan != null && (invoice.Subscription.Plan.Features == null || !invoice.Subscription.Plan.Features.Any()))
            {
                var features = await _context.PlanFeatures
                    .Where(f => f.PlanId == invoice.Subscription.PlanId)
                    .ToListAsync();
                invoice.Subscription.Plan.Features = features;
            }

            // 2. Load applied coupon if any
            CouponUsage? usage = null;
            if (invoice.SubscriptionId.HasValue)
            {
                usage = await _context.CouponUsages
                    .Include(u => u.Coupon)
                    .Where(u => u.SubscriptionId == invoice.SubscriptionId.Value)
                    .OrderByDescending(u => u.UsedAt)
                    .FirstOrDefaultAsync();
            }

            if (usage == null && !string.IsNullOrEmpty(invoice.CouponCode))
            {
                usage = await _context.CouponUsages
                    .Include(u => u.Coupon)
                    .Where(u => u.UserId == invoice.UserId && u.Coupon != null && u.Coupon.Code == invoice.CouponCode)
                    .OrderByDescending(u => u.UsedAt)
                    .FirstOrDefaultAsync();
            }

            if (usage == null)
            {
                usage = await _context.CouponUsages
                    .Include(u => u.Coupon)
                    .Where(u => u.UserId == invoice.UserId && u.UsedAt >= invoice.CreatedAt.AddHours(-2) && u.UsedAt <= invoice.CreatedAt.AddHours(2))
                    .OrderByDescending(u => u.UsedAt)
                    .FirstOrDefaultAsync();
            }

            if (usage != null && usage.Coupon != null)
            {
                invoice.CouponCode = usage.Coupon.Code;
                invoice.CouponDiscount = usage.DiscountAmount;
            }

            // 3. Resolve base plan price
            var planPrice = invoice.Subscription?.Plan?.Price ?? 0;
            if (planPrice > 0)
            {
                invoice.BasePlanPrice = planPrice;
            }
            else
            {
                invoice.BasePlanPrice = invoice.TotalAmount + invoice.CouponDiscount;
            }
        }

        public byte[] GenerateInvoicePdf(Invoice invoice)
        {
            var plan = invoice.Subscription?.Plan;
            var planName = plan?.Name ?? "Subscription Plan";
            var cycle = plan?.BillingCycle.ToString() ?? "Monthly";
            var features = plan?.Features?.ToList() ?? new List<PlanFeature>();
            var customerName = string.IsNullOrWhiteSpace(invoice.User?.FullName) 
                ? (invoice.User?.UserName ?? "Valued Customer") 
                : invoice.User.FullName;
            var customerEmail = invoice.User?.Email ?? "customer@example.com";
            var periodStart = invoice.PeriodStart.ToString("dd MMM yyyy");
            var periodEnd = invoice.PeriodEnd.ToString("dd MMM yyyy");

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9.5f).FontFamily("Arial"));

                    // Header
                    page.Header().Column(headerCol =>
                    {
                        headerCol.Item().Row(row =>
                        {
                            row.RelativeItem().Column(col =>
                            {
                                col.Item().Text("SubBill Inc.").Bold().FontSize(22).FontColor(Colors.Blue.Darken2);
                                col.Item().Text("Cloud Software as a Service (SaaS) Platform").FontSize(9.5f).FontColor(Colors.Grey.Darken1);
                                col.Item().Text("GSTIN: 27AABCS1429B1Z0 | SAC: 998313").Bold().FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                                col.Item().Text("SubBill Tower, Tech City, Maharashtra 400001, India").FontSize(8.5f).FontColor(Colors.Grey.Medium);
                                col.Item().Text("billing@subbill.com | www.subbill.com").FontSize(8.5f).FontColor(Colors.Grey.Medium);
                            });

                            row.RelativeItem().AlignRight().Column(col =>
                            {
                                col.Item().Text("TAX INVOICE").Bold().FontSize(20).FontColor(Colors.Grey.Darken3);
                                col.Item().Text($"Invoice #: {invoice.InvoiceNumber}").Bold().FontSize(10);
                                col.Item().Text($"Date: {invoice.InvoiceDate:dd MMM yyyy}").FontSize(9);
                                col.Item().Text($"Due Date: {invoice.DueDate:dd MMM yyyy}").FontSize(9);
                                col.Item().PaddingTop(3).Container().Background(invoice.Status == InvoiceStatus.Paid ? Colors.Green.Lighten4 : Colors.Yellow.Lighten4).PaddingHorizontal(8).PaddingVertical(2).Text(invoice.Status.ToString().ToUpper()).Bold().FontSize(9).FontColor(invoice.Status == InvoiceStatus.Paid ? Colors.Green.Darken3 : Colors.Orange.Darken3);
                            });
                        });

                        headerCol.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    });

                    // Content
                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        // 1. Subscription Service Period Banner
                        col.Item().Background(Colors.Blue.Lighten5).Border(1).BorderColor(Colors.Blue.Lighten3).Padding(8).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("SERVICE SUBSCRIPTION COVERAGE PERIOD").Bold().FontSize(8).FontColor(Colors.Blue.Darken3);
                                c.Item().Text($"{periodStart}  to  {periodEnd} ({cycle} Billing Cycle)").Bold().FontSize(11).FontColor(Colors.Blue.Darken3);
                            });

                            r.RelativeItem().AlignRight().Column(c =>
                            {
                                c.Item().Text($"Plan: {planName} Plan").Bold().FontSize(9.5f).FontColor(Colors.Grey.Darken3);
                                c.Item().Text("SAC Code: 998313 | Supply: Maharashtra (27)").FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                            });
                        });

                        // 2. Customer & Payment Information
                        col.Item().PaddingTop(10).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Billed To (Customer):").Bold().FontSize(10).FontColor(Colors.Grey.Darken2);
                                c.Item().Text(customerName).Bold().FontSize(10.5f);
                                c.Item().Text(customerEmail).FontSize(9);
                                c.Item().Text($"User Ref: {invoice.UserId}").FontSize(8).FontColor(Colors.Grey.Medium);
                                c.Item().Text("Place of Supply: Maharashtra (State Code 27)").FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                            });

                            r.RelativeItem().AlignRight().Column(c =>
                            {
                                c.Item().Text("Payment Details:").Bold().FontSize(10).FontColor(Colors.Grey.Darken2);
                                c.Item().Text($"Gateway: {invoice.Payment?.PaymentGateway ?? "Online Gateway"}").FontSize(9);
                                c.Item().Text($"Order ID: {invoice.Payment?.OrderId ?? "N/A"}").FontSize(8.5f);
                                c.Item().Text($"Transaction Ref: {invoice.Payment?.PaymentId ?? "N/A"}").FontSize(8.5f);
                                var payDate = invoice.Payment?.PaymentDate ?? invoice.CreatedAt;
                                c.Item().Text($"Paid On: {payDate:dd MMM yyyy HH:mm UTC}").FontSize(8.5f).FontColor(Colors.Green.Darken2);
                            });
                        });

                        // 3. Line Items & Included Features Table
                        col.Item().PaddingTop(15).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(25);
                                columns.RelativeColumn(5);
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(85);
                                columns.ConstantColumn(75);
                                columns.ConstantColumn(85);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("#").Bold().FontSize(8.5f);
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Service Description & Features").Bold().FontSize(8.5f);
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("SAC").Bold().FontSize(8.5f);
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Service Period").Bold().FontSize(8.5f);
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Taxable Net").Bold().FontSize(8.5f);
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignRight().Text("Total Amount").Bold().FontSize(8.5f);
                            });

                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text("1").FontSize(9);
                            
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Column(itemCol =>
                            {
                                itemCol.Item().Text($"{planName} Subscription").Bold().FontSize(10);
                                itemCol.Item().Text(invoice.ServiceDescription).FontSize(8).FontColor(Colors.Grey.Darken1);

                                if (features.Any())
                                {
                                    itemCol.Item().PaddingTop(3).Text("Included Plan Features:").Bold().FontSize(8).FontColor(Colors.Blue.Darken2);
                                    foreach (var feat in features)
                                    {
                                        itemCol.Item().Text($"• {feat.FeatureName}: {feat.FeatureValue}").FontSize(8).FontColor(Colors.Grey.Darken2);
                                    }
                                }
                            });

                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(invoice.SacCode).FontSize(8.5f);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Column(pCol =>
                            {
                                pCol.Item().Text(periodStart).FontSize(8);
                                pCol.Item().Text($"to {periodEnd}").FontSize(8);
                                pCol.Item().Text($"({cycle})").FontSize(7.5f).FontColor(Colors.Grey.Medium);
                            });
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text($"{invoice.Currency} {invoice.Amount:F2}").FontSize(9);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignRight().Text($"{invoice.Currency} {invoice.TotalAmount:F2}").Bold().FontSize(9);
                        });

                        // 4. Coupon Notice (if coupon applied)
                        if (!string.IsNullOrEmpty(invoice.CouponCode))
                        {
                            col.Item().PaddingTop(8).Background(Colors.Green.Lighten5).Border(0.5f).BorderColor(Colors.Green.Lighten2).Padding(6).Row(cr =>
                            {
                                cr.RelativeItem().Text($"✓ Coupon Applied: '{invoice.CouponCode}' — Promotional Discount of {invoice.Currency} {invoice.CouponDiscount:F2} applied to this billing cycle.").FontSize(8.5f).FontColor(Colors.Green.Darken3).Bold();
                            });
                        }

                        // 5. GST Breakdown Table and Financial Summary
                        col.Item().PaddingTop(12).Row(calcRow =>
                        {
                            // Left: Detailed Indian GST Schedule Table
                            calcRow.RelativeItem(3).Column(gstCol =>
                            {
                                gstCol.Item().Text("GST Tax Schedule (SAC 998313 — IT SaaS Services)").Bold().FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                                gstCol.Item().PaddingTop(3).Table(gstTable =>
                                {
                                    gstTable.ColumnsDefinition(gc =>
                                    {
                                        gc.ConstantColumn(45);
                                        gc.RelativeColumn(2);
                                        gc.RelativeColumn(2);
                                        gc.RelativeColumn(2);
                                        gc.RelativeColumn(2);
                                    });

                                    gstTable.Header(gh =>
                                    {
                                        gh.Cell().Background(Colors.Grey.Lighten4).Padding(3).Text("SAC").Bold().FontSize(7.5f);
                                        gh.Cell().Background(Colors.Grey.Lighten4).Padding(3).AlignRight().Text("Taxable").Bold().FontSize(7.5f);
                                        gh.Cell().Background(Colors.Grey.Lighten4).Padding(3).AlignRight().Text("CGST 9%").Bold().FontSize(7.5f);
                                        gh.Cell().Background(Colors.Grey.Lighten4).Padding(3).AlignRight().Text("SGST 9%").Bold().FontSize(7.5f);
                                        gh.Cell().Background(Colors.Grey.Lighten4).Padding(3).AlignRight().Text("Total GST").Bold().FontSize(7.5f);
                                    });

                                    gstTable.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(invoice.SacCode).FontSize(7.5f);
                                    gstTable.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).AlignRight().Text($"{invoice.Amount:F2}").FontSize(7.5f);
                                    gstTable.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).AlignRight().Text($"{invoice.CGST:F2}").FontSize(7.5f);
                                    gstTable.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).AlignRight().Text($"{invoice.SGST:F2}").FontSize(7.5f);
                                    gstTable.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3).AlignRight().Text($"{invoice.TaxAmount:F2}").Bold().FontSize(7.5f);
                                });

                                gstCol.Item().PaddingTop(6).Text("GST Calculation: GST @ 18% inclusive (CGST 9% + SGST 9%). Supply treated as intra-state supply within Maharashtra state jurisdiction.").FontSize(7.5f).FontColor(Colors.Grey.Medium);
                            });

                            calcRow.ConstantItem(15);

                            // Right: Summary of Payments
                            calcRow.RelativeItem(2).Column(summary =>
                            {
                                if (invoice.CouponDiscount > 0)
                                {
                                    summary.Item().Row(r =>
                                    {
                                        r.RelativeItem().Text("Base Plan Price:").FontSize(9);
                                        r.RelativeItem().AlignRight().Text($"{invoice.Currency} {invoice.BasePlanPrice:F2}").FontSize(9);
                                    });
                                    summary.Item().Row(r =>
                                    {
                                        r.RelativeItem().Text($"Coupon ({invoice.CouponCode}):").FontSize(8.5f).FontColor(Colors.Green.Darken2);
                                        r.RelativeItem().AlignRight().Text($"-{invoice.Currency} {invoice.CouponDiscount:F2}").FontSize(8.5f).FontColor(Colors.Green.Darken2).Bold();
                                    });
                                }

                                summary.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Taxable Net Amount:").FontSize(9);
                                    r.RelativeItem().AlignRight().Text($"{invoice.Currency} {invoice.Amount:F2}").FontSize(9);
                                });
                                summary.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("CGST (9.0%):").FontSize(8.5f);
                                    r.RelativeItem().AlignRight().Text($"{invoice.Currency} {invoice.CGST:F2}").FontSize(8.5f);
                                });
                                summary.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("SGST (9.0%):").FontSize(8.5f);
                                    r.RelativeItem().AlignRight().Text($"{invoice.Currency} {invoice.SGST:F2}").FontSize(8.5f);
                                });
                                summary.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Total GST (18.0%):").FontSize(8.5f).Bold();
                                    r.RelativeItem().AlignRight().Text($"{invoice.Currency} {invoice.TaxAmount:F2}").FontSize(8.5f).Bold();
                                });
                                summary.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                                summary.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Total Paid:").Bold().FontSize(12);
                                    r.RelativeItem().AlignRight().Text($"{invoice.Currency} {invoice.TotalAmount:F2}").Bold().FontSize(12).FontColor(Colors.Blue.Darken2);
                                });
                            });
                        });

                        // 6. Notes & Electronic Signature Declaration
                        col.Item().PaddingTop(20).Column(noteCol =>
                        {
                            noteCol.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten3);
                            noteCol.Item().PaddingTop(5).Text("Terms & Conditions:").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                            noteCol.Item().Text("1. Subscription fee covers uninterrupted access to the platform and specified features for the billed service period.").FontSize(7.5f).FontColor(Colors.Grey.Medium);
                            noteCol.Item().Text("2. This is a computer-generated Tax Invoice and requires no physical signature under Section 65B of the Indian Evidence Act / IT Act 2000.").FontSize(7.5f).FontColor(Colors.Grey.Medium);
                            noteCol.Item().Text("3. For billing questions or inquiries, email support@subbill.com or visit https://subbill.com/support").FontSize(7.5f).FontColor(Colors.Grey.Medium);
                        });
                    });

                    // Footer
                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.Span("Page ");
                        t.CurrentPageNumber();
                        t.Span(" of ");
                        t.TotalPages();
                        t.Span(" • SubBill Inc. • Tax Invoice");
                    });
                });
            });

            return doc.GeneratePdf();
        }
    }
}
