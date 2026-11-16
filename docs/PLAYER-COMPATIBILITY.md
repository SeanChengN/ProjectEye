# 桌面播放器兼容性验证（Phase 5）

日期：2026-11-16。系统：Windows 10 Education 22H2 Build 19045.6456。用户已确认网页播放测试通过，并打开Feishin、网易云音乐和PotPlayer，随后明确确认三个播放器正在播放测试内容。

## 首轮实际观察

| 播放器 | 本机版本证据 | 进程存在 | 当前GSMTC会话 | 暂停/恢复结果 |
| --- | --- | --- | --- | --- |
| Feishin | Product 1.17.0.0 / File 1.17.0 | 是 | 未发现 | 未发送命令，未验证 |
| 网易云音乐 | 3.0.15.202965 | 是 | 未发现 | 未发送命令，未验证 |
| PotPlayerMini64 | EXE Product/File均为0，需关于界面确认 | 是 | 未发现 | 未发送命令，未验证 |

独立Phase1 PoC和当前媒体模块均正常枚举到2个会话：Edge Paused、Chrome Paused，没有以上三项。两种调用均exit=0，未向播放器发出任何命令，也没有模拟媒体键或结束进程。进程存在和用户确认播放并不证明播放器已向Windows注册SMTC；本轮结论仅为当前版本/配置下未公开会话，不能宣称这些播放器永远不支持。

原始证据保存在artifacts/player-compatibility/20261116-183736-690：independent-enumeration.log、module-enumeration.log、result.json。日志不含媒体标题/艺术家或播放路径。

## 设置核对

已请用户检查各播放器的系统媒体控制/SMTC设置，有则开启后继续播放。未修改播放器设置或安装插件。

Feishin维护者明确给出入口Settings → Hotkeys → Enable Media Session：<https://github.com/jeffvli/feishin/issues/1936>。官方开发分支文档也列出playback.mediaSession设置（该开发分支默认值不代表本机1.17的实际配置）：<https://github.com/jeffvli/feishin/blob/development/docs/ENV_SETTINGS.md>。

网易云和PotPlayer未核实可靠官方说明，不据第三方搜索摘要修改配置。等待用户说明开关和实际PotPlayer版本后再枚举；只有真实会话出现才能进行控制验收。

## 控制验收边界

测试只控制用户准备的目标会话；浏览器保持暂停，不发送控制命令。对原本暂停/停止会话不发送暂停。恢复仍按用户明确选择的最保守规则：重复Paused通知或其他无法确认状态使本次会话仅暂停不恢复。未注册GSMTC时记录为当前配置不可控制，不回退模拟按键。

## Feishin会话出现后的实际控制结果

后续枚举发现Feishin.exe的真实会话为Playing，Edge和Chrome仍为Paused，网易云和PotPlayer仍无会话。测试仅按Feishin.exe精确应用标识选择该原生对象，之后重新枚举也只保留相同对象引用，不会控制浏览器或测试期间新出现的其他会话。没有在产品代码中引入播放器进程白名单。

Feishin 1.17.0：成功暂停记录1条，1秒后状态Paused；IsSameSessionAsync=true。由于重复Paused通知触发ResumeGuard:Playback:Paused，CanAutoResume=false，TryPlay未调用，结果按用户接受的严格策略为仅暂停。重复暂停仍仅1条记录，两个浏览器会话均unchanged=true。测试入口exit=0表示安全暂停及保守降级符合当前验收，不表示自动恢复成功。用户实际听觉确认待回复。

证据：artifacts/media-control/20261116-184043-785/feishin-cycle-*.log及退出码。该目录构建及52项回归全部通过。原有初始未发现Feishin的观察仍保留，说明注册状态会受运行配置/启动播放影响；不能将首次未注册误写为永久不支持。

新增测试入口：`MediaControl.Tests.exe --player-pause <exact-app-id>...` 或 `--player-cycle <exact-app-id>...`。没有精确匹配且唯一的注册会话就返回3，不发命令。cycle只在严格资格允许时恢复；符合资格但TryPlay失败仍计错误，不能将失败泛化成接受的降级。日志不输出媒体标题、艺术家或路径。

追加安全检查：artifacts/media-control/20261116-184156-007通过52项回归。Feishin已为Paused时再次运行--player-pause，expectedPlaying=0、successfulRecords=0，仍Paused，两个浏览器unchanged=true。不存在的精确应用标识返回3并明确no command sent，没有向其他会话发送命令。产品模块未修改。
