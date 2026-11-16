# beta.3：安全维护和覆盖升级

版本唯一输入为 `packaging/test-release.json`；构建驱动为应用及维护EXE生成版本，Inno通过ReleaseVersion参数读取同一值。目标仍为net45；既有应用依赖未更换。维护工具独立构建，不改变原四项目解决方案。

## 用户操作

先从托盘正常退出，再运行本地Setup。安装器按稳定测试注册、beta.2、beta.1顺序检查HKCU两个视图，并以产品标记选出默认路径；维护EXE随后对标记、可信manifest和每个程序文件哈希做最终验证。当前旧目录可继续使用，不执行旧卸载命令。新安装默认 `%LOCALAPPDATA%/Programs/ProjectEyeEnhanced/Test`。便携ZIP解压到新目录使用。

覆盖和同版本修复先把旧目录全部文件、配置、统计、标记、相关注册和快捷方式备份到 `%LOCALAPPDATA%/ProjectEyeEnhanced-Recovery/<时间>-upgrade-<id>`，逐项核验文件和快捷方式哈希。候选文件先复制为同卷临时文件并校验，随后替换已知程序；Data、Log和未知文件不覆盖，出现同名未知候选文件时拒绝。只有旧可信清单里的Remove-TestVersion.ps1才会退役，其备份保留。安装后校验全部payload，再写稳定注册和快捷方式；只迁移InstallLocation规范化后等于本次目录的旧注册。

卸载直接运行ProjectEye.Maintenance.exe（或应用和功能）。UninstallString与QuietUninstallString均以此EXE开头，不启动PowerShell、cmd、wscript或旧更新器。WScript.Shell仅作为进程内COM接口操作快捷方式，不启动脚本宿主进程。所有运行中实例需正常退出，工具不会结束进程。

卸载前验证嵌入可信清单、自身哈希和安装产品版本，再复制同产品助手到恢复目录，核验哈希并通过ticket交接；助手等待原卸载进程退出。Data、Log移入独立恢复目录并验证哈希，已知程序送系统回收站；跨盘搬移使用复制、校验、回收原件。未知文件原地保留并写入unknown-files-preserved.json。保留原目录、恢复数据、助手和ticket作诊断，不把它们递归当残留清理。

工具只移除目标确属本安装的注册及快捷方式。系统目录、盘符根、网络目录、重解析点、路径穿越、伪造产品标记、改动过的程序均会拒绝。清单哈希是本地可信版本绑定，不等同代码签名，无法防御可任意修改同一用户进程/EXE的攻击者。

## 回滚

停止应用，使用本次同源便携包维护EXE：

```powershell
& '便携目录/ProjectEye.Maintenance.exe' --rollback --transaction '完整upgrade恢复目录'
```

同源包必须与该事务的嵌入manifest对应。事务在transaction.json中记录状态、原文件/哈希、替换文件及快捷方式、注册快照；失败自动执行同一回滚。只回收本轮可信替换文件，恢复其旧副本及退役PS1，不回写Data/Log。若当前文件或备份已被修改则停止，保留候选与恢复目录供人工核对。每次修复都建立独立备份，不覆盖上次唯一副本。

回滚到beta.2会恢复其旧注册和PS1，这是还原语义，**不要再运行旧卸载入口或HiBit深度清理**；应重新安装beta.3。恢复目录可能包含个人配置与日志，请自行保管。

## 验证入口

- `scripts/Test-Maintenance.ps1 -Candidate <成功候选>`：相同维护代码用编译常量隔离HKCU/快捷方式，不分发测试构建。覆盖旧注册迁移、失败回滚、路径/标记/文件/运行中拒绝、跨盘数据保留和助手卸载。
- `scripts/Test-InstalledRelease.ps1 -Candidate <成功候选>`：真实Setup和分发维护EXE，新装、beta.2文件原地升级、同版本重复安装、真实卸载及每个释放文件哈希；前后扫描系统x86/x64 PowerShell、有关注册、实际旧版安装及快捷方式。为避免触碰用户旧注册，迁移写入本身由前项专用注册分支验证。
- `scripts/Test-MediaControl.ps1`：52项媒体保护回归。
- `scripts/Test-AppIntegration.ps1 -BuildDirectory <基线构建>`：旧配置、设置保存重载、WPF提醒、20次休息及双屏独立窗口/桌面超时回归，只允许操作精确测试窗口。

实际用户安装未覆盖，由用户执行最终候选；测试日志保存在artifacts，不提交。完整测试结果和最终候选见[BETA3-VALIDATION.md](BETA3-VALIDATION.md)。

HiBit深度清理没有可还原隔离Windows，明确未验证。应用无法约束第三方清理器额外行为，推荐自带卸载；beta.1/beta.2原包保留但不建议再运行其旧卸载。Windows11、24小时稳定性和原Json.NET安全告警仍待后续处理，本轮不夹带系统修复，不发布正式Release。
