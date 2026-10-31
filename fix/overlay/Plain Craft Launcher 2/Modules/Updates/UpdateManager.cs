using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Utils;
using PCL.Core.Utils.OS;

namespace PCL;

public static class UpdateManager
{
    public static bool isUpdateWaitingRestart;

    public static UpdatesWrapperModel remoteServer = new(new List<IUpdateSource>
    {
        new UpdatesMirrorChyanModel(),
        new UpdatesRandomModel(new[]
        {
            new UpdatesMinioModel("https://s3.pysio.online/pcl2-ce/", "Pysio"),
            new UpdatesMinioModel("https://staticassets.naids.com/resources/pclce/", "Naids")
        }),
        new UpdatesMinioModel("https://github.com/PCL-Community/PCL2_CE_Server/raw/main/", "GitHub")
    });

    public static bool IsCurrentVersionBeta
    {
        get
        {
            if (Common.VersionBaseName.Contains("beta"))
                return true;
            return (int)Config.Update.UpdateChannel == 1;
        }
    }
    
    public static UpdateEnums.VersionStatus GetVersionStatus()
    {
        // This method is also called by UI actions: never wait for network here.
        ForkUpdateService.RefreshInBackground();
        var results = ForkUpdateService.Results;
        if (results.Length == 0) return UpdateEnums.VersionStatus.Unknown;
        if (results.Any(r => r.Release is not null && ForkUpdateService.IsNewer(r.Release)))
            return UpdateEnums.VersionStatus.NotLatest;
        return results.Any(r => r.Release is null)
            ? UpdateEnums.VersionStatus.Unknown : UpdateEnums.VersionStatus.Latest;
    }

    public static Loading.LoaderCombo<JsonObject> updateLoader;

    public static void UpdateStart(UpdateEnums.UpdateType type, string receivedKey = null, bool forceValidated = false)
    {
        // Always let the player select the distribution before downloading.
        ForkUpdateService.NotifyInBackground();
    }

    public static void UpdateRestart(bool triggerRestartAndByEnd, bool triggerRestart = true,
        string? verifiedSource = null, string? expectedSha256 = null)
    {
        var fileName = verifiedSource ?? Path.Combine(Common.ExePath, "PCL", "Plain Craft Launcher Community Edition.exe");
        if (!File.Exists(fileName)) throw new FileNotFoundException("未找到更新文件", fileName);
        // Run a separate copy of our updater, including when switching to an upstream release.
        // Running the current executable itself would keep the destination locked.
        var helper = verifiedSource is null ? fileName
            : Path.Combine(Path.GetDirectoryName(fileName)!, "PCL_CE_Fix_Updater.exe");
        if (verifiedSource is not null) File.Copy(Basics.ExecutablePath, helper, overwrite: false);
        var start = new ProcessStartInfo(helper)
        {
            UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Basics.ExecutablePath)!
        };
        foreach (var argument in new[] { "update", Environment.ProcessId.ToString(), Basics.ExecutablePath,
                     fileName, triggerRestart ? "true" : "false" }) start.ArgumentList.Add(argument);
        if (expectedSha256 is not null) start.ArgumentList.Add(expectedSha256);
        using var updater = Process.Start(start) ?? throw new IOException("无法启动更新程序");
        if (triggerRestartAndByEnd)
        {
            Launcher.frmMain.EndProgram(false, true);
            Common.Log("[System] 已由于更新强制结束程序");
        }
    }

    /// <summary>
    ///     确保 PathTemp 下的 Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    ///     如果不是，则下载一个。
    /// </summary>
    internal static void DownloadLatestPCL(Loading.LoaderBase loaderToSyncProgress = null)
    {
        // 注意：如果要自行实现这个功能，请换用另一个文件路径，以免与官方版本冲突
        var latestPCLPath = Path.Combine(Common.pathTemp, "CE-Latest.exe");
        var target = remoteServer.GetLatestVersion(UpdateChannel.stable,
            SystemInfo.IsArm64System ? UpdateArch.arm64 : UpdateArch.x64);
        if (target is null)
            throw new Exception(Lang.Text("Update.Error.UnableToGetUpdate"));
        if (File.Exists(latestPCLPath) && (Common.GetFileSha256(latestPCLPath) ?? "") == (target.Sha256 ?? ""))
        {
            Common.Log("[System] 最新版 PCL 已存在，跳过下载");
            return;
        }

        if ((Common.GetFileSha256(Basics.ExecutablePath) ?? "") == (target.Sha256 ?? "")) // 正在使用的版本符合要求，直接拿来用
        {
            Common.CopyFile(Basics.ExecutablePath, latestPCLPath);
            return;
        }

        var loaders = remoteServer.GetDownloadLoader(UpdateChannel.stable,
            SystemInfo.IsArm64System ? UpdateArch.arm64 : UpdateArch.x64, latestPCLPath);
        var loader = new Loading.LoaderCombo<int>(Lang.Text("Update.Task.DownloadLatestStable"), loaders);
        loader.Start();
        loader.WaitForExit();
    }

    public static Loading.LoaderTask<int, int> serverLoader =
        new(Lang.Text("Update.Service.PclCe"),
            _ => LoadOnlineInfo(),
            priority: ThreadPriority.BelowNormal);

    private static void LoadOnlineInfo()
    {
        ScheduleBasedOnConfig();
        AnnouncementService.Load();
    }

    private static void ScheduleBasedOnConfig()
    {
        if (Config.Update.UpdateMode == LauncherAutoUpdateBehavior.Disable) return;
        ForkUpdateService.NotifyInBackground();
    }

    /// <summary>
    ///     展示社区版提示
    /// </summary>
    /// <param name="IsUpdate">是否为更新时启动</param>
    public static void ShowCEAnnounce()
    {
        if (Config.Update.HideCommunityNotice) return;
        if (Launcher.MyMsgBox(Lang.Text("Update.CommunityNotice.Body"),
                Lang.Text("Update.CommunityNotice.Title"),
                Lang.Text("Update.CommunityNotice.Confirm"), "不再提醒") == 2)
            Config.Update.HideCommunityNotice = true;
    }
}
