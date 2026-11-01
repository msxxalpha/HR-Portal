using System.Reflection;

namespace HRPortal;

/// <summary>
/// Human-readable build sequence plus the exact Git commit embedded by MSBuild.
/// </summary>
public static class BuildInfo
{
    public const string Number = "308";

    public static string Commit
    {
        get
        {
            var informationalVersion = typeof(BuildInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(informationalVersion))
                return "نامشخص";

            var separator = informationalVersion.LastIndexOf('+');
            if (separator < 0 || separator == informationalVersion.Length - 1)
                return "نامشخص";

            var commit = informationalVersion[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(commit) || commit.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                return "نامشخص";

            return commit.Length > 12 ? commit[..12] : commit;
        }
    }
}
