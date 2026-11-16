# beta.3 本地候选验证

2026-11-16，Windows 10 22H2。仅本地测试交付，不推送、不正式发布；用户实际安装未覆盖。完整Framework MSBuild、固定net45引用程序集1.0.3和Inno6.2.2；应用框架和依赖版本保持不变。

## 最终候选

`artifacts/test-release/20261116-231159-491-1.0.0-beta.3`

- 安装EXE及便携ZIP以同源payload组装，39个文件（含manifest）逐项校验。
- 基线Release：`artifacts/baseline/20261116-231159-559-Release`，四项目构建退出0；Fody/Costura正常，SQLite双架构资源保留。维护EXE另行net45构建退出0。
- plan/inputs/result/artifacts/SHA256SUMS记录源HEAD、未提交输入指纹、工具哈希、阶段时长、产物清单。构建后对inputs.json逐项复核，所有发布输入未变化；本报告在验收后新增，不进入payload。
- 本轮源基线为2a5b92c，发布输入指纹33DBE548153ECFEF5411F4BB25BA156DBF807650181BDB376D9FB0A3246AE693。最终独立提交包含对应源码和验收文档，包不因为文档提交再次重建。

```text
424CE231F5B73832B92CE28111C0AE9C7C56EBD73BFD3C34BB72AFFD38511557  ProjectEye-Enhanced-1.0.0-beta.3-Portable.zip
BDA7E1216070EEE0519C92ED98E3039FFA800F8FFF00FE092329CF044E759EF5  ProjectEye-Enhanced-1.0.0-beta.3-Setup.exe
```

## 自动验收

| 验收 | 结果 | 证据目录（artifacts下） |
| --- | --- | --- |
| 维护安全与事务 | 25项断言通过 | maintenance-tests/20261116-231250-660 |
| 真实Setup/卸载EXE | 19项断言通过 | installed-release-tests/20261116-231249-665 |
| 媒体保护回归 | 52通过、0失败 | media-control/20261116-230234-524 |
| 最终应用WPF/桌面集成 | 82项断言通过、退出0 | app-integration/20261116-231414-065 |

维护验收覆盖可信manifest与穿越拒绝、系统根拒绝、伪造标记、旧版文件升级、旧注册两个视图迁移、旧版升级后注册失败回滚、复制失败回滚、同版本修复、单实例运行中拒绝、程序篡改拒绝、重解析点拒绝和自有助手卸载。配置/统计升级及卸载后哈希一致，未知文件保留；跨E/C盘搬移实际覆盖。旧注册迁移在编译隔离的测试注册分支验证，与分发版共用维护实现，仅注册和快捷方式常量不同。

真实分发Setup测试在独立目录完成新装、beta.2原地升级、beta.3重复安装；每次释放39文件匹配最终payload。真实卸载入口直接为ProjectEye.Maintenance.exe，助手自行等待及移除程序文件，数据与日志移到独立恢复目录，未知文件保留。前后protected快照中x86/x64 PowerShell树、PowerShell注册和类关联、用户旧beta注册、实际beta.2全目录与已有快捷方式完全一致。真实测试不改用户旧注册；没有执行旧卸载命令。

WPF回归包含20轮休息、设置保存重载、旧配置、共享结束去重、桌面默认关闭/设置快照/提前跳过/取消/异常超时、精确独立测试窗口、双屏、最大化/已最小化/无响应窗口与遮罩/任务栏排除。此轮不重新控制用户播放器；实际播放器兼容与人工双屏媒体表现沿用之前记录，不宣称新增人工验收。

## 失败记录与修正

首轮Inno编译发现不支持SetRegView，改用HKCU32/HKCU64。真实安装首轮发现ssPostInstall异常仍返回成功；移到PrepareToInstall并保留详细拒绝结果。诊断随后确认CreateAppDir=no导致{app}变为Windows目录，安全校验正确拒绝；恢复目录选择语义后最终候选通过。测试启动参数对盘符根末尾反斜线的引号问题修正后通过。所有中间候选与失败日志保留，不作为交付包。

## 保留与限制

旧beta.1/beta.2原包原样保留。源码旧PS1已移至`.recycle-bin/beta3/packaging/Remove-TestVersion.ps1`，不再分发。程序移除使用系统回收站，恢复目录和测试证据保留，无永久删除源码或用户数据。

HiBit深度清理在可还原隔离Windows中**未验证**；不能保证第三方清理器安全。推荐应用自带卸载，不再使用旧包的PowerShell卸载。Windows11、24小时长期稳定性、签名及已有Json.NET高危告警仍待后续。系统未恢复项与本轮安装器改造独立，本包不尝试修复系统。

使用与回滚见[BETA3-MAINTENANCE.md](BETA3-MAINTENANCE.md)。先托盘退出，运行最终Setup并核对其默认旧目录；Data和统计无需手动搬迁。失败保留恢复记录，勿运行旧卸载。
