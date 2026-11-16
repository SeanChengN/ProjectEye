# Phase 1 — GSMTC PoC 与兼容性记录

## 当前结论

Phase 0 已通过并提交为 `f3351ac`。独立 PoC 已证明：在本机 Windows 10 22H2、现有 .NET Framework CLR 上，**目标 net45 无需升级即可直接调用 GSMTC**。成功初始化管理器、枚举无会话场景，并完成 Edge/Chrome 双播放器暂停、重复暂停及 Playing/Paused 混合状态测试。Windows 10 22H2 本轮验证通过；用户明确没有可用的 Windows 11 环境，因此 Windows 11 兼容性尚未验收。2026-11-16 用户随后明确批准忽略该环境并继续 Phase 2；Windows 11 仍保持待办。

主程序 src 未修改，未接入 RestStart/RestCompleted，未新增运行时 NuGet 包。

## 实现与构建

```powershell
./scripts/Build-GsmtcPoc.ps1 -Enumerate
```

需要 PowerShell 7、完整 Framework MSBuild、Windows 10/11 SDK。脚本使用 vswhere 和 KitsRoot10 发现工具，固定并校验 net45 引用程序集包1.0.3，在新的 artifacts/gsmtc 时间戳目录复制源码、构建和保存证据。

使用 SDK UnionMetadata/Windows.winmd 及 Framework 的 System.Runtime.WindowsRuntime.dll await 桥接。第一次仅引用 FoundationContract/UniversalApiContract 时，AsTask 重载解析报 CS0012，要求 Windows 程序集身份；改为 SDK 完整 Windows.winmd 后编译和运行通过。没有通过修改目标框架或替换依赖解决。

PoC 带 asInvoker 和 Windows 10 supportedOS manifest；net45 目标与本机 CLR 4.0.30319.42000 兼容。没有 manifest 的首次运行报告6.2版本是 Windows 版本兼容行为；加入 manifest 后报告10.0.19045，并未发生系统变化。

默认运行只枚举状态。显式 `--pause` 才遍历所有会话，跳过非 Playing 和不支持 Pause 的会话，命令前重新检查状态，调用 TryPauseAsync，分别输出返回值及调用后状态。仅成功返回计入 pauseSucceeded；不实现自动恢复、键盘模拟或结束播放器进程。计数不表示播放器UI确认完成，实际测试仍需核对。

每个异步调用5秒超时，并尝试取消，观察迟到的异常；超时不证明命令没有在播放器中生效。会话异常分别记录，继续检查其他会话。日志只含索引、应用标识、播放状态、计数和异常类型/HRESULT，不获取标题或艺术家，不上传。

## 已完成验证

| 场景 | 证据 | 结果 |
| --- | --- | --- |
| 拆分 WinMD 编译 | artifacts/gsmtc/20261116-162826-050/build.log | CS0012，已定位为桥接程序集身份要求 |
| 完整 WinMD 编译及无会话枚举 | artifacts/gsmtc/20261116-162850-641 | 成功，无会话，exit=0 |
| 带 manifest 的可重复脚本构建及枚举 | artifacts/gsmtc/20261116-163042-817 | 构建无错误，OS=10.0.19045.0，sessions=0，failed=0，exit=0 |
| 禁止复制 SDK WinMD 的最终构建 | artifacts/gsmtc/20261116-163305-804 | 构建及枚举成功，输出只有 EXE/config/PDB，无需随包分发 SDK WinMD |
| Edge 与 Chrome 同时播放 | 同目录 players-before/pause/after.log | 两次 TryPauseAsync 返回 true；1秒后两者 Paused，failed=0 |
| 重复暂停 | 同目录 players-repeat-pause.log | playing=0，pauseSucceeded=0，两者保持 Paused |
| Edge Playing / Chrome Paused | 同目录 mixed-before/pause/after.log | 仅 Edge 暂停成功1次，Chrome原本暂停会话保持 Paused |

每个正式脚本运行目录包含源码副本、build.log、command.json、result.json、输出 EXE 和引用程序集；result 记录SDK/桥接版本、引用包及 WinMD 哈希与 EXE 哈希。没有清理旧证据。

## 实际播放器测试操作

在上面脚本输出的 POC_EXE 所在目录运行：

```powershell
./GsmtcPoc.exe
./GsmtcPoc.exe --pause
./GsmtcPoc.exe
```

先准备两个正在播放的测试播放器及一个已经暂停的会话。第一次枚举应显示对应 Playing/Paused 状态；暂停运行只针对 Playing；最后核对支持的会话变为 Paused、原本暂停的会话仍保持暂停，并由操作者确认音视频停止。再运行一次 --pause 应不再对已暂停媒体发送命令。若播放器不注册系统会话，记为不支持，不能将 sessions=0 当作暂停测试通过。

Windows 11 验证可复制 PoC 输出的 bin 目录到可写目录，在有 .NET Framework 的普通用户会话执行同样步骤；无需复制 SDK 引用包或安装新驱动。应记录 OS Build、播放器名称/版本、初始状态、TryPause返回值、结束状态、重复运行结果。不要提交原始媒体元数据或个人媒体内容。

## 阶段门槛

- 已验：实际 Playing/Paused 读取、双播放器暂停成功、原本暂停会话保持不变和重复执行；Stopped会话尚未实际准备测试。
- 已验：两个浏览器播放器同时播放；非浏览器播放器、不支持控制、播放中关闭会话仍未实际验证，不扩大兼容性结论。
- Windows 10 22H2 实际媒体场景通过；Windows 11 未测，用户已明确没有环境，原为阶段阻断，用户于2026-11-16明确延期并允许继续Phase2。
- 仅勾选实际验证项目；用户已批准Windows11延期并进入Phase2，保留PoC跨系统验证步骤，不以本机结果代替Windows11结果。
- 原有 Json.NET 高危告警仍为主程序正式发布前待处理项，与本 PoC 编译成功不冲突。

## 本轮实测补充

用户确认双浏览器媒体实际停止，并按要求准备 Edge 继续播放、Chrome 保持暂停；工具随后验证该前置状态再发送暂停。两组测试命令均 exit=0。TryPauseAsync 返回 true 后立即读取仍可能为 Playing，延后1秒重新枚举才为 Paused；产品化不能把命令返回成功等同于状态已经同步刷新。

最终便携验证包保存在 artifacts/gsmtc/20261116-163305-804/GsmtcPoc-net45-validation.zip。ZIP只有最终 bin 的 EXE/config/PDB；旧构建含 Windows.winmd 的包保留为历史本地证据，不用来分发。当前没有对外发布，正式分发前仍需完成相关许可审查。

无需升级项目框架即可开始媒体模块设计，用户已于2026-11-16明确批准Windows11延期并继续Phase2，按该最新指示推进独立模块。跨系统步骤见上文；未将等待环境误写为功能实现失败。
