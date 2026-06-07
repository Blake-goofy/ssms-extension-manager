using System.Text.RegularExpressions;

namespace SsmsExtensionManager.Core.Services;

public sealed record GitHubRepository(string Owner, string Name)
{
    private static readonly Regex GitHubUrlPattern = new(
        @"^https?://github\.com/(?<owner>[^/\s]+)/(?<repo>[^/\s#?]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GitHubShortPattern = new(
        @"^(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string value, out GitHubRepository repository)
    {
        repository = default!;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();
        Match match = GitHubUrlPattern.Match(value);
        if (!match.Success)
        {
            match = GitHubShortPattern.Match(value);
        }

        if (!match.Success)
        {
            return false;
        }

        string owner = match.Groups["owner"].Value;
        string repo = match.Groups["repo"].Value;
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repo = repo[..^4];
        }

        repository = new GitHubRepository(owner, repo);
        return true;
    }

    public Uri ApiLatestReleaseUri => new($"https://api.github.com/repos/{Owner}/{Name}/releases/latest");

    public Uri RepositoryUri => new($"https://github.com/{Owner}/{Name}");

    public override string ToString() => $"{Owner}/{Name}";
}
