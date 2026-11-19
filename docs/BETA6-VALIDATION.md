# beta.6 本地验证

2026-11-19，Windows10 22H2。最终候选 `artifacts/test-release/20261119-203703-992-1.0.0-beta.6`。源基线a5df0d9，输入指纹EE2F297D09D77CC8EFD2B8D8075E3FBB242DA0DAEACDA465A3DF0BD32CDAC9D8。构建后inputs.json逐文件复核无漂移；验收后新增本报告，不重建候选。

## 验证结果

| 项目 | 结果 | artifacts证据 |
| --- | --- | --- |
| net45 Release四项目和独立维护EXE | 退出0，原依赖/Fody/Costura/SQLite保留 | baseline/20261119-203704-061-Release及候选日志 |
| WPF/20轮休息/设置/置顶保护 | 205断言通过，退出0 | app-integration/20261119-203752-977 |
| 媒体保护 | 52通过、0失败 | media-control/20261119-203830-504 |
| 完整Inno安装卸载 | 44断言通过 | installed-release-tests/20261119-203838-619 |
| 无备份维护/重试 | 34断言通过 | maintenance-tests/20261119-204224-457 |
| ZIP与安装payload | 36文件（含manifest），哈希匹配，无README/CHANGELOG | 候选及安装测试日志 |

安装测试使用同源代码、专用产品/HKCU/快捷方式常量，保护用户实际安装。覆盖新装、beta.2–beta.5升级、同版本重装、三轮同路径卸载重装，配置统计哈希一致；最终Inno登记后不留中断记录，安装/升级/卸载前后没有增加Recovery目录。x86/x64 PowerShell和相关注册、用户实际安装目录/配置及已有快捷方式前后快照一致。用户实际beta.5未升级。

维护测试覆盖复制失败在替换前停止、替换/登记失败仅保留元数据、强制进程退出及半截暂存文件、伪造根路径拒绝、同包重试完成、数据哈希一致、回滚入口拒绝。还覆盖保护路径、标记、篡改、运行中、reparse、独占文件拒绝及其他安装历史目录保留。成功后暂存目录和中断记录移入系统回收站，未复制旧payload或Data/Log。

## 用户已有目录清理

`C:/Users/SeanCheng/AppData/Local/ProjectEyeEnhanced-Recovery` 原含一份20261119-115312-250-upgrade事务，State=Committed，38个旧文件共约36MiB，不含Data/Log。逐文件哈希和产品标记已核验；旧manifest与beta.5可信内容一致，已记录其原始哈希7E3B6D565EDF953AF353064F423A58DABDCC08A5A66D180A53B5401E46D4AADC。

使用本轮维护工具的独立`--cleanup-history`核验后将该精确备份送系统回收站，空恢复根同样回收。`legacy-recovery-cleanup/20261119-202743-821`保存结果与实际安装前后文件哈希，完全一致。测试完成后再次核对根目录不存在。清理可从系统回收站找回，未永久删除，也未清空回收站。

## 失败记录与限制

一轮beta.4升级出现中途拒绝，保留中文错误和中断记录；同包修复后成功卸载，最终全矩阵复测通过。底层瞬时原因未可靠复现，不将其写成已证明根因。一次连续重装遇到E_LOCKED，按设计拒绝；占用释放后完整维护复测通过，保留204041-358失败记录，未取消占用保护。

诊断脚本初稿用Windows PowerShell5.1无BOM读取中文路径，已改UTF-8 BOM后针对精确夹具完成复核；发生在artifacts诊断文件，未损坏仓库文件或用户数据。早期候选全部保留，不作为最终交付。

用户明确移除安装回滚能力：失败后只能重运行同包修复，故障发生时旧程序可能已部分回收；数据不参与替换。强制断电等造成状态记录本身损坏时仍会停止，避免猜测覆盖。系统回收站不等同应用恢复功能，最终清理由用户决定。

Windows11、长期稳定性、HiBit隔离深度清理、既有真人Monitor伸缩/播放器及安装窗口完整目视验收仍按前轮待验；自动夹具不代替真人结果。Json.NET告警未处理。未推送、未正式发布；用户自己运行最终候选升级。

## SHA256

```text
D4CD53B5802E27037C03FC3A6CBF6F275D55DAC4BCCA40A1810FBBB506022544  ProjectEye-Enhanced-1.0.0-beta.6-Portable.zip
E10452868A482C5DE42E8329F5208D4000E1BA866962093BC15E1A630BF2536A  ProjectEye-Enhanced-1.0.0-beta.6-Setup.exe
```

使用说明位于包旁，维护行为见[BETA6-NO-BACKUPS.md](BETA6-NO-BACKUPS.md)。旧beta.1–5包保留，安装payload仍不含README/CHANGELOG，必需许可证保留。
