using System.Security.Cryptography;
using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

public class OtpService(HRPortalDbContext db, ISmsService sms)
{
    public async Task<(bool Success, string Message)> IssueAsync(Employee employee)
    {
        var settings = await db.OtpSettings.AsNoTracking().FirstOrDefaultAsync() ?? new OtpSettings();

        if (!settings.Enabled)
            return (false, "ورود با OTP در تنظیمات سامانه غیرفعال است.");

        var length = Math.Clamp(settings.Length, 4, 9);
        var min = (int)Math.Pow(10, length - 1);
        var max = (int)Math.Pow(10, length);
        var code = RandomNumberGenerator.GetInt32(min, max).ToString($"D{length}");

        var challenge = new OtpChallenge
        {
            EmployeeId = employee.Id,
            PersonnelNumber = employee.PersonnelNumber,
            Mobile = employee.Mobile,
            CodeHash = PasswordHasher.Hash(code),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddSeconds(Math.Max(1, settings.ValiditySeconds)),
            AttemptCount = 0,
            IsConsumed = false
        };

        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync();

        var smsResult = await sms.SendOtpAsync(employee.Mobile, code);

        challenge.SmsSent = smsResult.Success;
        challenge.SmsResponse = smsResult.Response.Length > 2000
            ? smsResult.Response[..2000]
            : smsResult.Response;

        await db.SaveChangesAsync();

        return smsResult.Success
            ? (true, "کد یکبارمصرف ارسال شد.")
            : (false, "ارسال پیامک ناموفق بود: " + smsResult.Response);
    }

    public async Task<bool> VerifyAsync(string personnelNumber, string code)
    {
        var settings = await db.OtpSettings.AsNoTracking().FirstOrDefaultAsync() ?? new OtpSettings();

        if (!settings.Enabled)
            return false;

        var challenge = await db.OtpChallenges
            .Where(x => x.PersonnelNumber == personnelNumber && !x.IsConsumed)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        if (challenge is null ||
            challenge.ExpiresAt <= DateTime.UtcNow ||
            challenge.AttemptCount >= Math.Max(1, settings.MaxAttempts))
            return false;

        challenge.AttemptCount++;
        var valid = PasswordHasher.Verify(code, challenge.CodeHash);

        if (valid)
            challenge.IsConsumed = true;

        await db.SaveChangesAsync();
        return valid;
    }
}
