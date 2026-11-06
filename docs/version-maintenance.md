# 游戏版本更新维护

1. 记录游戏标签、Application.version、Steam Build及Assembly-CSharp哈希；核对实际程序集中的API，不直接采用开发中指南。
2. 复查主库存、物品形状、拥有权、找槽/移动、机器UI树、WaterHelper、电源与睡眠/付款时序。
3. 对照Harmony目标及参数/字段绑定完成只读PE审计；抽象方法、加载器或信任管理器不能作为补丁目标。
4. 同步GameVersions、target-build.json、构建门禁及版本文档，编译当前提交。
5. 按功能用例进行有范围的验收，将编译、模型与实际游戏结果分开记录。优先覆盖开局/礼包、冷启动加载、终端取放与多选、驱动器/抽屉容量、机器IO、液体、商人预约、保存读取；耗电选项另测。
6. 打包仅含显式运行白名单、玩家说明、许可证及来源；源码、构建脚本和资源源稿在仓库中提供。不得夹带游戏引用、测试宿主、存档或工作站配置。

## 主要入口

| 范围 | 入口 |
| --- | --- |
| 拓扑与目录 | GameSession、NetworkProjection、NetworkEvaluator |
| 终端/堆叠/框选 | VirtualInventory、ItemStackState、TerminalStacks、NativeDrag、TerminalSelection |
| 介质与抽屉 | DiskStorage、CellCapacity、DrawerStorage |
| 液体和接口 | LiquidStorage、NativeLiquidInteraction、InterfaceStorage、NetworkInterfaces |
| 机器IO | IoBus、Automation、HopperHelper.RunWindow补丁 |
| 电源 | PartPower、NightReservation及原生付款事件 |
| 获取与商人 | Acquisition、AcquisitionCatalog、EngineerStart、电话与来访钩子 |
| 素材 | generate-game-assets.py、device_pixel_art.py、bus_pixel_art.py |

维护以真实实例和原生API结果为准；失败只影响当前操作，不增加永久全网锁、后置归属断言或孤立物品销毁。未结算峰值不跨日，成功扣电记录不重放。
Alpha阶段不提供历史存档迁移，当前版本自己的保存读取仍须正确。扳手拆卸总线的交互故障仍需实机定位；已有操作级诊断日志。
