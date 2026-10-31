using System.IO;
using System.Net.Http;
using PCL.Core.App;
using PCL.Core.App.Region;
using PCL.Core.UI;
using PCL.Core.Utils.OS;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

/// <summary>Check both distributions independently; installation always requires an explicit choice.</summary>
public static class ForkUpdateService
{
    private static readonly ReleaseUpdateClient Client = new(
        new HttpClient { Timeout = TimeSpan.FromSeconds(8) },
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
        System.Runtime.InteropServices.Architecture.Arm64,
        log: (repository, error) => ModBase.Log(error, $"[Update] {repository} 检查失败"), routes: GetRoutes);
    private static readonly SemaphoreSlim InstallLock = new(1, 1);
    private static readonly object RefreshLock = new();
    private static Task _backgroundRefresh = Task.CompletedTask;
    public static RepositoryUpdateResult[] Results => Client.Results;
    private static readonly RegionClassification DeviceRegion = RegionPolicy.Evaluate(RegionPolicy.ReadWindowsSignals()).Classification;
    public static IReadOnlyList<string> GetRoutes(string url) =>
        GitHubUpdateRoutes.Get(url, Config.Update.NetworkMode, DeviceRegion);
    public static bool Installing { get; private set; }

    public static bool IsNewer(ReleaseUpdateCandidate release) => release.IsNewer(ModBase.versionBaseName,
        Secrets.CommitHash, ReleaseUpdatePolicy.CurrentReleaseTag, ReleaseUpdatePolicy.CurrentBuildTime);

    public static Task<RepositoryUpdateResult[]> CheckAsync(bool force = false) => Client.CheckAsync(force);

    public static void RefreshInBackground()
    {
        lock (RefreshLock)
        {
            if (!_backgroundRefresh.IsCompleted) return;
            _backgroundRefresh = Task.Run(async () =>
            {
                try { await CheckAsync().ConfigureAwait(false); }
                catch (Exception ex) { ModBase.Log(ex, "[Update] 后台检查失败"); }
            });
        }
    }

    public static void NotifyInBackground() => _ = Task.Run(async () =>
    {
        try { await CheckAndNotifyAsync().ConfigureAwait(false); }
        catch (Exception ex) { ModBase.Log(ex, "[Update] 后台更新通知失败"); }
    });

    public static async Task CheckAndNotifyAsync()
    {
        var results = await CheckAsync().ConfigureAwait(false);
        var available = results.Where(r => r.Release is not null && IsNewer(r.Release)).ToArray();
        if (available.Length == 0) return;
        ModBase.RunInUi(() =>
        {
            var names = string.Join("\n", available.Select(r =>
                $"{(r.Release!.IsFork ? "PCL_CE_Fix（保留补丁）" : "上游 PCL CE（原版）")}：{r.Release.Tag}"));
            if (ModMain.MyMsgBox($"检测到可用更新：\n{names}\n\n请在更新页面选择来源，不会自动切换版本。",
                    "启动器更新", "选择更新来源", "稍后") == 1)
                ModMain.frmMain.PageChange(FormMain.PageType.Setup, FormMain.PageSubType.SetupUpdate);
        });
    }

    public static async Task InstallAsync(ReleaseUpdateCandidate release)
    {
        if (!await InstallLock.WaitAsync(0)) return;
        string? output = null;
        try
        {
            if (UpdateManager.isUpdateWaitingRestart ||
                UpdateManager.updateLoader?.State == ModBase.LoadState.Loading)
                throw new InvalidOperationException("已有更新任务，请先完成该任务。");
            Installing = true;
            var source = release.IsFork ? "PCL_CE_Fix（保留区域限制补丁）" : "上游 PCL CE（原版，可能恢复区域限制）";
            if (ModMain.MyMsgBox($"将下载并安装：{source}\n版本：{release.Tag}\n\n安装后将重启启动器，是否继续？",
                    "确认更新来源", "安装并重启", "取消", isWarn: !release.IsFork) != 1) return;
            output = Path.Combine(Basics.ExecutableDirectory, "PCL", "Updates", Guid.NewGuid().ToString("N"), "Launcher.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            // Reuse the existing taskbar download UI, then verify the exact asset selected by the player.
            var download = new ModLoader.LoaderCombo<int>($"更新 {source}",
                new List<ModLoader.LoaderBase>
                {
                    new LoaderDownload("下载启动器", new List<DownloadFile>
                        { new(GetRoutes(release.DownloadUrl).ToArray(), output, new ModBase.FileChecker(hash: release.Sha256)) })
                });
            UpdateManager.updateLoader = null;
            download.Start();
            ModLoader.LoaderTaskbarAdd(download);
            ModMain.frmMain.BtnExtraDownload.ShowRefresh();
            await Task.Run(() => download.WaitForExit());
            var hash = ModBase.GetFileSHA256(output);
            if (!string.Equals(hash, release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("下载文件的 SHA-256 校验失败，已停止安装。");
            UpdateManager.UpdateRestart(true, verifiedSource: output, expectedSha256: release.Sha256);
        }
        catch (Exception ex)
        {
            try { if (output is not null && File.Exists(output)) File.Delete(output); }
            catch (Exception cleanupError) { ModBase.Log(cleanupError, "[Update] 清理下载文件失败"); }
            ModBase.Log(ex, "[Update] 安装失败");
            HintService.Hint($"更新失败：{ex.Message}", HintType.Error);
        }
        finally { Installing = false; InstallLock.Release(); }
    }
}
