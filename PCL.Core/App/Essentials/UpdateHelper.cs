using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace PCL.Core.App.Essentials;

public static class UpdateHelper
{
    public static string ComputeSha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Stage beside the target and atomically replace it, retrying temporary file locks.</summary>
    public static Exception? Replace(string source, string target, string? expectedSha256 = null,
        TimeSpan? retryTimeout = null)
    {
        string? staged = null;
        string? backup = null;
        try
        {
            source = Path.GetFullPath(source);
            target = Path.GetFullPath(target);
            if (string.Equals(source, target, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("更新来源和目标不能是同一个文件。");
            var expected = expectedSha256 ?? ComputeSha256(source);
            if (!string.Equals(ComputeSha256(source), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新来源文件的 SHA-256 校验失败。");
            var id = Guid.NewGuid().ToString("N");
            staged = target + ".update." + id + ".tmp";
            backup = target + ".update." + id + ".bak";
            File.Copy(source, staged);
            if (!string.Equals(ComputeSha256(staged), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新暂存文件的 SHA-256 校验失败。");

            var timer = Stopwatch.StartNew();
            var timeout = retryTimeout ?? TimeSpan.FromSeconds(15);
            while (true)
            {
                try
                {
                    if (File.Exists(target)) File.Replace(staged, target, backup, ignoreMetadataErrors: true);
                    else File.Move(staged, target);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (timer.Elapsed >= timeout) throw;
                    Thread.Sleep(150);
                }
            }
            if (!string.Equals(ComputeSha256(target), expected, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(backup)) File.Copy(backup, target, overwrite: true);
                throw new InvalidDataException("安装后的 SHA-256 校验失败，已尝试恢复旧版本。");
            }
            // A receipt is written only after installation was verified. Failed-update backups
            // never receive one, so startup cleanup cannot destroy recovery files.
            var receipt = target + ".update." + id + ".done";
            File.WriteAllText(receipt, expected);
            for (var attempt = 0; attempt < 10 && !TryDelete(backup); attempt++) Thread.Sleep(150);
            if (!File.Exists(backup)) TryDelete(receipt);
            return null;
        }
        catch (Exception ex) { return ex; }
        finally
        {
            if (staged is not null) TryDelete(staged);
            // Keep a backup on failure rather than risk deleting the only recoverable copy.
        }
    }

    public static void CleanupCompletedUpdates(string target)
    {
        target = Path.GetFullPath(target);
        var directory = Path.GetDirectoryName(target)!;
        var prefix = Path.GetFileName(target) + ".update.";
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, prefix + "*"))
            {
                var name = Path.GetFileName(path);
                var suffix = name[prefix.Length..];
                var dot = suffix.IndexOf('.');
                if (dot != 32 || !Guid.TryParseExact(suffix[..dot], "N", out _)) continue;
                var extension = suffix[dot..];
                if (extension == ".tmp" && File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1))
                    TryDelete(path);
                if (extension != ".done") continue;
                var hash = File.ReadAllText(path).Trim();
                if (hash.Length != 64 || !System.Linq.Enumerable.All(hash, Uri.IsHexDigit)) continue;
                var stem = path[..^5];
                if (TryDelete(stem + ".bak") && TryDelete(stem + ".tmp")) TryDelete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static bool TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
