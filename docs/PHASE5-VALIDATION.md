# Phase 5 — 综合验证进度

## 已通过

- 完整Release构建：artifacts/baseline/20261116-170446-688-Release，exit=0。
- 完整Debug构建：artifacts/baseline/20261116-170723-092-Debug，exit=0。
- 模块及协调器49项回归：artifacts/media-control/20261116-171334-487，Passed=49、Failed=0，包含20个顺序假会话休息周期。
- 真实GSMTC只读枚举Edge/Chrome：成功；原Phase1/2实际暂停证据继续有效。
- 隔离WPF业务集成：旧配置默认值、重复开始、真实DispatcherTimer完成休息、取消停止计时、媒体控件绑定、开关依赖、保存/重载全部通过。Release证据artifacts/app-integration/20261116-171301-457；Debug证据artifacts/app-integration/20261116-171615-807，两者全部通过。
- 媒体设置离屏布局：artifacts/app-integration/20261116-171301-457/app/media-settings.png，实际检查中文标签、禁用控件、范围和说明无截断。
- 测试工作目录修复后，仓库根不再生成Data；前次测试产生两文件已精确移入项目回收目录，未永久删除。

## 未通过或待验证

- 真实浏览器自动恢复：等待用户准备Edge播放、Chrome暂停，测试入口MediaControl.Tests.exe --live-resume，仅允许预期的两会话混合状态。
- 自建静音MediaPlayer合成夹具未向系统公开可发现会话，返回3（SKIP），不计通过。证据artifacts/media-control/20261116-170856-796/synthetic.log。
- 24小时真实连续运行尚未执行；20个假会话周期不等于24小时实机稳定性。
- 睡眠/唤醒、真实多显示器与DPI、非浏览器播放器、播放器退出/重启实机兼容性尚未完成。
- Windows11按用户明确指示延期，不作为继续开发阻断，但仍不得宣称测试通过。
- 原有Newtonsoft.Json 12.0.2的NU1903高危告警仍在，正式发布前需独立处理及回归；没有通过屏蔽告警或擅自升级依赖掩盖。

## 继续条件

本轮遵守“没有问题不需停下来报告，继续原计划”，已从Phase3连续推进Phase4和本阶段。当前真实环境验证不能用构建或单元测试替代。Phase5保持未完成，Phase6版本号、安装包、安装/卸载/升级及正式Release尚未执行；不在综合测试未完成时声称1.0.0可发布。

本阶段扩展测试入口、合成媒体夹具及布局导出，未新增产品依赖。所有生成媒体、日志和产物留在artifacts，不上传数据。
