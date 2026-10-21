using System.Windows.Input;
using PCL.Core.App;
using PCL.Core.Utils.OS;

namespace PCL;

public partial class PageSetupUpdate
{
    private bool _initializing;
    private bool _checking;
    private ReleaseUpdateCandidate? _fork;
    private ReleaseUpdateCandidate? _upstream;

    public PageSetupUpdate()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _initializing = true;
            TextCurrentVersion.Text = $"当前：PCL_CE_Fix {ModBase.versionBaseName} · {ReleaseUpdatePolicy.CurrentReleaseTag}";
            CheckStartupUpdate.Checked = Config.Update.UpdateMode != LauncherAutoUpdateBehavior.Disable;
            CheckHideNotice.Checked = Config.Update.HideCommunityNotice;
            _initializing = false;
            CheckUpdate();
        };
    }

    private void SettingsChanged(object sender, bool user)
    {
        if (_initializing || !IsLoaded) return;
        Config.Update.HideCommunityNotice = CheckHideNotice.Checked == true;
        Config.Update.UpdateMode = CheckStartupUpdate.Checked == true
            ? LauncherAutoUpdateBehavior.AnnounceOnly : LauncherAutoUpdateBehavior.Disable;
    }

    public async void CheckUpdate()
    {
        if (_checking) return;
        _checking = true;
        BtnCheckAgain.IsEnabled = BtnForkUpdate.IsEnabled = BtnUpdate.IsEnabled = false;
        _fork = _upstream = null;
        TextForkStatus.Text = TextUpstreamStatus.Text = "正在检查…";
        TextForkNotes.Text = TextUpstreamNotes.Text = "";
        try
        {
            var results = await ForkUpdateService.CheckAsync(force: true);
            foreach (var result in results)
            {
                var fork = result.Repository == ReleaseUpdatePolicy.ForkRepository;
                var status = fork ? TextForkStatus : TextUpstreamStatus;
                var notes = fork ? TextForkNotes : TextUpstreamNotes;
                var button = fork ? BtnForkUpdate : BtnUpdate;
                if (result.Release is not { } release)
                {
                    status.Text = result.Error;
                    continue;
                }
                var newer = ForkUpdateService.IsNewer(release);
                status.Text = $"{release.Tag} · {(newer ? "有可用更新" : fork ? "当前已是此发布版本" : "可选择切回此上游版本")}";
                notes.Text = release.Notes.Length > 1200 ? release.Notes[..1200] + "…" : release.Notes;
                button.IsEnabled = !ForkUpdateService.Installing && (!fork || newer);
                if (fork) _fork = release; else _upstream = release;
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "[Update] 检查失败");
            TextForkStatus.Text = TextUpstreamStatus.Text = "检查失败，请稍后重试。";
        }
        finally { _checking = false; BtnCheckAgain.IsEnabled = true; }
    }

    private async Task InstallAsync(ReleaseUpdateCandidate? release)
    {
        if (release is null || ForkUpdateService.Installing) return;
        BtnForkUpdate.IsEnabled = BtnUpdate.IsEnabled = false;
        await ForkUpdateService.InstallAsync(release);
        CheckUpdate();
    }

    private async void ForkUpdate_Click(object sender, MouseButtonEventArgs e) => await InstallAsync(_fork);
    private async void UpstreamUpdate_Click(object sender, MouseButtonEventArgs e) => await InstallAsync(_upstream);
    private void CheckAgain_Click(object sender, MouseButtonEventArgs e) => CheckUpdate();
    private void ForkRelease_Click(object sender, MouseButtonEventArgs e) =>
        ModBase.OpenWebsite(_fork?.PageUrl ?? "https://github.com/re-mcmohen/PCL-CE-Fix/releases");
    private void UpstreamRelease_Click(object sender, MouseButtonEventArgs e) =>
        ModBase.OpenWebsite(_upstream?.PageUrl ?? "https://github.com/PCL-Community/PCL-CE/releases");
}
