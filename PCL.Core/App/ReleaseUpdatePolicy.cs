using System;
using System.Linq;
using System.Text.Json.Nodes;
using PCL.Core.Utils;

namespace PCL.Core.App;

public sealed record ReleaseUpdateCandidate(string Repository, string Tag, string Commit,
    string DownloadUrl, string Sha256, string Notes, string PageUrl)
{
    public bool IsFork => Repository == ReleaseUpdatePolicy.ForkRepository;

    public bool IsNewer(string currentVersion, string currentCommit, string currentTag = "") => IsFork
        ? (string.IsNullOrEmpty(currentTag)
            ? !string.Equals(Commit, currentCommit, StringComparison.OrdinalIgnoreCase)
            : Tag != currentTag)
        : SemVer.TryParse(Tag.TrimStart('v'), out var remote) &&
          SemVer.TryParse(currentVersion, out var current) && remote! > current!;
}

public static class ReleaseUpdatePolicy
{
    // Stamped by the sync workflow before tests and compilation.
    public const string CurrentReleaseTag = "fix-1a7653127d57-233bf6004395";
    public const string ForkRepository = "re-mcmohen/PCL-CE-Fix";
    public const string UpstreamRepository = "PCL-Community/PCL-CE";

    public static ReleaseUpdateCandidate Parse(JsonNode release, string repository, bool arm64)
    {
        if (repository != ForkRepository && repository != UpstreamRepository)
            throw new ArgumentException("Unknown update repository", nameof(repository));
        if (release["draft"]?.GetValue<bool>() != false || release["prerelease"]?.GetValue<bool>() != false)
            throw new FormatException("Expected a published stable release");
        var tag = release["tag_name"]?.GetValue<string>() ?? throw new FormatException("Missing release tag");
        var arch = arm64 ? "ARM64" : "x64";
        var file = repository == ForkRepository ? $"PCL_CE_Fix_{arch}.exe" : $"PCL2_CE_Release_{arch}.exe";
        var asset = release["assets"]?.AsArray().FirstOrDefault(a => a?["name"]?.GetValue<string>() == file)
                    ?? throw new FormatException($"No executable for {arch}");
        var digest = asset["digest"]?.GetValue<string>() ?? "";
        if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 ||
            !digest[7..].All(Uri.IsHexDigit))
            throw new FormatException("Release executable has no valid SHA-256 digest");
        var url = asset["browser_download_url"]?.GetValue<string>() ?? "";
        var expected = $"https://github.com/{repository}/releases/download/{tag}/{file}";
        if (!string.Equals(url, expected, StringComparison.Ordinal))
            throw new FormatException("Unexpected executable download URL");
        var commit = release["target_commitish"]?.GetValue<string>() ?? "";
        if (repository == ForkRepository && (commit.Length != 40 || !commit.All(Uri.IsHexDigit)))
            throw new FormatException("Fork release must identify its source commit");
        return new(repository, tag, commit, url, digest[7..].ToLowerInvariant(),
            release["body"]?.GetValue<string>() ?? "暂无更新说明。",
            $"https://github.com/{repository}/releases/tag/{tag}");
    }
}
