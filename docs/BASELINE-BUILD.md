# 原版 net45 基线构建

在仓库根目录使用 PowerShell 7：

```powershell
./scripts/Build-Baseline.ps1
./scripts/Build-Baseline.ps1 -Configuration Debug
```

需要已安装的 Visual Studio Build Tools（完整 Framework MSBuild，17.8 或以上；引用路径验证使用 -getProperty）、vswhere 和 NuGet 网络访问；不要求安装全局 net45 targeting pack，不使用 dotnet build。脚本默认 Release，固定目标仍为 .NET Framework 4.5，既有 NuGet 包版本不变。

脚本校验 Microsoft.NETFramework.ReferenceAssemblies.net45 1.0.3 的 SHA256，将其解压到每次新建的 `artifacts/baseline/<时间戳>-<配置>/`。MSBuild 的 TargetFrameworkRootPath 必须指向包内 **build**，因为解析器自行追加 `.NETFramework/v4.5`。

每次复制 src 到独立目录，排除 bin、obj、packages、.vs 和回收目录。所有恢复、编译及编织只在副本中进行；副本内 FodyWeavers.xml 设置 `DisableCleanup=true`，并传入 `FodyGenerateXsd=false`。Fody/Costura 保持启用，原始 XML、XSD、csproj 与源码不改。这个基线输出保留外置 DLL，不能当作已经验证的单文件正式发布包。

证据目录包含：result.json、command.json、四个项目的引用路径 JSON、build.log、build.binlog、embedded-resources.json、output-manifest.json，以及源码副本、引用程序集和编译产物。产物检查覆盖三个 EXE、共享 UI DLL、x86/x64 SQLite 原生库与主程序嵌入资源。构建失败也保留目录和错误信息；缓存哈希不匹配时中止，保留原文件调查。

脚本没有 Clean/Rebuild 或删除操作。无需人工清理即可再次运行；如要回收旧证据，先校验精确目录，再移动到仓库 `.recycle-bin/`，不要永久删除。

现有 Newtonsoft.Json 12.0.2 的 NU1903 高危漏洞警告不被屏蔽；正式发布前必须另行处理。基线构建成功不等于所有运行或 Windows 11 验收通过。
