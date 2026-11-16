# Phase 2 — 独立媒体控制模块

## 状态与边界

2026-11-16 用户明确指示“后续先忽略windows11环境，继续Phase2”。Windows 11 保持未测试，不再阻挡本阶段；不宣称跨系统验收通过。独立模块完成，没有接入原版应用的服务注册、休息事件或设置，也没有改变主程序源码、框架和既有依赖。

模块为 net45 类库 `modules/MediaControl`，真实适配器使用 Phase 1 已验证的 SDK Windows.winmd 和 Framework WinRT bridge；不引入第三方 NuGet。测试为 net45 控制台运行器，假会话测试无需操控播放器。Phase 3 才实现休息会话协调与安全自动恢复。

## 接口与行为

- `GetMediaSessionsAsync(scope, cancellationToken)`：默认所有会话，也可用 CurrentSession（GSMTC GetCurrentSession），返回只读快照，不发送播放控制。
- `PausePlayingMediaAsync(scope, cancellationToken)`：只处理 Playing 且支持 Pause 的会话，调用前再检查一次；仅 TryPauseAsync 返回 true 才保存成功记录。原本 Paused/Stopped、不支持控制、返回 false 的会话不计成功。
- `IsSameSessionAsync(snapshot, cancellationToken)`：要求同一个服务和未释放的批次、成功暂停记录、原生对象引用相同、完整指纹相同。相同媒体元数据但不同对象仍返回 false，避免重开播放器或多窗口歧义。
- `ReleasePausedMediaAsync(cancellationToken)`：上层明确结束批次后清理当前成功记录和去重信息，使旧快照失效。存在迟到的 API 时拒绝释放；不执行恢复。
- `Dispose()`：幂等取消服务寿命令牌，不阻塞 WPF dispatcher，不恢复媒体；已运行且忽略取消的原生请求可能继续完成，不能宣称已撤销。

IMediaSessionBackend/IMediaSession 将策略与 WinRT 调用隔离，适配器支持注入测试。方法使用 Task 和 CancellationToken；等待队列或入口前已取消时遵循 Task 取消语义，调用方需要处理 OperationCanceledException。运行中的取消会保留此前已确认成功的记录，避免半途成功丢失。

MediaSessionSnapshot 保存原会话对象引用、应用标识、Title/Artist/Album、原播放状态、指纹、PauseSucceeded；属性只读。元数据仅在本机内存中使用，不持久化。SHA256 指纹使用长度前缀编码，避免字段拼接歧义；缺少应用标识或标题时不能验证身份。元数据读取普通异常时仍允许暂停，但不允许身份验证；元数据超时则中止本次操作，以免并发重叠。

`CanAutoResume` 本阶段固定 false。对象与内容匹配不能证明用户未主动修改播放状态；不提供 TryPlay 或自动恢复入口，留待 Phase 3 根据状态变化监听和休息周期管理实现。

## 异常、取消和并发

一个服务实例对应一个上层批次，SemaphoreSlim 串行化枚举/暂停/身份验证/清理。每个原生操作（含同步工厂）通过线程池执行，默认5秒超时，避免 WPF dispatcher 等待。不会通过模拟按键、进程白名单或结束进程兜底。

同一批次内按对象引用去重；即使播放器暂未把成功暂停反映到状态，也不会再次暂停同一对象。返回 false 的命令可重试；异常、取消或超时可能已经发送命令，保守保留去重标记。超时后若原 Task 尚未完成，新操作立即返回保留的成功记录并给出 OperationStillPending，不启动其他 API；迟到结果不计作已确认成功，也不自动重试该会话。完成后由上层明确释放批次才允许重新处理。

初始化失败和普通会话异常转换为诊断，单个会话失败不影响其他会话；超时/取消结束当前遍历。诊断回调本身抛异常也不会影响媒体逻辑。MediaDiagnostic 仅暴露事件代码、异常类型、HRESULT，不含异常消息、应用标识、标题、艺术家、指纹或原始日志。日志持久化/开关在后续设置集成阶段处理。

## 构建与测试

```powershell
./scripts/Test-MediaControl.ps1
./scripts/Test-MediaControl.ps1 -Live
```

脚本通过 vswhere/Windows SDK 注册表发现工具，用固定 net45 引用包及已校验 SHA256，在新 artifacts/media-control 时间戳目录复制模块和测试源码，构建、运行32项测试。-Live 只读枚举真实会话并在内存读取元数据，不打印完整媒体信息。SDK WinMD 不复制进输出。脚本保存源码副本、构建/测试/枚举日志、退出码、工具版本、产物 SHA256；不执行删除或清理。

显式真实暂停验收入口为测试 EXE 的 `--live-pause`，专门要求恰好两个会话（Edge Playing、Chrome Paused），前置状态不满足就返回3且不发送命令。它验证成功记录、延后状态、原对象/指纹和重复请求；这个固定场景限制仅属于测试程序，库本身没有进程名规则。

| 验证 | 结果 |
| --- | --- |
| net45 类库及测试构建 | 成功，无编译警告和错误 |
| 32项单元测试 | 全部通过，Failed=0 |
| 无会话、Playing/Paused/Stopped、多会话和主要会话范围 | 通过 |
| 不支持暂停、返回false、状态临时变化、会话异常、初始化异常 | 通过 |
| 元数据缺失、指纹变化/拼接碰撞、会话消失/替换、外部/失效快照 | 通过 |
| 并发12次请求、重复枚举对象、状态刷新延迟、超时迟到结果 | 通过 |
| 元数据超时、同步工厂阻塞、部分成功后取消、释放批次及Dispose | 通过 |
| 日志回调异常隔离和不暴露元数据 | 通过 |
| 真实适配器读取 Edge/Chrome | 成功 |
| 用户准备的 Edge Playing / Chrome Paused | 仅Edge成功暂停1次，1秒后两者Paused，identityMatches=true，canAutoResume=false，重复后仍为1条记录，exit=0 |

最终证据：`artifacts/media-control/20261116-164628-169`，包含 tests.log、live-enumeration.log、live-pause.log、live-pause-exit-code.txt、result.json、output-manifest.json。早期编译发现适配器缺少 System 命名空间，修正后通过，失败记录保留。

## 后续事项

Phase 2 清单完成。Phase 3 需要接入 RestStart/RestCompleted，冻结周期设置、处理重复事件、等待暂停结束、监听用户媒体变化、保守恢复及退出取消。当前模块不注册进主程序，不主动运行，不会改变现有护眼行为。

Windows 11、非浏览器播放器以及真实播放器不支持/突然退出等兼容场景仍待后续实机验证；已有单元测试不替代对应实机结果。原 Json.NET 漏洞仍未修复，不因此扩大本阶段改动范围。
