namespace HRPortal;

/// <summary>
/// Human-readable build sequence plus the exact Git commit captured at compile time.
/// </summary>
public static partial class BuildInfo
{
    public const string Number = "308";

    public static string Commit
    {
        get
        {
            var commit = "unknown";
            SetGitCommit(ref commit);
            if (string.IsNullOrWhiteSpace(commit) ||
                commit.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                return "نامشخص";

            return commit.Length > 12 ? commit[..12] : commit;
        }
    }

    // In Visual Studio design-time builds the generated implementation is absent;
    // the partial call is erased and the property safely falls back to "نامشخص".
    static partial void SetGitCommit(ref string commit);
}
