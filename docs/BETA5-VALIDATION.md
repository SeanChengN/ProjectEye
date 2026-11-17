# beta.5 本地验证

2026-11-17，Windows10 22H2。最终候选 `artifacts/test-release/20261117-150529-094-1.0.0-beta.5`。构建源基线e9e6fd6，输入指纹1F5969F0F01C4E11AE1BECAF9ACE1ECE288882AD671E0DF1298B9CEFE7817231；构建后inputs.json逐项复核无漂移。本报告在验收后新增，不进入payload。

## 结果

| 项目 | 结果 | artifacts证据 |
| --- | --- | --- |
| net45完整Release和独立维护EXE | 退出0，Fody/Costura及双架构SQLite保留 | baseline/20261117-150529-154-Release及候选构建日志 |
| WPF/桌面/设置集成 | 205断言通过，退出0 | app-integration/20261117-150611-102 |
| 媒体保护 | 52通过，0失败 | media-control/20261117-150016-654 |
| 完整隔离安装/卸载 | 37断言通过 | installed-release-tests/20261117-150647-020 |
| 维护与失败回滚 | 27断言通过 | maintenance-tests/20261117-150823-195 |
| 包文件 | 36文件，含manifest；无README/CHANGELOG/.gitattributes | 最终payload、zip-verification及安装释放哈希 |

桌面新增有标题栏置顶窗、非任务栏窗、无激活窗、枚举后切换置顶及隐藏夹具；普通/最大化窗口两屏3目标成功最小化，受保护夹具未改可见性/任务栏。当前实际Monitor六个原生窗口在新后端只读枚举中均排除，commands=0；未发送最小化、恢复、样式写入或关闭命令。20轮休息、超时边界、遮罩/任务栏排除、设置保存重载及滚动条回归通过。

安装测试使用同源代码，编译隔离产品标识/HKCU/快捷方式，未替换当前用户安装注册。覆盖三轮重装、beta.2/3/4升级、重复安装、锁文件中文错误、数据哈希及系统PowerShell快照。beta.4升级回收README/CHANGELOG和licenses/.gitattributes，保留LICENSE。注入注册失败的升级回滚恢复旧文档，已按旧包哈希核验。未知文件仍保留；新payload按明确运行文件清单组装，许可证txt及安全标记/清单保留。README/CHANGELOG只存在候选包外，不进入安装文件或便携ZIP。

## 故障记录与边界

首轮逐窗Process.ProcessName调用耗尽200ms预算，attempted=0；诊断后改OpenProcess有限查询和QueryFullProcessImageName，并前置样式筛选。修正后普通窗口及保护夹具全部通过，未延长时间预算。原始失败目录和中间候选保留，不交付。一次测试路径手工抄录错误在启动应用前即停止，之后从result.json读取正确构建目录完成。

**真实Monitor完整护眼周期后的伸缩和任务栏状态仍需用户复测。** 只读排除与夹具通过不能替代该项。本轮不修改Monitor仓库、不强制用户播放器或窗口状态，也不自动升级当前实际安装。若启用置顶的播放器保持可见，这是用户选择的置顶排除行为。

Windows11、长期稳定性、HiBit可还原隔离深度清理、前轮安装/卸载窗口完整目视和真实DPI/拖动检查继续待验；已有Json.NET告警保留。目标框架和业务依赖未升级，不推送或正式发布。旧beta.1–4原包保留，未永久清理构建缓存或回收站。

## SHA256

```text
5BE079C7DC0A85DD3A3C49CA928873E0C83B628C336E74D1F9B0DDF9F4B8ECEF  ProjectEye-Enhanced-1.0.0-beta.5-Portable.zip
A229FC4B236CA8D624CEFA3B37D26CB2ED92FBC9A306C142D372105FEAB92E81  ProjectEye-Enhanced-1.0.0-beta.5-Setup.exe
```

窗口规则和升级说明见[BETA5-WINDOW-PROTECTION.md](BETA5-WINDOW-PROTECTION.md)，用户说明在候选包旁的“使用说明.md”。托盘退出当前实例后运行最终Setup覆盖升级，配置/统计保留；卸载仍按beta.4规则回收整个专用目录及可靠归属的备份。
