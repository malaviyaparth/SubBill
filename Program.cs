using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using SubBill.Data;
using SubBill.Models;
using SubBill.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => { 
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireDigit = false;
    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail = false;
    options.SignIn.RequireConfirmedPhoneNumber = false;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddTransient<IEmailSender, EmailSender>();

// Register Domain Services
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ICouponService, CouponService>();

// Register Phase 10 Background Hosted Service for Expired Free Trials
builder.Services.AddHostedService<TrialExpirationBackgroundService>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});

builder.Services.AddControllersWithViews();

builder.Services.AddRazorPages();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

    string[] roles = { "Admin", "User" };
    foreach (var role in roles)
    {
        if(!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    string adminEmail = "admin@subscription.com";
    string adminPassword = "Admin@123";

    var adminUser = await userManager.FindByEmailAsync(adminEmail);
    if(adminUser == null)
    {
        adminUser = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FullName = "System Admin",
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(adminUser, adminPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
            await userManager.AddToRoleAsync(adminUser, "User");
        }
    }
    else
    {
        if (!await userManager.IsInRoleAsync(adminUser, "User"))
        {
            await userManager.AddToRoleAsync(adminUser, "User");
        }
    }

    var dbContext = services.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.ExecuteSqlRawAsync(@"
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Coupons')
        BEGIN
            CREATE TABLE [Coupons] (
                [Id] int NOT NULL IDENTITY,
                [Code] nvarchar(50) NOT NULL,
                [DiscountType] nvarchar(max) NOT NULL,
                [DiscountValue] decimal(18,2) NOT NULL,
                [MaxDiscount] decimal(18,2) NULL,
                [MinimumAmount] decimal(18,2) NOT NULL,
                [UsageLimit] int NULL,
                [UsedCount] int NOT NULL,
                [ValidFrom] datetime2 NOT NULL,
                [ValidUntil] datetime2 NOT NULL,
                [IsActive] bit NOT NULL,
                [CreatedAt] datetime2 NOT NULL,
                CONSTRAINT [PK_Coupons] PRIMARY KEY ([Id])
            );
            CREATE UNIQUE INDEX [IX_Coupons_Code] ON [Coupons] ([Code]);
        END;

        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CouponUsages')
        BEGIN
            CREATE TABLE [CouponUsages] (
                [Id] int NOT NULL IDENTITY,
                [CouponId] int NOT NULL,
                [UserId] nvarchar(450) NOT NULL,
                [SubscriptionId] int NULL,
                [DiscountAmount] decimal(18,2) NOT NULL,
                [UsedAt] datetime2 NOT NULL,
                CONSTRAINT [PK_CouponUsages] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_CouponUsages_Coupons_CouponId] FOREIGN KEY ([CouponId]) REFERENCES [Coupons] ([Id]) ON DELETE CASCADE,
                CONSTRAINT [FK_CouponUsages_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
                CONSTRAINT [FK_CouponUsages_UserSubscriptions_SubscriptionId] FOREIGN KEY ([SubscriptionId]) REFERENCES [UserSubscriptions] ([Id]) ON DELETE SET NULL
            );
            CREATE INDEX [IX_CouponUsages_CouponId] ON [CouponUsages] ([CouponId]);
            CREATE INDEX [IX_CouponUsages_UserId] ON [CouponUsages] ([UserId]);
        END;

        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AuditLogs')
        BEGIN
            CREATE TABLE [AuditLogs] (
                [Id] int NOT NULL IDENTITY,
                [AdminEmail] nvarchar(256) NOT NULL,
                [Action] nvarchar(100) NOT NULL,
                [EntityType] nvarchar(100) NOT NULL,
                [EntityId] nvarchar(100) NULL,
                [Details] nvarchar(1000) NULL,
                [Timestamp] datetime2 NOT NULL,
                [IpAddress] nvarchar(50) NULL,
                CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
            );
            CREATE INDEX [IX_AuditLogs_Timestamp] ON [AuditLogs] ([Timestamp]);
            CREATE INDEX [IX_AuditLogs_Action] ON [AuditLogs] ([Action]);
        END;

        -- Ensure UserSubscriptions has all Phase 1 & Phase 10 columns
        IF EXISTS (SELECT * FROM sys.tables WHERE name = 'UserSubscriptions')
        BEGIN
            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UserSubscriptions') AND name = 'CurrentPeriodStart')
            BEGIN
                ALTER TABLE [UserSubscriptions] ADD [CurrentPeriodStart] datetime2 NOT NULL CONSTRAINT [DF_UserSubscriptions_CurrentPeriodStart] DEFAULT GETUTCDATE();
                EXEC(N'UPDATE [UserSubscriptions] SET [CurrentPeriodStart] = [StartDate];');
            END;

            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UserSubscriptions') AND name = 'CurrentPeriodEnd')
            BEGIN
                ALTER TABLE [UserSubscriptions] ADD [CurrentPeriodEnd] datetime2 NOT NULL CONSTRAINT [DF_UserSubscriptions_CurrentPeriodEnd] DEFAULT GETUTCDATE();
                EXEC(N'
                IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(''UserSubscriptions'') AND name = ''ExpiryDate'')
                    UPDATE [UserSubscriptions] SET [CurrentPeriodEnd] = [ExpiryDate];
                ELSE
                    UPDATE [UserSubscriptions] SET [CurrentPeriodEnd] = DATEADD(month, 1, [StartDate]);
                ');
            END;

            -- If ExpiryDate exists in older schema as NOT NULL, make it NULLable so EF Core entity inserts succeed
            IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UserSubscriptions') AND name = 'ExpiryDate' AND is_nullable = 0)
            BEGIN
                DECLARE @defConstraint sysname;
                SELECT @defConstraint = d.name
                FROM sys.default_constraints d
                JOIN sys.columns c ON d.parent_column_id = c.column_id AND d.parent_object_id = c.object_id
                WHERE d.parent_object_id = OBJECT_ID('UserSubscriptions') AND c.name = 'ExpiryDate';

                IF @defConstraint IS NOT NULL
                BEGIN
                    EXEC('ALTER TABLE [UserSubscriptions] DROP CONSTRAINT [' + @defConstraint + '];');
                END;

                ALTER TABLE [UserSubscriptions] ALTER COLUMN [ExpiryDate] datetime2 NULL;
            END;

            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UserSubscriptions') AND name = 'AutoRenew')
            BEGIN
                ALTER TABLE [UserSubscriptions] ADD [AutoRenew] bit NOT NULL CONSTRAINT [DF_UserSubscriptions_AutoRenew] DEFAULT 1;
            END;

            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UserSubscriptions') AND name = 'CancelledAt')
            BEGIN
                ALTER TABLE [UserSubscriptions] ADD [CancelledAt] datetime2 NULL;
            END;

            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UserSubscriptions') AND name = 'CancellationReason')
            BEGIN
                ALTER TABLE [UserSubscriptions] ADD [CancellationReason] nvarchar(500) NULL;
            END;

            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UserSubscriptions') AND name = 'CreatedAt')
            BEGIN
                ALTER TABLE [UserSubscriptions] ADD [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_UserSubscriptions_CreatedAt] DEFAULT GETUTCDATE();
            END;

            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UserSubscriptions') AND name = 'UpdatedAt')
            BEGIN
                ALTER TABLE [UserSubscriptions] ADD [UpdatedAt] datetime2 NULL;
            END;

            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UserSubscriptions') AND name = 'TrialStartDate')
            BEGIN
                ALTER TABLE [UserSubscriptions] ADD [TrialStartDate] datetime2 NULL;
            END;

            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UserSubscriptions') AND name = 'TrialEndDate')
            BEGIN
                ALTER TABLE [UserSubscriptions] ADD [TrialEndDate] datetime2 NULL;
            END;

            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UserSubscriptions') AND name = 'HasUsedTrial')
            BEGIN
                ALTER TABLE [UserSubscriptions] ADD [HasUsedTrial] bit NOT NULL CONSTRAINT [DF_UserSubscriptions_HasUsedTrial] DEFAULT 0;
            END;
        END;

        -- Ensure Payments Table
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Payments')
        BEGIN
            CREATE TABLE [Payments] (
                [Id] int NOT NULL IDENTITY,
                [UserId] nvarchar(450) NOT NULL,
                [SubscriptionId] int NULL,
                [Amount] decimal(18,2) NOT NULL,
                [Currency] nvarchar(10) NOT NULL,
                [PaymentGateway] nvarchar(50) NOT NULL,
                [OrderId] nvarchar(100) NOT NULL,
                [PaymentId] nvarchar(100) NULL,
                [Signature] nvarchar(255) NULL,
                [Status] nvarchar(max) NOT NULL,
                [PaymentDate] datetime2 NULL,
                [CreatedAt] datetime2 NOT NULL,
                CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_Payments_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
                CONSTRAINT [FK_Payments_UserSubscriptions_SubscriptionId] FOREIGN KEY ([SubscriptionId]) REFERENCES [UserSubscriptions] ([Id]) ON DELETE SET NULL
            );
        END;

        -- Ensure SubscriptionHistories Table
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SubscriptionHistories')
        BEGIN
            CREATE TABLE [SubscriptionHistories] (
                [Id] int NOT NULL IDENTITY,
                [SubscriptionId] int NOT NULL,
                [OldPlanId] int NOT NULL,
                [NewPlanId] int NOT NULL,
                [ChangeType] nvarchar(max) NOT NULL,
                [ChangedAt] datetime2 NOT NULL,
                CONSTRAINT [PK_SubscriptionHistories] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_SubscriptionHistories_SubscriptionPlans_NewPlanId] FOREIGN KEY ([NewPlanId]) REFERENCES [SubscriptionPlans] ([Id]) ON DELETE NO ACTION,
                CONSTRAINT [FK_SubscriptionHistories_SubscriptionPlans_OldPlanId] FOREIGN KEY ([OldPlanId]) REFERENCES [SubscriptionPlans] ([Id]) ON DELETE NO ACTION,
                CONSTRAINT [FK_SubscriptionHistories_UserSubscriptions_SubscriptionId] FOREIGN KEY ([SubscriptionId]) REFERENCES [UserSubscriptions] ([Id]) ON DELETE CASCADE
            );
        END;

        -- Ensure Invoices Table
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Invoices')
        BEGIN
            CREATE TABLE [Invoices] (
                [Id] int NOT NULL IDENTITY,
                [InvoiceNumber] nvarchar(50) NOT NULL,
                [UserId] nvarchar(450) NOT NULL,
                [SubscriptionId] int NULL,
                [PaymentId] int NULL,
                [Amount] decimal(18,2) NOT NULL,
                [TaxAmount] decimal(18,2) NOT NULL,
                [TotalAmount] decimal(18,2) NOT NULL,
                [Currency] nvarchar(10) NOT NULL,
                [InvoiceDate] datetime2 NOT NULL,
                [DueDate] datetime2 NOT NULL,
                [Status] nvarchar(max) NOT NULL,
                [CreatedAt] datetime2 NOT NULL,
                CONSTRAINT [PK_Invoices] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_Invoices_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
                CONSTRAINT [FK_Invoices_Payments_PaymentId] FOREIGN KEY ([PaymentId]) REFERENCES [Payments] ([Id]) ON DELETE SET NULL,
                CONSTRAINT [FK_Invoices_UserSubscriptions_SubscriptionId] FOREIGN KEY ([SubscriptionId]) REFERENCES [UserSubscriptions] ([Id]) ON DELETE SET NULL
            );
            CREATE UNIQUE INDEX [IX_Invoices_InvoiceNumber] ON [Invoices] ([InvoiceNumber]);
        END;

        -- Phase 2: PlanFeatures Table
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PlanFeatures')
        BEGIN
            CREATE TABLE [PlanFeatures] (
                [Id] int NOT NULL IDENTITY,
                [PlanId] int NOT NULL,
                [FeatureName] nvarchar(100) NOT NULL,
                [FeatureValue] nvarchar(100) NOT NULL,
                [CreatedAt] datetime2 NOT NULL,
                CONSTRAINT [PK_PlanFeatures] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_PlanFeatures_SubscriptionPlans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [SubscriptionPlans] ([Id]) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX [IX_PlanFeatures_PlanId_FeatureName] ON [PlanFeatures] ([PlanId], [FeatureName]);
        END;

        -- Phase 10: TrialDays on SubscriptionPlans
        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('SubscriptionPlans') AND name = 'TrialDays')
        BEGIN
            ALTER TABLE [SubscriptionPlans] ADD [TrialDays] int NOT NULL CONSTRAINT [DF_SubscriptionPlans_TrialDays] DEFAULT 0;
            EXEC(N'UPDATE [SubscriptionPlans] SET [TrialDays] = 14 WHERE [Name] = ''Basic''; UPDATE [SubscriptionPlans] SET [TrialDays] = 7 WHERE [Name] = ''Pro'';');
        END;

        -- Seed default features if table is empty
        IF EXISTS (SELECT * FROM sys.tables WHERE name = 'PlanFeatures')
        BEGIN
            EXEC(N'
            IF NOT EXISTS (SELECT 1 FROM [PlanFeatures])
            BEGIN
                DECLARE @BasicId int = (SELECT TOP 1 [Id] FROM [SubscriptionPlans] WHERE [Name] = ''Basic'');
                DECLARE @ProId int = (SELECT TOP 1 [Id] FROM [SubscriptionPlans] WHERE [Name] = ''Pro'');
                DECLARE @EntId int = (SELECT TOP 1 [Id] FROM [SubscriptionPlans] WHERE [Name] = ''Enterprise'');

                IF @BasicId IS NOT NULL
                BEGIN
                    INSERT INTO [PlanFeatures] ([PlanId], [FeatureName], [FeatureValue], [CreatedAt]) VALUES
                    (@BasicId, ''Projects'', ''5 Projects'', GETUTCDATE()),
                    (@BasicId, ''Storage'', ''10 GB Cloud Storage'', GETUTCDATE()),
                    (@BasicId, ''Team Members'', ''2 Users'', GETUTCDATE()),
                    (@BasicId, ''Support'', ''Community Support'', GETUTCDATE());
                END

                IF @ProId IS NOT NULL
                BEGIN
                    INSERT INTO [PlanFeatures] ([PlanId], [FeatureName], [FeatureValue], [CreatedAt]) VALUES
                    (@ProId, ''Projects'', ''50 Projects'', GETUTCDATE()),
                    (@ProId, ''Storage'', ''100 GB Cloud Storage'', GETUTCDATE()),
                    (@ProId, ''Team Members'', ''10 Users'', GETUTCDATE()),
                    (@ProId, ''API Access'', ''Full REST API Access'', GETUTCDATE()),
                    (@ProId, ''Support'', ''Priority Email Support'', GETUTCDATE());
                END

                IF @EntId IS NOT NULL
                BEGIN
                    INSERT INTO [PlanFeatures] ([PlanId], [FeatureName], [FeatureValue], [CreatedAt]) VALUES
                    (@EntId, ''Projects'', ''Unlimited Projects'', GETUTCDATE()),
                    (@EntId, ''Storage'', ''1 TB Cloud Storage'', GETUTCDATE()),
                    (@EntId, ''Team Members'', ''Unlimited Users'', GETUTCDATE()),
                    (@EntId, ''API Access'', ''Dedicated Endpoints & Webhooks'', GETUTCDATE()),
                    (@EntId, ''Support'', ''24/7 Dedicated Account Manager'', GETUTCDATE()),
                    (@EntId, ''SLA'', ''99.9% Uptime Guarantee'', GETUTCDATE());
                END
            END
            ');
        END;
    ");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages();
app.MapStaticAssets();

app.Run();
