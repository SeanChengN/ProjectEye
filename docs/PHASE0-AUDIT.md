# ProjectEye Enhanced — Phase 0 项目审计

审计日期：2026-11-16（Asia/Shanghai）。范围：严格遵守 `PLAN.md` 第 12 节，本轮仅执行 Phase 0。

## 结论和阶段状态

源码、许可证、配置和事件链审计已完成；原始构建已实际尝试并保存失败基线，**Phase 0 尚未全部通过**，不能宣称原版能够编译运行。没有进入 Phase 1，也没有修改应用源码、目标框架或既有依赖版本。

| Phase 0 条目 | 结果 |
| --- | --- |
| Fork 或克隆上游 | 已存在本地克隆，origin 指向 SeanChengN/ProjectEye；未核实 GitHub fork 关系元数据 |
| 独立开发分支 | 已创建 `audit/projecteye-enhanced-phase0` |
| 许可证 | 已核对根目录 LICENSE，MIT |
| 框架和依赖 | 已核实，见下文 |
| 休息事件 | 已核实定义、调用、订阅和生命周期 |
| 配置机制 | 已核实 XML 持久化及补默认值机制 |
| 原始版本构建 | 未通过，两次尝试均被 MSB3644 阻断 |
| 保存基线结果 | 已保存两次日志、退出码和隔离引用程序集 |

下一步最小决策：选择在本机补齐 .NET Framework 4.5 targeting pack，或在后续任务中修正隔离引用程序集的 MSBuild 解析方式；先获得成功的原始构建，再开展 GSMTC PoC。当前证据不足以要求升级到 .NET Framework 4.8。

## 仓库与上游基线

- 仓库：`ProjectEye`，父目录为共享工程域。仓库及 src 下未发现项目专属 `AGENTS.md` 或 `.editorconfig`，遵守父域规则。
- 初始分支：`master`；初始 `git status --short` 仅有 `?? PLAN.md`，跟踪文件和暂存区无修改。
- 本地 HEAD：`50519089db349ef7ce60534078c6b7d49e5ba76b`，提交日期 2022-06-04，标题“更新版本号到1.4.3”。
- origin：`https://github.com/SeanChengN/ProjectEye`。
- 实测 `git ls-remote origin HEAD` 和 `git ls-remote https://github.com/Planshit/ProjectEye.git HEAD`，两者均为上述 SHA。当前审计源代码与上游 HEAD 一致；未更改远端配置。
- 应用 AssemblyVersion 为 `1.4.3`，AssemblyFileVersion 为 `1.0.0.1`；未改为增强版版本号。
- `.gitattributes` 使用 `* text=auto`。本轮新报告为 UTF-8 无 BOM、LF；保留原 `PLAN.md` 内容和格式，不把未完成项标成完成。
- 未发现测试项目、自动化测试框架或构建工作流；`.github` 只有 FUNDING.yml。

## 许可证

根目录 LICENSE 实际内容为 MIT License，版权行为 `Copyright (c) 2020 Plan shit`。后续修改、再分发必须保留版权和许可文本。

本轮没有新增运行时依赖。隔离构建辅助包 `Microsoft.NETFramework.ReferenceAssemblies.net45 1.0.3` 仅放在被忽略的 artifacts 目录，其 nuspec 作者为 Microsoft，标记 developmentDependency，licenseUrl 指向 Microsoft/dotnet/LICENSE；本轮未获取并审阅该许可正文，不将其宣称为 MIT，也不将引用程序集随应用发布。原有全部直接及传递依赖的许可证和再分发条件尚未逐项完成发布审查。

## 项目结构与构建条件

解决方案：`src/Local/ProjectEye/ProjectEye.Local.sln`，格式为 VS 16 / Visual Studio 2019，MinimumVisualStudioVersion 为 10.0.40219.1。四个旧式 csproj 均为 ToolsVersion 15.0、目标 `v4.5`、默认 AnyCPU。

| 项目 | 作用 | 关键依赖 |
| --- | --- | --- |
| ProjectEye | WPF 主程序，服务、ViewModel、配置、SQLite 统计 | Project1.UI 和 PackageReference |
| Project1.UI | 共享 WPF 控件和主题 | Framework 引用 |
| ProjectEyeUp | 控制台更新器 | Framework 压缩、文件及网络类 |
| ProjectEyeBug | WPF 错误提示工具 | Project1.UI、packages.config、Fody/Costura |

主程序直接 NuGet 版本：

| 包 | 版本 |
| --- | --- |
| Costura.Fody | 4.0.0 |
| EntityFramework | 6.3.0 |
| Fody | 5.0.6 |
| Newtonsoft.Json | 12.0.2 |
| Npoi.Mapper | 3.5.1 |
| SQLite.CodeFirst | 1.5.3.29 |
| System.Data.SQLite | 1.0.112 |
| System.Data.SQLite.Core | 1.0.112.1 |
| System.Data.SQLite.EF6 | 1.0.112.1 |
| System.Data.SQLite.Linq | 1.0.112.1 |
| WpfAnimatedGif | 2.0.0 |

ProjectEyeBug 使用 `Costura.Fody 4.1.0`、`Fody 6.1.1`，通过相对路径引用 `../ProjectEye/packages`。不能只恢复主程序的 PackageReference 而遗漏 packages.config。四个项目中 Up/Bug 的输出目录与主程序共享，后续便携打包必须核对所需辅助程序和 SQLite 原生资源，不可只取主 EXE。

本机环境实测：

- Windows 10 Education 22H2，Build 19045.6456；没有 Windows 11 验证环境证据。
- vswhere 找到 VS Build Tools 18.9.12120.119；MSBuild `18.9.1.35102`。
- MSBuild 路径：`D:\AppData\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe`。
- `dotnet --info` 显示没有 .NET SDK；已安装现代 .NET runtime 不能证明旧式 WPF 构建条件满足。
- 标准路径的 .NET Framework 4.5 引用程序集不存在；MSBuild 的实际失败与之吻合。
- CodeGraph explore 返回没有索引，且工具明确要求由项目所有者决定初始化；未创建索引，采用 rg 定点追踪。

## 真实休息生命周期

路径以下均相对于仓库根目录。

| 位置 | 已验证行为 |
| --- | --- |
| `src/Local/ProjectEye/Core/Service/RestService.cs:22` | 定义 RestCompleted |
| 同文件 `:26` | 定义 RestStart |
| 同文件 `:50` | Start 读取 RestTime、启动 DispatcherTimer，再于 :54 同步触发 RestStart |
| 同文件 `:59` | End 隐藏 TipWindow、停止 timer、重置计时、重启工作计时监视，再调用 OnResetCompleted |
| 同文件 `:86` | RestCompleted 的唯一 Invoke 位置 |
| `src/Local/ProjectEye/ViewModels/TipViewModel.cs:436` | resetCommand_action 更新界面可见性，:441 调用 reset.Start；用户命令、自动行为、超时处理汇入此路径 |
| 同文件 `:447` | busyCommand_action 只重启 main、隐藏窗口、记跳过统计，不调用 RestService.End |
| 同文件 `:59`、`:415` | 订阅休息完成，恢复界面、播放提示音、重启 main |
| `src/Local/ProjectEye/Core/Service/PreAlertService.cs:111` | 订阅 RestStart，暂停预提醒；:112 订阅 RestCompleted，重新启动预提醒 |

线程判断来自静态调用链：App 在 WPF 启动线程创建服务和 DispatcherTimer；现有开始入口来自 UI 命令、窗口显示及 DispatcherTimer，完成来自 DispatcherTimer.Tick，因此正常路径在 WPF UI dispatcher 上同步执行。Start 没有强制调度/线程断言，不能保证未来调用者都在 UI 线程。尚未运行应用进行线程采样。

关键风险：

1. Start 不检查是否已经休息，没有唯一休息 ID，每次调用都会重置倒计时并触发 RestStart。
2. End 是私有方法，只由倒计时走到结束触发；没有公开 Cancel/Stop/Skip 完成通路。
3. 提示前跳过不会产生 RestStart/RestCompleted。若休息中通过快捷键等执行 busyCommand，现有代码也不停止 RestService 的 timer；不能把“窗口隐藏”视为休息已结束。
4. MainService.OnLeave 隐藏窗口并停止主服务计时，MainService.Exit 释放 screen、关闭窗口，但未调用 RestService 的完成事件；退出清理必须独立设计。
5. 多显示器窗口可复用 ViewModel，窗口 OnWShow 调用 HandleAutoAction，再汇入 resetCommand_action；静态上存在重复开始风险，需实机验证，不能每个显示器都创建媒体任务。
6. TipWindow 已由 MainService.ShowTipWindow 显示后，才可能由显示事件自动开始休息；RestStart 是同步 void 事件。仅订阅 async void 不能保证“暂停完成之后才显示原有窗口”。Phase 1 后需明确以实际休息开始为触发点，保持 UI 响应，不能为等待媒体 API 阻塞 dispatcher。

## 媒体检测

`src/Local/ProjectEye/Core/BusyVerdictHelper.cs:17` 的 IsWatchingVideo 会枚举进程但始终返回 false；MusicApps 只有 `cloudmusic` 与空字符串，没有可用的媒体状态检测。全 src 搜索没有发现此方法的调用点，也没有 GSMTC、MediaPlayPause 或系统媒体会话实现。

`MainService.IsBreakReset` 当前使用全屏判断和 Behavior.BreakProgressList 进程名单决定是否跳过提醒。这是原有免打扰规则，不能替代真实媒体状态或作为恢复身份依据。`AudioHelper`/SoundService 是提醒提示音用途，未发现可复用的系统会话控制接口。

## 配置、启动、后台和退出

- `ConfigService` 读取运行目录下 `Data/config.xml`；`XmlExtensions` 使用 XmlSerializer。Save 先触发 Changed、以 JSON 深拷贝保存 oldOptions，最后写 XML。
- `Core/Models/Options/OptionsModel.cs` 包含 General、Style、KeyboardShortcuts、Behavior、Tomato；ConfigService.CheckOptions 为缺失引用属性调用无参构造补默认值，并进一步检查 Style/Tomato。
- 建议后续增加独立 Media 配置节，构造默认关闭暂停/恢复、所有会话、日志开启；通过旧 XML 和新 XML 往返验证兼容性。本轮没有改变 XML 结构，也没有操作用户已有配置。
- 设置 ViewModel 调用 config.Save；休息开始时应复制媒体配置快照，避免 Changed 改变当前周期。
- App.OnStartup 中按顺序注册并初始化自定义 ServiceCollection；依赖只能取已注册实例，新服务应在所需依赖后注册。
- App 使用 `projecteye` mutex；通过托盘驻留，托盘退出调用 Application.Shutdown。MainService 和 TrayService 均订阅 App.Exit；新增媒体清理必须幂等且不在退出时擅自恢复。
- BackgroundWorkerService 是现有串行后台动作队列，不能直接等同于支持异步取消和休息任务关联的协调器。
- LogHelper 写运行目录 Log，写入失败会吞异常；可参考其接口，但新日志必须去除媒体元数据、受配置控制并限制增长。
- 普通安装目录可能不可写，而现有配置、日志、统计都依赖运行目录。安装包阶段须解决普通用户可写性与迁移兼容，不能仅生成安装器即视为满足“不要求管理员”。

## 基线构建：实际命令和结果

日志位于被现有 .gitignore 忽略的 `artifacts/phase0-20261116/`，未纳入提交。没有运行 Clean/Rebuild、更新器或任何清理命令。

首次，未经源码改动，调用上述 MSBuild：

```powershell
& $msbuild src/Local/ProjectEye/ProjectEye.Local.sln /restore /t:Build /p:Configuration=Release '/p:Platform=Any CPU' /p:RestorePackagesConfig=true /nologo /v:minimal /fl "/flp:LogFile=$auditPath\baseline-build.log;Encoding=UTF-8;Verbosity=normal"
```

结果：NuGet 恢复成功，主程序与 ProjectEyeBug 的两种依赖格式均被恢复；退出码 1，4 个 MSB3644 错误，各项目均无法解析 `.NETFramework,Version=v4.5` 的引用程序集。另有 1 个 NU1903 警告：Newtonsoft.Json 12.0.2 的高严重性漏洞 `GHSA-5crp-9r3c-p9vr`（https://github.com/advisories/GHSA-5crp-9r3c-p9vr）。这是已存在的依赖风险，本轮不擅自升级。

第二次，仅下载官方 NuGet 引用程序集到隔离目录，不增加项目包引用，不安装系统组件：

- 包：Microsoft.NETFramework.ReferenceAssemblies.net45 1.0.3。
- 来源：`https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net45/1.0.3/microsoft.netframework.referenceassemblies.net45.1.0.3.nupkg`。
- SHA256：`23A9F94EA3E2CB88CD8341AF75B811C6FB5CB82516FC696E95ED4620279128E3`。
- `$refPath`：仓库内 `artifacts/phase0-20261116/net45-reference-assemblies/build/.NETFramework`。

```powershell
& $msbuild src/Local/ProjectEye/ProjectEye.Local.sln /t:Build /p:Configuration=Release '/p:Platform=Any CPU' "/p:TargetFrameworkRootPath=$refPath" /p:CleanReferenced=false /nologo /v:minimal /fl "/flp:LogFile=$auditPath\baseline-net45-refs-build.log;Encoding=UTF-8;Verbosity=normal"
```

结果：退出码 1，仍为 4 个 MSB3644。引用包存在并不代表 MSBuild 已正确采用；本轮没有确认该参数布局失败的具体原因。两种不同方案均失败，按工程规则停止进一步构建尝试。`CleanReferenced=false` 是尝试禁用 Costura 引用清理的参数，但构建尚未进入编织阶段，不宣称其行为已验证。

证据文件：`baseline-build.log`、`baseline-exit-code.txt`、`baseline-net45-refs-build.log`、`baseline-net45-refs-exit-code.txt`；隔离包和解包目录也保留在该 artifacts 目录。既有 bin/obj 和恢复的 packages 未进行删除或清理。

## 后续技术建议和未解决项

1. **先修复基线构建环境**：保留 net45；在成功构建之前不能评价是否还有 WPF/SQLite/Fody 编译或打包问题。本轮没有可运行产物，不能进行原版功能回归。
2. **Phase 1 独立 PoC**：优先研究现有框架直接引用 Windows SDK WinMD 与 WinRT await 桥接的可行性，实际编译、初始化、枚举和暂停后再决定依赖；本轮未验证 GSMTC 可调用性，不能预先承诺 net45 或升级方案可用。
3. **身份与并发**：未来模块按周期 ID、成功暂停记录、保守指纹和用户状态变化检测协调；异步调用需要取消/超时，成功返回才登记，结束等待对应暂停任务，退出不恢复。
4. **兼容性实测**：本机虽为 Windows 10 22H2，本轮没有调用 GSMTC；Windows 11、多播放器、睡眠唤醒、20 周期和 24 小时稳定性均未执行。不要把静态审查计作这些验收通过。
5. **安全依赖与再分发**：单独评估现有 Json.NET 漏洞的兼容升级及所有依赖许可证，避免与媒体模块同时大改。
6. **原有联网与计划冲突**：UpdateViewModel 仍请求 Planshit 上游 releases/latest；如果“不得连接外部服务”适用于整个增强版而非新增媒体模块，需要明确原有检查更新功能如何处理。后续发布还要避免增强版更新回上游包。本轮没有改动此功能。
7. **原有更新器删除行为**：ProjectEyeUp 有 File.Delete 覆盖旧程序及删除压缩包的实现；本轮只审查源码，没有运行更新器。未来安装/升级验证须先遵守可恢复清理规则。
8. **安装与版本**：现有 README 是 ZIP 便携分发，未发现成熟独立安装工程；增强版身份、mutex、更新源、数据目录与升级迁移需要在相应阶段设计，当前不改架构。

## 交付与完整性

本轮交付文件为本报告。`PLAN.md` 保持原状，不纳入本次审计提交；失败日志、下载包、NuGet 恢复缓存和构建中间文件保留本机且不提交。未推送、未发布 Release，未永久删除文件。

静态审计不等于运行测试：未运行应用、播放器或安装/卸载测试。阶段结束应检查 `git diff --check`、Git 仓库身份及最终状态；原有跟踪源码必须保持与审计基线相同。

执行环境附记：Codex 配置为 Windows sandbox `unelevated`。默认命令启动报 `windows unelevated restricted-token sandbox cannot reopen writable descendants under read-only carveouts directly; refusing to run unsandboxed`；后续命令使用已获工具审查许可的 require_escalated。检查默认 `.codex/.sandbox/sandbox.log` 时该文件不存在；未读取 sandbox-secrets，未修改沙箱配置或 ACL。
