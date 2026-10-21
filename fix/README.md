# PCL_CE_Fix

**又名: _Fuck CN Parent Fix_**

第三方基于 PCL Community Edition 独立修改的版本，解除中国大陆地区及区域信息不可用时的 Minecraft 安装、整合包安装、修复和启动文件补全限制。

区域补丁保留区域识别及本地化逻辑，只将 `RegionAssessment.IsRestricted` 改为 `false`。另提供可关闭的启动提醒与双仓库更新页面。原作者署名、赞助入口、正版购买劝导及许可证保持原样。与原版 PCL 及 PCL CE 的维护者无隶属关系。

感谢 [PCL Community Edition](https://github.com/PCL-Community/PCL-CE) 的开发者，以及 [PCL](https://github.com/Meloong-Git/PCL) 原作者龙腾猫跃提供的基础项目。

## 本地应用与验证

需要 Python 3、Git、.NET 10 SDK，以及 Windows 环境来运行 WPF 测试。

```powershell
# 对干净的上游 checkout 应用补丁；已经打过补丁会报错：
python fix/apply.py PATH_TO_CLEAN_UPSTREAM
dotnet test PCL.Core.Test/PCL.Core.Test.csproj -c Release -p:Platform=x64 --filter "FullyQualifiedName~RegionPolicyTest|FullyQualifiedName~ReleaseUpdatePolicyTest|FullyQualifiedName~ReleaseUpdateClientTest"
dotnet publish 'Plain Craft Launcher 2/Plain Craft Launcher 2.csproj' -c Release -p:Platform=x64 --no-self-contained -o artifacts/x64
```

`fix/remove-region-restriction.patch` 同时更新上游区域测试的限制预期，保留原有区域分类断言。修改补丁时需同步维护控制分支中的源码及测试。

`fix/launcher-experience.patch` 包含启动提醒选项、双仓库更新界面、下载校验和相应测试；自动同步时会与区域补丁一起检查并应用。

## 许可与原项目

`PCL.Core/` 等目录使用根目录 Apache 2.0 许可证；`Plain Craft Launcher 2/` 使用其目录内的《PCL 分发有限许可》及使用指南。请阅读并保留全部原始许可文本，不能将整个项目视为 Apache 2.0。

原项目：[PCL Community Edition](https://github.com/PCL-Community/PCL-CE) · [PCL](https://github.com/Meloong-Git/PCL)。
