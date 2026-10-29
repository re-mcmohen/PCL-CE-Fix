using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.App;

namespace PCL.Core.Test;

[TestClass]
public class ReleaseUpdateClientTest
{
    private sealed class Handler(Func<string, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public ConcurrentDictionary<string, int> Calls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var repo = request.RequestUri!.AbsolutePath.Contains("/re-mcmohen/")
                ? ReleaseUpdatePolicy.ForkRepository : ReleaseUpdatePolicy.UpstreamRepository;
            return Task.FromResult(respond(repo, Calls.AddOrUpdate(repo, 1, (_, count) => count + 1)));
        }
    }

    private static HttpResponseMessage Success(string repo)
    {
        var fork = repo == ReleaseUpdatePolicy.ForkRepository;
        var tag = fork ? "fix-test" : "v2.15.1";
        var file = fork ? "PCL_CE_Fix_x64.exe" : "PCL2_CE_Release_x64.exe";
        var json = new JsonObject
        {
            ["tag_name"] = tag, ["target_commitish"] = new string('a', 40),
            ["draft"] = false, ["prerelease"] = false,
            ["assets"] = new JsonArray(new JsonObject
            {
                ["name"] = file, ["digest"] = "sha256:" + new string('b', 64),
                ["browser_download_url"] = $"https://github.com/{repo}/releases/download/{tag}/{file}"
            })
        };
        return new(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
    }

    private static ReleaseUpdateClient Client(HttpClient http, Func<DateTimeOffset>? clock = null) =>
        new(http, false, clock, (_, _) => Task.CompletedTask);

    [TestMethod]
    public async Task AcceleratorFailureFallsBackToVerifiedDirectRelease()
    {
        var handler = new Handler((repo, count) => count == 1
            ? new(HttpStatusCode.BadGateway) : Success(repo));
        using var http = new HttpClient(handler);
        var client = new ReleaseUpdateClient(http, false, delay: (_, _) => Task.CompletedTask,
            routes: url => GitHubUpdateRoutes.Get(url, UpdateNetworkMode.Accelerated, PCL.Core.App.Region.RegionClassification.Other));
        var results = await client.CheckAsync();
        foreach (var result in results)
        {
            Assert.IsNotNull(result.Release);
            Assert.AreEqual(2, handler.Calls[result.Repository]);
            Assert.IsTrue(result.Release.DownloadUrl.StartsWith("https://github.com/"));
        }
    }

    [TestMethod]
    public async Task SuccessfulResultsAreCachedForFiveMinutesAndForceRefreshes()
    {
        var now = DateTimeOffset.UtcNow;
        var handler = new Handler((repo, _) => Success(repo));
        using var http = new HttpClient(handler);
        var client = Client(http, () => now);
        await client.CheckAsync();
        now = now.AddMinutes(4);
        await client.CheckAsync();
        Assert.AreEqual(1, handler.Calls[ReleaseUpdatePolicy.ForkRepository]);
        await client.CheckAsync(force: true);
        Assert.AreEqual(2, handler.Calls[ReleaseUpdatePolicy.ForkRepository]);
        now = now.AddMinutes(6);
        await client.CheckAsync();
        Assert.AreEqual(3, handler.Calls[ReleaseUpdatePolicy.ForkRepository]);
    }

    [TestMethod]
    public async Task TransientServerFailureRetriesWithinTheSameCheck()
    {
        var handler = new Handler((repo, count) => repo == ReleaseUpdatePolicy.ForkRepository && count < 3
            ? new(HttpStatusCode.ServiceUnavailable) : Success(repo));
        using var http = new HttpClient(handler);
        var results = await Client(http).CheckAsync();
        Assert.IsNotNull(results[0].Release);
        Assert.IsNotNull(results[1].Release);
        Assert.AreEqual(3, handler.Calls[ReleaseUpdatePolicy.ForkRepository]);
        Assert.AreEqual(1, handler.Calls[ReleaseUpdatePolicy.UpstreamRepository]);
    }

    [TestMethod]
    public async Task FailedSourceExpiresSoonerWithoutInvalidatingHealthySource()
    {
        var now = DateTimeOffset.UtcNow;
        var failing = true;
        var handler = new Handler((repo, _) => repo == ReleaseUpdatePolicy.ForkRepository && failing
            ? new(HttpStatusCode.ServiceUnavailable) : Success(repo));
        using var http = new HttpClient(handler);
        var client = Client(http, () => now);
        var first = await client.CheckAsync();
        Assert.AreEqual(UpdateCheckFailure.Server, first[0].Failure);
        Assert.IsNotNull(first[1].Release);
        failing = false;
        now = now.AddSeconds(10);
        await client.CheckAsync();
        Assert.AreEqual(3, handler.Calls[ReleaseUpdatePolicy.ForkRepository]);
        now = now.AddSeconds(6);
        var recovered = await client.CheckAsync();
        Assert.IsNotNull(recovered[0].Release);
        Assert.AreEqual(4, handler.Calls[ReleaseUpdatePolicy.ForkRepository]);
        Assert.AreEqual(1, handler.Calls[ReleaseUpdatePolicy.UpstreamRepository]);
    }

    [TestMethod]
    [DataRow(403)]
    [DataRow(429)]
    public async Task RateLimitHonorsRetryAfterEvenOnManualRefresh(int status)
    {
        var now = DateTimeOffset.UtcNow;
        var handler = new Handler((repo, _) =>
        {
            if (repo != ReleaseUpdatePolicy.ForkRepository) return Success(repo);
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            response.Headers.Add("X-RateLimit-Remaining", "0");
            return response;
        });
        using var http = new HttpClient(handler);
        var client = Client(http, () => now);
        var result = await client.CheckAsync();
        Assert.AreEqual(UpdateCheckFailure.RateLimited, result[0].Failure);
        Assert.AreEqual(now.AddMinutes(2), result[0].RetryAt);
        now = now.AddMinutes(1);
        await client.CheckAsync(force: true);
        Assert.AreEqual(1, handler.Calls[ReleaseUpdatePolicy.ForkRepository]);
        now = now.AddMinutes(2);
        await client.CheckAsync();
        Assert.AreEqual(2, handler.Calls[ReleaseUpdatePolicy.ForkRepository]);
    }

    [TestMethod]
    public async Task PrimaryRateLimitUsesResetHeader()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1000);
        var handler = new Handler((repo, _) =>
        {
            if (repo != ReleaseUpdatePolicy.ForkRepository) return Success(repo);
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Reset", "1300");
            return response;
        });
        using var http = new HttpClient(handler);
        var results = await Client(http, () => now).CheckAsync();
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1300), results[0].RetryAt);
        Assert.AreEqual(1, handler.Calls[ReleaseUpdatePolicy.ForkRepository]);
    }

    [TestMethod]
    public async Task PermissionDenialIsNotReportedAsRateLimitAndDoesNotRetry()
    {
        var handler = new Handler((repo, _) => repo == ReleaseUpdatePolicy.ForkRepository
            ? new(HttpStatusCode.Forbidden) : Success(repo));
        using var http = new HttpClient(handler);
        var results = await Client(http).CheckAsync();
        Assert.AreEqual(UpdateCheckFailure.Forbidden, results[0].Failure);
        StringAssert.Contains(results[0].Error!, "403");
        Assert.AreEqual(1, handler.Calls[ReleaseUpdatePolicy.ForkRepository]);
    }

    [TestMethod]
    public async Task DnsErrorsAreClassifiedAndRetriesAreBounded()
    {
        var handler = new Handler((repo, _) => repo == ReleaseUpdatePolicy.ForkRepository
            ? throw new HttpRequestException(HttpRequestError.NameResolutionError, "DNS failed") : Success(repo));
        using var http = new HttpClient(handler);
        var results = await Client(http).CheckAsync();
        Assert.AreEqual(UpdateCheckFailure.Dns, results[0].Failure);
        StringAssert.Contains(results[0].Error!, "DNS");
        Assert.AreEqual(3, handler.Calls[ReleaseUpdatePolicy.ForkRepository]);
    }

    [TestMethod]
    public async Task TimeoutIsClassifiedSeparately()
    {
        var handler = new Handler((repo, _) => repo == ReleaseUpdatePolicy.ForkRepository
            ? throw new TaskCanceledException("timeout") : Success(repo));
        using var http = new HttpClient(handler);
        var results = await Client(http).CheckAsync();
        Assert.AreEqual(UpdateCheckFailure.Timeout, results[0].Failure);
        Assert.AreEqual(3, handler.Calls[ReleaseUpdatePolicy.ForkRepository]);
    }

    [TestMethod]
    public async Task InvalidReleaseIsNotRetriedAsNetworkFailure()
    {
        var handler = new Handler((repo, _) => repo == ReleaseUpdatePolicy.ForkRepository
            ? new(HttpStatusCode.OK) { Content = new StringContent("invalid JSON") } : Success(repo));
        using var http = new HttpClient(handler);
        var results = await Client(http).CheckAsync();
        Assert.AreEqual(UpdateCheckFailure.InvalidRelease, results[0].Failure);
        Assert.AreEqual(1, handler.Calls[ReleaseUpdatePolicy.ForkRepository]);
    }

    [TestMethod]
    public async Task PreCanceledCheckDoesNotPreventLaterChecks()
    {
        var handler = new Handler((repo, _) => Success(repo));
        using var http = new HttpClient(handler);
        var client = Client(http);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => client.CheckAsync(token: cancellation.Token));
        var results = await client.CheckAsync();
        Assert.IsNotNull(results[0].Release);
        Assert.IsNotNull(results[1].Release);
    }
}
