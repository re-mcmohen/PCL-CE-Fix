using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.App;

public enum UpdateCheckFailure
{
    None, RateLimited, Forbidden, NotFound, Network, Dns, Timeout, Server, InvalidRelease, Unexpected
}

public sealed record RepositoryUpdateResult(string Repository, ReleaseUpdateCandidate? Release, string? Error,
    UpdateCheckFailure Failure = UpdateCheckFailure.None, DateTimeOffset? RetryAt = null);

/// <summary>Independent repository caches and bounded retries for transient failures.</summary>
public sealed class ReleaseUpdateClient
{
    private sealed record CacheEntry(RepositoryUpdateResult Result, DateTimeOffset ExpiresAt);
    private readonly HttpClient _client;
    private readonly bool _arm64;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<string, Exception>? _log;
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private readonly Dictionary<string, CacheEntry> _cache = new();
    private RepositoryUpdateResult[] _results = [];

    public ReleaseUpdateClient(HttpClient client, bool arm64, Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Action<string, Exception>? log = null)
    {
        _client = client;
        _arm64 = arm64;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        _log = log;
    }

    public RepositoryUpdateResult[] Results => Volatile.Read(ref _results);

    public async Task<RepositoryUpdateResult[]> CheckAsync(bool force = false, CancellationToken token = default)
    {
        await _checkLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var entries = await Task.WhenAll(CheckOneAsync(ReleaseUpdatePolicy.ForkRepository, force, token),
                CheckOneAsync(ReleaseUpdatePolicy.UpstreamRepository, force, token)).ConfigureAwait(false);
            foreach (var entry in entries) _cache[entry.Result.Repository] = entry;
            var results = entries.Select(entry => entry.Result).ToArray();
            Volatile.Write(ref _results, results);
            return results;
        }
        finally { _checkLock.Release(); }
    }

    private async Task<CacheEntry> CheckOneAsync(string repository, bool force, CancellationToken token)
    {
        if (_cache.TryGetValue(repository, out var cached) && _clock() < cached.ExpiresAt &&
            (!force || cached.Result.Failure == UpdateCheckFailure.RateLimited)) return cached;
        var result = await FetchAsync(repository, token).ConfigureAwait(false);
        var expires = result.Release is not null ? _clock().AddMinutes(5)
            : result.RetryAt ?? _clock().AddSeconds(15);
        return new(result, expires);
    }

    private async Task<RepositoryUpdateResult> FetchAsync(string repository, CancellationToken token)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.github.com/repos/{repository}/releases/latest");
                request.Headers.UserAgent.ParseAdd("PCL-CE-Fix-Updater/1.0");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var response = await _client.SendAsync(request, token).ConfigureAwait(false);
                var limited = response.StatusCode == HttpStatusCode.TooManyRequests ||
                    response.StatusCode == HttpStatusCode.Forbidden &&
                    (response.Headers.RetryAfter is not null ||
                     response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0"));
                if (limited)
                {
                    var retryAt = GetRetryAt(response);
                    return Failure(repository, UpdateCheckFailure.RateLimited,
                        $"GitHub API 限流（HTTP {(int)response.StatusCode}），请在 {retryAt.ToLocalTime():HH:mm:ss} 后重试或打开发布页。",
                        new HttpRequestException("GitHub rate limit", null, response.StatusCode), retryAt);
                }
                if (!response.IsSuccessStatusCode)
                {
                    var transient = (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout;
                    if (transient && attempt < 3)
                    {
                        await BackoffAsync(attempt, token).ConfigureAwait(false);
                        continue;
                    }
                    var kind = response.StatusCode switch
                    {
                        HttpStatusCode.Forbidden => UpdateCheckFailure.Forbidden,
                        HttpStatusCode.NotFound => UpdateCheckFailure.NotFound,
                        _ => UpdateCheckFailure.Server
                    };
                    return Failure(repository, kind, $"GitHub 请求失败（HTTP {(int)response.StatusCode}），请稍后重试或打开发布页。",
                        new HttpRequestException(response.ReasonPhrase, null, response.StatusCode));
                }
                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false))
                           ?? throw new FormatException("Empty release response");
                return new(repository, ReleaseUpdatePolicy.Parse(json, repository, _arm64), null);
            }
            catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
            {
                if (attempt < 3) { await BackoffAsync(attempt, token).ConfigureAwait(false); continue; }
                return Failure(repository, UpdateCheckFailure.Timeout, "连接 GitHub 超时，已自动重试，请稍后重试。", ex);
            }
            catch (HttpRequestException ex)
            {
                if (attempt < 3) { await BackoffAsync(attempt, token).ConfigureAwait(false); continue; }
                var dns = ex.HttpRequestError == HttpRequestError.NameResolutionError;
                return Failure(repository, dns ? UpdateCheckFailure.Dns : UpdateCheckFailure.Network,
                    dns ? "GitHub 域名解析失败（DNS），请检查网络后重试。" : "无法连接 GitHub，已自动重试，请检查网络后重试。", ex);
            }
            catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException)
            {
                return Failure(repository, UpdateCheckFailure.InvalidRelease, "发布信息格式异常或缺少可验证的安装文件，请打开发布页查看。", ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Failure(repository, UpdateCheckFailure.Unexpected, "读取更新信息失败，详细原因已记录到日志。", ex);
            }
        }
        throw new InvalidOperationException("Retry loop did not produce a result");
    }

    private Task BackoffAsync(int attempt, CancellationToken token) =>
        _delay(TimeSpan.FromMilliseconds(250 * attempt), token);

    private DateTimeOffset GetRetryAt(HttpResponseMessage response)
    {
        var now = _clock();
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero) return now + delta;
        if (response.Headers.RetryAfter?.Date is { } date && date > now) return date;
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resets) &&
            long.TryParse(resets.FirstOrDefault(), out var seconds))
        {
            try
            {
                var reset = DateTimeOffset.FromUnixTimeSeconds(seconds);
                if (reset > now) return reset;
            }
            catch (ArgumentOutOfRangeException) { }
        }
        return now.AddMinutes(1);
    }

    private RepositoryUpdateResult Failure(string repository, UpdateCheckFailure kind, string message,
        Exception exception, DateTimeOffset? retryAt = null)
    {
        _log?.Invoke(repository, exception);
        return new(repository, null, message, kind, retryAt);
    }
}
