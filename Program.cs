using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SubBill.Data;
using SubBill.Models;
using Microsoft.AspNetCore.Identity.UI.Services;
using SubBill.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => { 
    //options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    //options.Password.RequireUppercase = false;
    //options.Password.RequireLowercase = false;
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

    if(await userManager.FindByEmailAsync(adminEmail) == null)
    {
        var adminUser = new ApplicationUser
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
        }
    };

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
app.MapRazorPages();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
