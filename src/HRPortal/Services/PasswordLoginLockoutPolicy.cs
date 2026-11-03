namespace HRPortal.Services;

/// <summary>
/// Central lockout thresholds for password-based employee sign-in.
/// Persistent state is stored on Employee; this class owns the policy boundaries.
/// </summary>
public static class PasswordLoginLockoutPolicy
{
    public const int MaximumFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public static bool IsLocked(DateTime? lockedUntilUtc, DateTime nowUtc) =>
        lockedUntilUtc.HasValue && lockedUntilUtc.Value > nowUtc;

    public static int GetRemainingSeconds(DateTime? lockedUntilUtc, DateTime nowUtc)
    {
        if (!IsLocked(lockedUntilUtc, nowUtc)) return 0;
        return Math.Max(0, (int)Math.Ceiling((lockedUntilUtc!.Value - nowUtc).TotalSeconds));
    }
}
