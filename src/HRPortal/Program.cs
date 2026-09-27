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
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireClaim("IsAdmin", "1"));
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
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();