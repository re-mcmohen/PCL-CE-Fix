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
            TryDelete(backup);
            return null;
        }
        catch (Exception ex) { return ex; }
        finally
        {
            if (staged is not null) TryDelete(staged);
            // Keep a backup on failure rather than risk deleting the only recoverable copy.
        }
    }

    public static bool TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
