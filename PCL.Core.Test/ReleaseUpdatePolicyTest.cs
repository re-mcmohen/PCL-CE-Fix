using System;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App;

namespace PCL.Core.Test;

[TestClass]
public class ReleaseUpdatePolicyTest
{
    [TestMethod]
    public void ReleaseIdentityIsEmbeddedByBuild()
    {
        var expected = Environment.GetEnvironmentVariable("PCL_EXPECTED_RELEASE_TAG");
        if (string.IsNullOrEmpty(expected))
        {
            Assert.Inconclusive("Set PCL_EXPECTED_RELEASE_TAG and build with -p:PCLCEFixReleaseTag to verify CI metadata.");
            return;
        }
        Assert.AreEqual(expected, ReleaseUpdatePolicy.CurrentReleaseTag);
    }

    private static JsonObject Release(string repo, string tag, bool arm64 = false)
    {
        var file = repo == ReleaseUpdatePolicy.ForkRepository
            ? $"PCL_CE_Fix_{(arm64 ? "ARM64" : "x64")}.exe"
            : $"PCL2_CE_Release_{(arm64 ? "ARM64" : "x64")}.exe";
        return new JsonObject
        {
            ["draft"] = false, ["prerelease"] = false, ["tag_name"] = tag,
            ["target_commitish"] = new string('a', 40), ["body"] = "notes",
            ["assets"] = new JsonArray(new JsonObject
            {
                ["name"] = file, ["digest"] = "sha256:" + new string('b', 64),
                ["browser_download_url"] = $"https://github.com/{repo}/releases/download/{tag}/{file}"
            })
        };
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ForkChoosesExactArchitecture(bool arm64)
    {
        var release = ReleaseUpdatePolicy.Parse(Release(ReleaseUpdatePolicy.ForkRepository, "fix-one", arm64),
            ReleaseUpdatePolicy.ForkRepository, arm64);
        StringAssert.EndsWith(release.DownloadUrl, arm64 ? "_ARM64.exe" : "_x64.exe");
        Assert.IsFalse(release.IsNewer("2.15.1", release.Commit));
        Assert.IsTrue(release.IsNewer("2.15.1", new string('c', 40)));
        Assert.IsFalse(release.IsNewer("2.15.1", "", "fix-one"));
        Assert.IsTrue(release.IsNewer("2.15.1", release.Commit, "fix-older"));
    }

    [TestMethod]
    public void ForkUpdateIsDetectedEvenWhenBaseVersionDoesNotChange()
    {
        var release = ReleaseUpdatePolicy.Parse(Release(ReleaseUpdatePolicy.ForkRepository, "fix-new"),
            ReleaseUpdatePolicy.ForkRepository, false);
        Assert.IsTrue(release.IsNewer("2.15.1", new string('c', 40)));
    }

    [TestMethod]
    public void UpstreamUsesSemanticVersionInsteadOfDevBranchName()
    {
        var json = Release(ReleaseUpdatePolicy.UpstreamRepository, "v2.15.1");
        json["target_commitish"] = "dev";
        var release = ReleaseUpdatePolicy.Parse(json, ReleaseUpdatePolicy.UpstreamRepository, false);
        Assert.IsTrue(release.IsNewer("2.15.0", ""));
        Assert.IsFalse(release.IsNewer("2.15.1", ""));
        Assert.IsFalse(release.IsNewer("2.16.0", ""));
    }

    [TestMethod]
    public void MissingArchitectureIsRejected()
    {
        Assert.ThrowsExactly<FormatException>(() => ReleaseUpdatePolicy.Parse(
            Release(ReleaseUpdatePolicy.ForkRepository, "fix-one"), ReleaseUpdatePolicy.ForkRepository, true));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("sha256:invalid")]
    [DataRow("sha512:bbbb")]
    public void MissingOrInvalidChecksumIsRejected(string digest)
    {
        var json = Release(ReleaseUpdatePolicy.ForkRepository, "fix-one");
        json["assets"]![0]!["digest"] = digest;
        Assert.ThrowsExactly<FormatException>(() => ReleaseUpdatePolicy.Parse(json, ReleaseUpdatePolicy.ForkRepository, false));
    }

    [TestMethod]
    public void DownloadFromDifferentRepositoryIsRejected()
    {
        var json = Release(ReleaseUpdatePolicy.ForkRepository, "fix-one");
        json["assets"]![0]!["browser_download_url"] = "https://github.com/other/repository/file.exe";
        Assert.ThrowsExactly<FormatException>(() => ReleaseUpdatePolicy.Parse(json, ReleaseUpdatePolicy.ForkRepository, false));
    }

    [TestMethod]
    public void DraftAndPrereleaseAreRejected()
    {
        var json = Release(ReleaseUpdatePolicy.ForkRepository, "fix-one");
        json["draft"] = true;
        Assert.ThrowsExactly<FormatException>(() => ReleaseUpdatePolicy.Parse(json, ReleaseUpdatePolicy.ForkRepository, false));
        json["draft"] = false;
        json["prerelease"] = true;
        Assert.ThrowsExactly<FormatException>(() => ReleaseUpdatePolicy.Parse(json, ReleaseUpdatePolicy.ForkRepository, false));
    }
}
