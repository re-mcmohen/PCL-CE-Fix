# PCL_CE_Fix

第三方基于 PCL Community Edition 独立修改的版本，解除中国大陆地区及区域信息不可用时的 Minecraft 安装、整合包安装、修复和启动文件补全限制。

保留区域识别及本地化逻辑，只将 `RegionAssessment.IsRestricted` 改为 `false`。原作者署名、赞助入口、正版购买劝导及许可证保持原样。与原版 PCL 及 PCL CE 的维护者无隶属关系。

## 分支与发布

- 默认分支 `dev`：维护本项目的 `fix/` 补丁和 `.github/workflows/fix-sync-release.yml`，请在这里修改自动化。
- `patched-dev`：自动化从上游 `PCL-Community/PCL-CE:dev` 重新生成的完整修改版源码，不要直接向此分支提交；下一次同步会用带 lease 的强制推送替换它。
- 每天北京时间 02:23、08:23、14:23、20:23 检查上游（GitHub 调度可能延迟）。同一上游与控制版本已有成功 Release 时跳过；失败后下次重试。
- 两个架构均构建成功才发布；每个 ZIP 包含自包含程序、许可证及 `fix-provenance.json`。Release 带 SHA-256 校验文件，并绑定完整补丁源码提交。
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

## API 凭据与更新

Fork 不会获得上游 Secrets。可选配置你自己的 `CLIENT_ID`（Microsoft OAuth）和 `CURSEFORGE_API_KEY`。缺少这些凭据时构建仍可进行，但相应服务可能不可用；联机大厅等依赖上游私有服务凭据的功能同样不保证可用。本仓库不尝试提取上游密钥。

**目前程序内更新仍使用上游服务器。请关闭自动更新，并从本仓库 Releases 手动下载更新，否则可能被覆盖回含区域限制的上游版本。** 游戏和 Mod 的更新不受此说明影响。

## 本地应用与验证

需要 Python 3、Git、.NET 10 SDK，以及 Windows 环境来运行 WPF 测试。

```powershell
# 对干净的上游 checkout 应用补丁；已经打过补丁会报错：
python fix/apply.py PATH_TO_CLEAN_UPSTREAM
dotnet test PCL.Core.Test/PCL.Core.Test.csproj -c Release -p:Platform=x64 --filter FullyQualifiedName~RegionPolicyTest
dotnet publish 'Plain Craft Launcher 2/Plain Craft Launcher 2.csproj' -c Release -p:Platform=x64 --self-contained true -o artifacts/x64
```

`fix/remove-region-restriction.patch` 同时更新上游区域测试的限制预期，保留原有区域分类断言。修改补丁时需同步维护控制分支中的源码及测试。

## 许可与原项目

`PCL.Core/` 等目录使用根目录 Apache 2.0 许可证；`Plain Craft Launcher 2/` 使用其目录内的《PCL 分发有限许可》及使用指南。请阅读并保留全部原始许可文本，不能将整个项目视为 Apache 2.0。

上游项目说明见控制分支原始 README，或生成分支的 `README-UPSTREAM.md`。
