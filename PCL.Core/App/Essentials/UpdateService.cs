using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using PCL.Core.App.IoC;
using PCL.Core.Utils.Exts;

namespace PCL.Core.App.Essentials;

[LifecycleService(LifecycleState.BeforeLoading)]
public sealed class UpdateService : GeneralService
{
    private static LifecycleContext? _context;
    private static LifecycleContext Context => _context!;

    private UpdateService() : base("update", "更新", false) { _context = ServiceContext; }

    public override void Start()
    {
        var args = Basics.CommandLineArguments;
        
        if (args is not ["update", _, _, _, _] and not ["update", _, _, _, _, _])
        {
            switch (args)
            {
                case ["update_finished", _]:
                {
                    var toDelete = args[1];
                    // The updater may still be exiting; cleanup must not interrupt launcher startup.
                    for (var attempt = 0; attempt < 15 && !UpdateHelper.TryDelete(toDelete); attempt++)
                        Thread.Sleep(100);
                    var directory = Path.GetDirectoryName(Path.GetFullPath(toDelete))!;
                    var updatesRoot = Path.Combine(Basics.ExecutableDirectory, "PCL", "Updates") + Path.DirectorySeparatorChar;
                    if (directory.StartsWith(updatesRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        var helper = Path.Combine(directory, "PCL_CE_Fix_Updater.exe");
                        for (var attempt = 0; attempt < 15 && !UpdateHelper.TryDelete(helper); attempt++) Thread.Sleep(100);
                        try { if (Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                    Context.Debug("更新临时文件清理完成");
                    break;
                }
                case ["update_failed", _]:
                {
                    var reason = args[1];
                    Context.Error(
                        $"更新失败: {reason}\n你可以手动将 exe 文件替换为 PCL 目录中的新版本" +
                        $"或再次尝试更新，若再次尝试仍然失败，请尽快反馈这个问题");
                    break;
                }
                default: Context.Debug("无更新任务"); break;
            }
            Context.DeclareStopped();
            return;
        }

        try
        {
            Context.Info("开始更新");
            Lifecycle.PendingLogDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2]))!, "Log");
            Lifecycle.PendingLogFileName = "LastPending_Update.log";

            var oldProcessId = args[1].Convert<int>();
            Context.Debug($"旧版本进程 ID: {oldProcessId}");
            try
            {
                using var oldProcess = Process.GetProcessById(oldProcessId);
                Context.Debug("正在等待旧版本进程退出");
                if (!oldProcess.WaitForExit(60000))
                    throw new TimeoutException("旧版本进程未能在 60 秒内退出，更新已停止。");
                Context.Trace("旧版本进程已退出");
            }
            catch (ArgumentException)
            {
                // The original process has already exited.
            }

            Context.Debug("正在替换文件");
            var target = args[2];
            Context.Trace($"目标: {target}");
            var source = args[3];
            Context.Trace($"来源: {source}");
            var expected = args.Length == 6 ? args[5] : UpdateHelper.ComputeSha256(source);
            var ex = UpdateHelper.Replace(source, target, expected);
            if (ex is not null) throw new IOException("替换文件失败；请关闭占用文件的程序后重试，详情见更新日志。", ex);
            Context.Trace("替换完成且 SHA-256 校验通过");

            var restart = args[4].Convert<bool>();
            if (restart)
            {
                var restartArgs = $"finished \"{source}\"";
                restartArgs = $"update_{restartArgs}";
                Context.Debug($"重启中，使用参数: {restartArgs}");
                Process.Start(new ProcessStartInfo(target, restartArgs)
                    { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! });
            }
        }
        catch (Exception ex)
        {
            Context.Error("更新过程出错", ex);
            MessageBox.Show($"更新未完成：{ex.Message}\n\n已停止自动重启。请查看 Log/LastPending_Update.log，关闭占用文件的程序后重试，或手动替换为已下载的新版本。",
                "PCL_CE_Fix 更新失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        
        Context.RequestExit();
    }
}
