using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App;
using PCL.Core.App.Region;

namespace PCL.Core.Test;

[TestClass]
public class GitHubUpdateRoutesTest
{
    private const string Url = "https://api.github.com/repos/re-mcmohen/PCL-CE-Fix/releases/latest";

    [TestMethod]
    public void MainlandAutoPrefersAccelerationAndKeepsDirectFallback()
    {
        var routes = GitHubUpdateRoutes.Get(Url, UpdateNetworkMode.Auto, RegionClassification.Mainland);
        CollectionAssert.AreEqual(new[] { GitHubUpdateRoutes.Accelerator + Url, Url }, new System.Collections.Generic.List<string>(routes));
    }

    [TestMethod]
    public void OtherAndUnknownRegionsAutoUseDirect()
    {
        Assert.AreEqual(Url, GitHubUpdateRoutes.Get(Url, UpdateNetworkMode.Auto, RegionClassification.Other)[0]);
        Assert.AreEqual(1, GitHubUpdateRoutes.Get(Url, UpdateNetworkMode.Auto, RegionClassification.Unknown).Count);
    }

    [TestMethod]
    public void ExplicitModeOverridesRegionForAssetsAndPages()
    {
        const string asset = "https://github.com/re-mcmohen/PCL-CE-Fix/releases/download/fix-test/PCL_CE_Fix_x64.exe";
        Assert.AreEqual(asset, GitHubUpdateRoutes.Get(asset, UpdateNetworkMode.Direct, RegionClassification.Mainland)[0]);
        Assert.AreEqual(GitHubUpdateRoutes.Accelerator + asset,
            GitHubUpdateRoutes.Get(asset, UpdateNetworkMode.Accelerated, RegionClassification.Other)[0]);
    }

    [TestMethod]
    public void RejectsNonGitHubAndInsecureUrls()
    {
        foreach (var url in new[] { "http://github.com/a", "https://github.com.evil.test/a", "https://user@github.com/a", "https://api.github.com:8443/a" })
            Assert.ThrowsExactly<ArgumentException>(() => GitHubUpdateRoutes.Get(url, UpdateNetworkMode.Auto, RegionClassification.Mainland));
    }
}
