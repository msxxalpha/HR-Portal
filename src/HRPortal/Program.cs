using HRPortal.Data;
using HRPortal.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
    o.IdleTimeout = TimeSpan.FromHours(4);
});

builder.Services.AddDbContext<HRPortalDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<SystemSettingsService>();
builder.Services.AddScoped<EnvironmentSettingsService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<OtpService>();
builder.Services.AddScoped<OrganizationService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<EmployeeExcelService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<RoleService>();
builder.Services.AddScoped<ISmsService, ConfigurableSmsService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/Denied";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireClaim("IsAdmin", "1"));

    foreach (var permission in RoleService.DefaultPermissions)
        options.AddPolicy(permission.Code, policy =>
            policy.RequireAssertion(context =>
                context.User.HasClaim("IsAdmin", "1") ||
                context.User.HasClaim("Permission", permission.Code)));

    options.AddPolicy("UsersRoles.Manage", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim("IsAdmin", "1") ||
            context.User.HasClaim("Permission", "Users.Manage") ||
            context.User.HasClaim("Permission", "Roles.Manage")));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HRPortalDbContext>();
    await DatabaseInitializer.InitializeAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";

    if (!path.StartsWith("/Account", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/SystemSettings", StringComparison.OrdinalIgnoreCase))
    {
        var db = ctx.RequestServices.GetRequiredService<HRPortalDbContext>();
        var settings = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync();

        if (settings?.MaintenanceMode == true)
        {
            ctx.Response.StatusCode = 503;
            await ctx.Response.WriteAsync(
                $"<html lang='fa' dir='rtl'><meta charset='utf-8'><body style='font-family:Vazir,Arial;text-align:center;padding:80px'><h2>{settings.ApplicationName}</h2><p>{settings.MaintenanceMessage}</p></body></html>");
            return;
        }
    }

    await next();
});

app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "root",
    pattern: "",
    defaults: new { controller = "Home", action = "Index" });

app.MapControllerRoute(
    name: "employees",
    pattern: "Employees/{action=Index}/{id?}",
    defaults: new { controller = "Employees" });

app.MapControllerRoute(
    name: "organization",
    pattern: "Organization/{action=Index}/{id?}",
    defaults: new { controller = "Organization" });

app.MapControllerRoute(
    name: "payroll",
    pattern: "Payroll/{action=Payslip}/{id?}",
    defaults: new { controller = "Payroll" });

app.MapControllerRoute(
    name: "settings",
    pattern: "SystemSettings/{action=Index}/{id?}",
    defaults: new { controller = "SystemSettings" });

app.MapControllerRoute(
    name: "account",
    pattern: "Account/{action=Login}/{id?}",
    defaults: new { controller = "Account" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();