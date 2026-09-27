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

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

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
        }
    }
}
