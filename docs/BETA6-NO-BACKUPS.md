# beta.6：无安装恢复备份

用户2026-11-19明确取消旧程序恢复功能。beta.6不再创建ProjectEyeEnhanced-Recovery，不复制旧程序、Data、Log或统计，不支持`--rollback`。旧版本文档中的“自动回滚”“独立升级备份”仅作为历史记录。本轮目标net45和业务依赖保持不变；beta.5置顶/Monitor保护、常显滚动条及精简包继续适用。

## 安装与重试

新装、覆盖升级、同版本重装使用同一流程：校验候选、安装目录、旧可信清单、reparse/路径边界、单实例运行状态、待替换文件占用、稳定注册与快捷方式冲突；读取旧清单作身份依据，不复制旧payload。Data、Log和未知文件不在替换集合内。

安装目录内生成`.projecteye-install-state.json`（Product、Root、CandidateHash、PreviousManifest、StageName），仅元数据与旧清单，没有旧程序字节、配置、统计或注册备份。新文件在同目录`.projecteye-stage-<随机ID>`全部复制并按候选哈希校验；准备完毕才逐项回收旧程序、移动新文件。精确可信退役文件规则继续用于README/CHANGELOG和旧PS1。正常成功/异常退出回收暂存目录；进程强制中断留下的新文件暂存可由同包识别校验后回收重建；部分写入文件须为候选内容的逐字节前缀。

失败不恢复旧版本。中文提示要求重新运行**同一安装包**。再次执行时要求状态根路径匹配、候选哈希匹配，旧清单哈希为发布配置记录的可信旧版之一或当前候选。混合目录中每个待替换文件必须匹配可信旧版或新候选，缺失项允许修复，额外篡改拒绝；不能凭伪造标记允许覆盖未知文件。不同候选必须先完成当前修复，不覆盖未完成的另一次版本。

Inno在PrepareToInstall执行`--install --defer-finalize`；文件写入后状态暂不移除。Inno建立自己的卸载EXE，再由ssPostInstall `--register`校验、登记稳定入口并完成收尾；成功才回收状态和已验证历史记录。直接维护安装不加defer时自行完成收尾。全程不使用PowerShell/cmd/wscript或旧更新器。

CLI继续提供`--install --source --target`、`--check`、`--register --target`、`--check-uninstall --target`、`--remove --target`；新增`--cleanup-history --target`仅校验已安装产品、回收可信旧目录，不执行安装。不接受回滚。结果JSON仍有recovery字段但始终null，中文`.txt`继续供Inno显式UTF-8解码。

## 历史目录与卸载

旧Recovery路径只作为只读历史来源，不调用CreateDirectory。清理只回收规范化安装路径一致、产品/旧清单/每文件哈希匹配的旧事务；不回收其他安装记录，未知或改变的记录保留提示。成功安装和卸载使用同一定向清理。恢复根空时回收根；清理失败不假报完全清理。

卸载继续回收整个已验证专用安装目录；Inno自己的开放记录留到标准退出阶段收尾。用户数据没有应用外恢复副本，系统回收站由用户决定是否最终清空。本工具不永久删除文件。

## 验证边界

脚本在隔离目录与专用HKCU/快捷方式编译常量下验证复制失败、替换失败、注册失败、强制进程中断、篡改/伪造重试记录、同包重装、数据哈希、旧备份归属和不创建Recovery；完整Inno覆盖新装、beta.5升级、重装/卸载。前后核对系统PowerShell、实际安装和用户配置不变。最终证据见[BETA6-VALIDATION.md](BETA6-VALIDATION.md)。

Windows11、长期稳定性、HiBit隔离深度清理及既有真人窗口/媒体待验项继续按原记录，不把自动夹具通过说成真人通过。不推送或正式发布，最终覆盖由用户运行安装包。
