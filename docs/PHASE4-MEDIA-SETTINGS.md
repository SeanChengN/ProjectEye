# Phase 4 — 媒体控制设置

原设置窗口新增“媒体控制 / Media control”选项卡，沿用原有Toggle、ComboBox和主题。新增中英文资源，不创建额外常驻窗口，不改休息遮罩布局。

设置：自动暂停默认关闭，自动恢复默认关闭，范围默认所有会话（可选GSMTC主要会话），本机诊断日志默认开启。关闭自动暂停会同时清除恢复设置并禁用恢复及范围控件；日志开关独立可用。界面明确保守恢复条件和设置下次休息生效。当前周期使用不可变设置快照，不读取后续界面改动。

旧XML没有Media节时采用默认值，新增配置保存并重新加载均成功，原WarnTime等值保留。

验证：Release完整解决方案构建成功，证据artifacts/baseline/20261116-170446-688-Release。真实WPF业务测试验证控件编译、暂停关闭时恢复不可用、暂停开启时恢复可用、关闭暂停清除恢复、配置保存/重载，最终artifacts/app-integration/20261116-170605-616/runtime.log全部通过。

首次绑定测试在Dispatcher完成DataBind前读取未加载窗口，错误地观察到默认IsEnabled=true；让Dispatcher完成DataBind并核对BindingExpression.Status=Active后通过，没有为测试修改产品绑定逻辑。桌面截图捕获仍未得到可靠结果，本轮不宣称人工视觉验收通过。

测试隔离修复：旧统计库使用相对工作目录，早期业务测试在仓库根生成Data/data.db和Data/data.db.version。已核对精确两文件及创建时间，将整个本任务Data目录移动到.recycle-bin/20261116-170337-777/Data，无永久删除，可恢复；测试脚本改为Push-Location隔离应用目录，finally恢复当前目录，后续根目录不再生成Data。

Phase5继续综合测试，包括真实GSMTC恢复、播放器变化、睡眠唤醒和长期运行。Windows11按用户指示延期；未验证项目保持待办，不提前进入正式发布。

后续离屏布局验证：artifacts/app-integration/20261116-171301-457/app/media-settings.png。WPF完成DataBind后对设置内容在白底渲染，已实际查看中文标签、默认关闭/禁用状态、范围选择及换行说明，均无截断；这是离屏布局检查，不替代实际桌面/DPI验收。

2026-11-16按用户确认的保守策略更新中英说明：重复状态通知等无法确认的会话仅暂停、不自动恢复。最新Release及20次真实休息周期验证通过，更新布局图位于artifacts/app-integration/20261116-180019-692/app/media-settings.png，已检查说明完整显示。
