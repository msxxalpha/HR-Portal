using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class AdminService(HRPortalDbContext db)
{
    public async Task<AdminUser?> FindAsync(string username)
    {
        username = username.Trim();
        return await db.AdminUsers.SingleOrDefaultAsync(x =>
            x.Username == username && x.IsActive);
    }

    public bool VerifyPassword(AdminUser admin, string password)
        => PasswordHasher.Verify(password, admin.PasswordHash);

    public async Task SeedAsync()
    {
        if (await db.AdminUsers.AnyAsync())
            return;

        db.AdminUsers.Add(new AdminUser
        {
            Username = "admin",
            PasswordHash = PasswordHasher.Hash("Admin@12345"),
            DisplayName = "مدیر سامانه",
            IsActive = true,
            MustChangePassword = true
        });

        await db.SaveChangesAsync();
    }

    public async Task ChangePasswordAsync(AdminUser admin, string newPassword)
    {
        admin.PasswordHash = PasswordHasher.Hash(newPassword);
        admin.MustChangePassword = false;
        admin.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task MarkLoginAsync(AdminUser admin)
    {
        admin.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}