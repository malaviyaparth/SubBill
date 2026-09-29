using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SubBill.Models;

namespace SubBill.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
        public DbSet<UserSubscription> UserSubscriptions { get; set; }
        public DbSet<SubscriptionHistory> SubscriptionHistories { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<Coupon> Coupons { get; set; }
        public DbSet<CouponUsage> CouponUsages { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<UserSubscription>(entity =>
            {
                entity.HasOne(s => s.User)
                    .WithMany()
                    .HasForeignKey(s => s.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(s => s.Plan)
                    .WithMany()
                    .HasForeignKey(s => s.PlanId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.Property(s => s.Status)
                    .HasConversion<string>();
            });

            builder.Entity<SubscriptionHistory>(entity =>
            {
                entity.HasOne(h => h.Subscription)
                    .WithMany()
                    .HasForeignKey(h => h.SubscriptionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(h => h.OldPlan)
                    .WithMany()
                    .HasForeignKey(h => h.OldPlanId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(h => h.NewPlan)
                    .WithMany()
                    .HasForeignKey(h => h.NewPlanId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.Property(h => h.ChangeType)
                    .HasConversion<string>();
            });

            builder.Entity<Payment>(entity =>
            {
                entity.HasOne(p => p.User)
                    .WithMany()
                    .HasForeignKey(p => p.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(p => p.Subscription)
                    .WithMany()
                    .HasForeignKey(p => p.SubscriptionId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.Property(p => p.Status)
                    .HasConversion<string>();
            });

            builder.Entity<Invoice>(entity =>
            {
                entity.HasOne(i => i.User)
                    .WithMany()
                    .HasForeignKey(i => i.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(i => i.Subscription)
                    .WithMany()
                    .HasForeignKey(i => i.SubscriptionId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(i => i.Payment)
                    .WithMany()
                    .HasForeignKey(i => i.PaymentId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(i => i.InvoiceNumber)
                    .IsUnique();

                entity.Property(i => i.Status)
                    .HasConversion<string>();
            });

            builder.Entity<SubscriptionPlan>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Description).HasMaxLength(300);
                entity.Property(e => e.Price).HasColumnType("decimal(18,2)");
                entity.Property(e => e.BillingCycle).IsRequired();
                entity.Property(e => e.IsActive).IsRequired();
            });

            builder.Entity<SubscriptionPlan>().HasData(
                new SubscriptionPlan
                {
                    Id = 1,
                    Name = "Basic",
                    Description = "Entry-level plan",
                    Price = 9.99m,
                    BillingCycle = BillingCycle.Monthly,
                    IsActive = true
                },

                new SubscriptionPlan
                {
                    Id = 2,
                    Name = "Pro",
                    Description = "For growing teams",
                    Price = 29.99m,
                    BillingCycle = BillingCycle.Monthly,
                    IsActive = true
                },

                new SubscriptionPlan
                {
                    Id = 3,
                    Name = "Enterprise",
                    Description = "Full feature set",
                    Price = 299.99m,
                    BillingCycle = BillingCycle.Yearly,
                    IsActive = true
                }
            );

            builder.Entity<Coupon>(entity =>
            {
                entity.HasIndex(c => c.Code).IsUnique();
                entity.Property(c => c.DiscountType).HasConversion<string>();
            });

            builder.Entity<CouponUsage>(entity =>
            {
                entity.HasOne(u => u.Coupon)
                    .WithMany()
                    .HasForeignKey(u => u.CouponId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(u => u.User)
                    .WithMany()
                    .HasForeignKey(u => u.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(u => u.Subscription)
                    .WithMany()
                    .HasForeignKey(u => u.SubscriptionId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<AuditLog>(entity =>
            {
                entity.HasIndex(a => a.Timestamp);
                entity.HasIndex(a => a.Action);
            });
        }
    }
}
