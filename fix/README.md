# PCL_CE_Fix

第三方基于 PCL Community Edition 独立修改的版本，解除中国大陆地区及区域信息不可用时的 Minecraft 安装、整合包安装、修复和启动文件补全限制。

区域补丁保留区域识别及本地化逻辑，只将 `RegionAssessment.IsRestricted` 改为 `false`。另提供可关闭的启动提醒与双仓库更新页面。原作者署名、赞助入口、正版购买劝导及许可证保持原样。与原版 PCL 及 PCL CE 的维护者无隶属关系。

## 分支与发布

- 默认分支 `dev`：维护本项目的 `fix/` 补丁和 `.github/workflows/fix-sync-release.yml`，请在这里修改自动化。
- `patched-dev`：自动化从上游 `PCL-Community/PCL-CE:dev` 重新生成的完整修改版源码，不要直接向此分支提交；下一次同步会用带 lease 的强制推送替换它。
- 每天北京时间 02:23、08:23、14:23、20:23 检查上游（GitHub 调度可能延迟）。同一上游与控制版本已有成功 Release 时跳过；失败后下次重试。
- 两个架构均构建成功才发布；直接发布 x64 和 ARM64 的单文件 `.exe`，另附许可证及 `fix-provenance.json`。Release 带 EXE 的 SHA-256 校验文件，并绑定完整补丁源码提交。
- 补丁上下文不匹配时停止，不会自动猜测如何处理新增的限制。上游如果增加新的独立检查，需要人工审查并更新补丁。

这是跟随 **dev 开发分支** 的持续构建，不代表上游稳定版质量。

## 首次部署

在已登录 GitHub CLI 的终端执行（`OWNER` 替换成你的账号）：

```powershell
gh repo fork PCL-Community/PCL-CE --fork-name PCL-CE-Fix --clone=false
git remote add fork https://github.com/OWNER/PCL-CE-Fix.git
# 将本地修改审查并提交后，推送默认控制分支：
git push fork dev
gh repo edit OWNER/PCL-CE-Fix --default-branch dev
gh workflow enable fix-sync-release.yml --repo OWNER/PCL-CE-Fix
gh workflow run fix-sync-release.yml --repo OWNER/PCL-CE-Fix --ref dev
```

Fork 的 Actions 初始可能需要在仓库 Actions 页面启用。仓库或组织策略需允许工作流写入 contents。工作流显式请求所需权限，不需要上游的发布、MirrorChyan 或签名密钥。

如果 GitHub 拒绝同步包含 `.github/workflows` 变更的上游提交，请在仓库 Actions Secrets 中设置 `SYNC_TOKEN`：使用仅能访问本仓库的令牌，授予 Contents 和 Workflows 写入权限；经典 PAT 对公开仓库需要 `public_repo` 与 `workflow`。该令牌仅用于同步源码。无此需求时默认使用内置 `GITHUB_TOKEN`。

补丁维护时，控制分支提交会触发一次构建。也可以在 Actions 中手动运行 **PCL_CE_Fix Sync and Release**。构建或补丁失败时，在 Actions 日志检查失败原因；修正后重跑即可。

GitHub 可能在公开仓库长期无活动后关闭定时工作流；如停止运行，请在 Actions 中重新启用。

## 运行环境与体积

与上游一致使用 `--no-self-contained` 发布单文件程序，不包含 .NET/WPF 运行时。运行前需要安装对应架构的 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。单文件不等于自包含：旧版自包含构建将整个运行时打入 EXE，因此体积超过 150 MB；当前发布方式只打包应用及其依赖。

## API 凭据与更新

Fork 不会获得上游 Secrets。可选配置你自己的 `CLIENT_ID`（Microsoft OAuth）和 `CURSEFORGE_API_KEY`。缺少这些凭据时构建仍可进行，但相应服务可能不可用；联机大厅等依赖上游私有服务凭据的功能同样不保证可用。本仓库不尝试提取上游密钥。

启动社区版提示中可点击“不再提醒”；也可在设置的更新页面修改“不再显示社区版启动提醒”，此选择保存到本地配置。

更新页面独立检查本仓库与上游的最新正式 Release，分别展示来源、版本、更新说明和安装按钮。一方网络失败不影响另一方。点击安装后会确认目标来源、下载对应进程架构的 EXE、校验 GitHub 提供的 SHA-256，再使用现有替换机制安装并重启。同版本号的本仓库补丁更新通过发布标签识别。

启动时的检查开关同时控制两个来源；所有旧自动更新模式在修改版中均只提示，必须由玩家主动选择安装来源。切回上游会恢复上游的功能与更新行为，可能重新出现区域限制和启动提醒；切换后可通过本仓库 Releases 手动切回修改版。GitHub API 限流或连接失败时，可以使用各自的“打开发布页”按钮手动下载。

## 本地应用与验证

需要 Python 3、Git、.NET 10 SDK，以及 Windows 环境来运行 WPF 测试。

```powershell
# 对干净的上游 checkout 应用补丁；已经打过补丁会报错：
python fix/apply.py PATH_TO_CLEAN_UPSTREAM
dotnet test PCL.Core.Test/PCL.Core.Test.csproj -c Release -p:Platform=x64 --filter "FullyQualifiedName~RegionPolicyTest|FullyQualifiedName~ReleaseUpdatePolicyTest"
dotnet publish 'Plain Craft Launcher 2/Plain Craft Launcher 2.csproj' -c Release -p:Platform=x64 --no-self-contained -o artifacts/x64
```

`fix/remove-region-restriction.patch` 同时更新上游区域测试的限制预期，保留原有区域分类断言。修改补丁时需同步维护控制分支中的源码及测试。

`fix/launcher-experience.patch` 包含启动提醒选项、双仓库更新界面、下载校验和相应测试；自动同步时会与区域补丁一起检查并应用。

## 许可与原项目

`PCL.Core/` 等目录使用根目录 Apache 2.0 许可证；`Plain Craft Launcher 2/` 使用其目录内的《PCL 分发有限许可》及使用指南。请阅读并保留全部原始许可文本，不能将整个项目视为 Apache 2.0。

上游项目说明见控制分支原始 README，或生成分支的 `README-UPSTREAM.md`。
