# Phase 3 — 休息周期媒体集成

2026-11-16：接入原版 RestStart/RestCompleted，添加 RestCancelled、周期 Guid 和 IsResting，重复开始不重置倒计时；跳过和离开取消，退出取消并释放媒体监视。原休息界面、计时器和正常完成提示音保持原路径，媒体任务异步独立执行，失败不传播到 WPF。

配置新增 Media，默认 AutoPause=false、AutoResume=false、Scope=0（所有会话）、Logging=true。旧 XML 缺少该节时通过默认构造兼容；周期开始复制设置，修改设置不影响当前批次。日志仅记录UTC时间、事件代码、异常类型/HRESULT，每日文件达到1MiB即停止追加，不覆盖或删除。

自动恢复要求：本软件成功暂停记录、当前批次、原会话对象、指纹相同、Paused、监视持续有效、明确开启恢复。监视在暂停前注册；第一次暂停转换后任何额外播放通知或媒体变化都使资格失效，重复Paused通知也保守作废。恢复资格只消费一次；失败或超时不自动重试。GSMTC不能完整证明用户意图，无法可靠判断时不恢复。

RestMediaCoordinator 等待本周期暂停完成再正常恢复；重复开始/完成共用同一任务，取消或退出不恢复。超时迟到任务未结束导致无法清理时，协调器禁用后续媒体操作至应用重启，防止跨周期命令重叠；护眼功能继续。RestService.Cancel 只负责取消事件，正常完成原路径保持不变。

验证：46项模块测试全部通过（新增默认关闭、恢复一次、用户状态/内容变化、恢复拒绝、监视清理、重复周期、取消、结束等待暂停、20个顺序周期）；真实GSMTC只读枚举通过。Release完整解决方案通过，保留既有Json.NET高危和未使用字段警告。

隔离WPF业务集成测试启动真正App和服务集合，验证旧XML加载默认关闭、重复RestStart去重、DispatcherTimer完成休息、取消只发一次并停止计时、配置保存及旧值保留。证据：artifacts/media-control/20261116-165645-242；artifacts/baseline/20261116-165932-025-Release；artifacts/app-integration/20261116-170106-337。外部真实播放器自动恢复尚未实机验收，不以假会话测试替代。

命令：`./scripts/Test-MediaControl.ps1 -Live`；`./scripts/Build-Baseline.ps1 -Configuration Release`；`./scripts/Test-AppIntegration.ps1 -BuildDirectory <成功构建证据目录>`。所有证据、测试数据均留在artifacts，未改用户配置。

原项目UTF8 BOM/CRLF通过字节检查保留；曾发现混合换行，精确恢复当时仅含本任务修改的RestService.cs及OptionsModel.cs，再保留格式重做，最终没有整文件格式diff。

Windows11按用户指示延期，发布兼容性不据此宣称通过。Phase4继续在原设置窗口添加媒体区域；Phase5需综合实际播放器、睡眠/唤醒、多显示器及24小时稳定性验证。

综合实测补充：Edge实际自动恢复尚未通过。已修复轮询提前确认与真实通知的竞态，51项模块测试通过；但Edge重复Paused通知仍触发保守拒绝。具体阻断与待决策略见Phase5报告，不将单元测试等同于实机恢复成功。
