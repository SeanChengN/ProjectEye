# beta.5：置顶保护和精简包

用户于2026-11-17确认允许排除所有置顶窗口。beta.5只改ProjectEye，不更改OpenCodex Monitor程序或用户配置；目标仍为net45，媒体策略与既有依赖版本不变。

## 桌面控制

旧规则将WS_CAPTION位当作真实外观边框，漏掉Tauri窗口。前轮只读快照观察Monitor主窗口style=0x14CE0000、exStyle=0x40118、rect=[-406,243,14,463]，具有WS_EX_TOPMOST，但仍有WS_CAPTION，因此旧规则不排除。Monitor源码Moved事件会重新判断停靠，脱离后恢复任务栏入口；这是本次故障的合理调用链，不能将源码推断等同新版真人验收。

现在保留所有WS_EX_TOPMOST窗口，无论有无标题栏，包括置顶播放器；固定排除原生进程文件名opencodex-monitor.exe的全部窗口，以覆盖暂时未置顶的切换状态。保留工具窗、无激活窗、隐藏/已最小化窗、非独立任务栏从属窗、其他虚拟桌面、DWM cloaked、桌面/任务栏及本应用遮罩。

枚举和ShowWindowAsync之前复用同一资格检查；轮询发现目标已经进入受保护状态即停止等待。只发最小化，不Show/Restore或重写第三方样式，不模拟Win+D。进程身份用OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)及QueryFullProcessImageName，避免旧框架ProcessName实现的高成本系统枚举；读取失败保守跳过。资格重新核验后到原生命令之间仍存在Windows级瞬时竞争，不宣称绝对原子性。

总预算继续200ms。日志保持数量/结果码，不记录窗口标题、路径或进程名。测试原生后端只对精确自身夹具发送命令；真实Monitor只允许只读状态筛选检查，完整护眼周期后的伸缩/任务栏需用户实测。

## 包内容与升级

packaging/test-release.json集中版本、明确runtimeFiles、requiredFiles和retiredFiles；只拷贝许可的运行EXE/DLL/配置、维护EXE、产品标记、哈希清单及必需许可证。许可证目录只包含txt，不带.gitattributes。README和CHANGELOG在候选目录与安装包/ZIP并列，ZIP本体与安装payload不含这两项。没有新增/替换业务依赖，不为节省文件数盲目删除DLL。

覆盖beta.4前核验旧清单与每文件哈希，仍备份后升级并保留Data/Log和未知文件。README/CHANGELOG、licenses/.gitattributes与旧PS1只有同时属于旧可信清单且哈希一致才回收，写入事务Retired列表；失败回滚恢复对应旧文档。篡改过的旧产品文件会按原规则拒绝升级，未知用户文件不删除。beta.4原包保留，不重新打包旧版本。

## 验证

新增有标题栏置顶、无任务栏、枚举后切换置顶/隐藏夹具，确认普通/最大化窗口仍最小化，受保护窗口不改可见性/任务栏设置。回归200ms预算、取消去重、20轮休息、常显滚动条、52项媒体、维护回滚及完整隔离安装/卸载。安装测试增加beta.4升级及旧文档退役、必需LICENSE存在。候选ZIP、payload及安装释放清单一致，禁止非必要文档回流。

Windows11、长期稳定性、HiBit隔离深度清理及前轮界面目视待验保持不变。最终候选证据及真人Monitor验收状态见[BETA5-VALIDATION.md](BETA5-VALIDATION.md)。
