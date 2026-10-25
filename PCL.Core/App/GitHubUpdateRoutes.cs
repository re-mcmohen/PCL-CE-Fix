using System;
using System.Collections.Generic;
using PCL.Core.App.Region;

namespace PCL.Core.App;

public enum UpdateNetworkMode { Auto, Accelerated, Direct }

public static class GitHubUpdateRoutes
{
    public const string Accelerator = "https://gh-proxy.com/";

    public static IReadOnlyList<string> Get(string url, UpdateNetworkMode mode, RegionClassification region)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            (uri.Host != "github.com" && uri.Host != "api.github.com") || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Expected a canonical HTTPS GitHub URL", nameof(url));
        var accelerated = mode == UpdateNetworkMode.Accelerated ||
                          mode == UpdateNetworkMode.Auto && region == RegionClassification.Mainland;
        return accelerated ? new[] { Accelerator + url, url } : new[] { url };
    }
}
