# Phase 6 — 本地测试版

当前交付beta.4，安装/卸载规则以[BETA4-MAINTENANCE.md](BETA4-MAINTENANCE.md)为准。beta.3保留为历史，卸载默认保留数据的规则已由用户取消。以下beta.1记录保留历史证据，其中PowerShell卸载入口已废弃，不应再使用。

用户于2026-11-16批准先制作1.0.0-beta.1，24小时运行由用户实际使用验证，不在Codex等待。该授权适用于本地测试包，不包含推送、GitHub发布或正式Release。Phase5未验证项仍保留。

## 构建入口

发布配置：packaging/test-release.json。驱动：scripts/Build-TestRelease.ps1。使用已有且数字签名有效的 Inno Setup ISCC.exe；本轮为6.2.2。编译器只是构建工具，不随应用打包，也不增加用户运行依赖。

```powershell
./scripts/Build-TestRelease.ps1 -Plan -IsccPath '<ISCC.exe绝对路径>'
./scripts/Build-TestRelease.ps1 -IsccPath '<ISCC.exe绝对路径>'
```

先运行plan模式，记录源提交、源文件SHA256指纹、编译器哈希、空间及输出目录。构建调用既有隔离Release入口，不Clean/Rebuild、不安装全局组件、不修改依赖版本。每次产生新候选目录，不复用未经验证的旧包。产物、命令、日志和哈希留在artifacts/test-release，不提交。源提交之外的未提交构建输入由inputs.json和inputFingerprint完整区分。

## 测试版行为

- 产品/关于页/托盘标识为ProjectEye Enhanced，产品版本1.0.0-beta.1；原上游1.4.3来源及版权保留。
- 独立mutex为projecteye-enhanced，快捷方式为ProjectEye Enhanced。原版可保留，测试时建议只运行一套提醒。
- 启动时把工作目录固定为应用目录，避免从其他目录启动使相对SQLite数据写入错误位置。
- 默认媒体暂停、恢复仍关闭；不夹带任何验收配置或用户Data/Log。
- 上游更新页保留说明，下载/安装命令停用，不请求上游版本，也不分发ProjectEyeUp.exe。
- ZIP与安装器使用相同payload清单；保留x86/x64 SQLite原生资源、共享UI和错误提示工具，不包含PDB、SDK元数据或引用程序集。
- 安装器为当前用户、不需要管理员、默认独立版本目录，不接受覆盖已有目录或重复注册同一测试版。没有静默运行应用。
- 卸载脚本核验包标记、所属目录和进程，先要求正常退出；程序及Data/Log进入系统回收站，仅移除目标属于本安装的快捷方式、启动项和注册。没有永久删除应用数据。

## 验证和交付边界

已验证完整Release构建、52项媒体回归、实际WPF集成（20次计时周期、旧配置、媒体绑定及保存/重载），新增beta版本与上游更新停用断言通过。安装器实际释放的38个文件与同源payload逐项哈希一致，包括清单文件；重复安装返回7拒绝覆盖。Windows PowerShell5.1卸载入口将本任务验证目录连同数据标记送入回收站，注册与所属快捷方式移除。

同版本覆盖升级被明确禁止；跨beta版本数据迁移及正式安装/卸载/升级全矩阵未完成，所以PLAN.md相应正式验收条目不勾选。安装器未签名；Windows11、不同DPI和长期稳定性未验；既有Newtonsoft.Json12.0.2 NU1903未屏蔽。原版DEBUG加速计时只在Debug编译，测试包使用Release，不把它当成未清理的发布调试代码执行。

使用说明见[测试版说明](TEST-VERSION.md)，已知播放器限制见[兼容性](PLAYER-COMPATIBILITY.md)，第三方许可文本随包附带。上游素材逐项权利来源审计仍为正式发布待办。正式Release须用户后续明确确认。

## 本轮证据

最终候选：artifacts/test-release/20261116-191450-590-1.0.0-beta.1，plan.json、inputs.json、result.json、artifacts.json及SHA256SUMS.txt记录工具、源输入和包哈希。对应完整Release：artifacts/baseline/20261116-191450-657-Release。52项回归：artifacts/media-control/20261116-191016-263；WPF集成：artifacts/app-integration/20261116-191135-456。产品源码与最终候选相比集成测试版本没有再改动；最终只加强安装冲突与启动项移除。

最终候选安装验证见installation-verification.json：38文件、哈希一致，重复安装exit=7，Windows PowerShell5.1卸载exit=0，目录连数据标记进入系统回收站，注册清除。前一次最终安装验证命令在产生安装文件前进程中断；检查无安装文件及注册后执行单次重试成功，未覆盖任何目录。中间候选原样保留，不作为最终交付。

最终ZIP的独立解压副本已从系统TEMP工作目录启动，进程正常响应，Data/config.xml与SQLite库均位于应用目录，WarnTime=20、RestTime=20，AutoPause/AutoResume=false，产品版本1.0.0-beta.1；没有应用异常日志。startup-result.json保留证据。该副本暂时留在托盘供用户查看，安装正式测试目录前从托盘正常退出即可；分发ZIP本身仍不含Data/Log。

## beta.2 增量

2026-11-16新增结束休息显示桌面，方案及本轮证据见[桌面功能说明](DESKTOP-PRIVACY.md)。当前配置和打包脚本版本改为1.0.0-beta.2，beta.1历史包保留；正式发布仍未执行。
