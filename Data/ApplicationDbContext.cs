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
        public DbSet<PlanFeature> PlanFeatures { get; set; }
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
                    .IsRequired(false)
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

            builder.Entity<PlanFeature>(entity =>
            {
                entity.HasKey(f => f.Id);
                entity.Property(f => f.FeatureName).IsRequired().HasMaxLength(100);
                entity.Property(f => f.FeatureValue).HasMaxLength(100);
                entity.HasIndex(f => new { f.PlanId, f.FeatureName }).IsUnique();

                entity.HasOne(f => f.Plan)
                    .WithMany(p => p.Features)
                    .HasForeignKey(f => f.PlanId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<SubscriptionPlan>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Description).HasMaxLength(300);
                entity.Property(e => e.Price).HasColumnType("decimal(18,2)");
                entity.Property(e => e.BillingCycle).IsRequired();
                entity.Property(e => e.TrialDays).HasDefaultValue(0);
                entity.Property(e => e.IsActive).IsRequired();
            });

            builder.Entity<SubscriptionPlan>().HasData(
                new SubscriptionPlan
                {
                    Id = 1,
                    Name = "Basic",
                    Description = "Entry-level plan for individuals and small projects",
                    Price = 9.99m,
                    BillingCycle = BillingCycle.Monthly,
                    TrialDays = 14,
                    IsActive = true
                },

                new SubscriptionPlan
                {
                    Id = 2,
                    Name = "Pro",
                    Description = "Designed for growing teams and scaling businesses",
                    Price = 29.99m,
                    BillingCycle = BillingCycle.Monthly,
                    TrialDays = 7,
                    IsActive = true
                },

                new SubscriptionPlan
                {
                    Id = 3,
                    Name = "Enterprise",
                    Description = "Comprehensive features, high performance, and priority SLA",
                    Price = 299.99m,
                    BillingCycle = BillingCycle.Yearly,
                    TrialDays = 0,
                    IsActive = true
                }
            );

            builder.Entity<PlanFeature>().HasData(
                // Basic Plan Features
                new PlanFeature { Id = 1, PlanId = 1, FeatureName = "Projects", FeatureValue = "5 Projects", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new PlanFeature { Id = 2, PlanId = 1, FeatureName = "Storage", FeatureValue = "10 GB Cloud Storage", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new PlanFeature { Id = 3, PlanId = 1, FeatureName = "Team Members", FeatureValue = "2 Users", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new PlanFeature { Id = 4, PlanId = 1, FeatureName = "Support", FeatureValue = "Community Support", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },

                // Pro Plan Features
                new PlanFeature { Id = 5, PlanId = 2, FeatureName = "Projects", FeatureValue = "50 Projects", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new PlanFeature { Id = 6, PlanId = 2, FeatureName = "Storage", FeatureValue = "100 GB Cloud Storage", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new PlanFeature { Id = 7, PlanId = 2, FeatureName = "Team Members", FeatureValue = "10 Users", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new PlanFeature { Id = 8, PlanId = 2, FeatureName = "API Access", FeatureValue = "Full REST API Access", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new PlanFeature { Id = 9, PlanId = 2, FeatureName = "Support", FeatureValue = "Priority Email Support", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },

                // Enterprise Plan Features
                new PlanFeature { Id = 10, PlanId = 3, FeatureName = "Projects", FeatureValue = "Unlimited Projects", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new PlanFeature { Id = 11, PlanId = 3, FeatureName = "Storage", FeatureValue = "1 TB Cloud Storage", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new PlanFeature { Id = 12, PlanId = 3, FeatureName = "Team Members", FeatureValue = "Unlimited Users", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new PlanFeature { Id = 13, PlanId = 3, FeatureName = "API Access", FeatureValue = "Dedicated Endpoints & Webhooks", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new PlanFeature { Id = 14, PlanId = 3, FeatureName = "Support", FeatureValue = "24/7 Dedicated Account Manager", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new PlanFeature { Id = 15, PlanId = 3, FeatureName = "SLA", FeatureValue = "99.9% Uptime Guarantee", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
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
