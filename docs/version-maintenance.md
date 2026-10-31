# 游戏版本升级维护入口

以 [完整功能规格](feature-spec.md) 和 [用例矩阵](functional-cases.md) 为维护合同。当前代码目标 100-Rev6 / Steam Build 26337366；不要把旧 REV3、云端编译或开发中 APIChanges 当成新版本可用证据。

## 更新顺序

1. 查看 `git status`，使用当前 checkout，保留既有改动；记录游戏版本/Steam Build、SDK/global.json、上一可用源码提交和安装包。
2. 从用户合法本机安装只读核验必要 Managed 引用。游戏 DLL 保持仓库外，不能提交或随包发送。哈希变化应先使构建失败，再逐项确认接口/行为，最后有意更新门禁；不能直接跳过哈希。
3. 使用离线反编译/PE 元数据读取验证下表契约；缓存放 artifacts/native-api，不入库。优先查看真实实现及 Player.log，不能只看方法名仍存在。
4. 修改后运行 `scripts/verify.ps1`，再 `scripts/build-game.ps1`；后者包含当前 DLL 对应的补丁元数据审计。模型不依赖游戏对象，模拟库存仅在 Tests。
5. 对新增/变化的原生行为执行相关用例，再回归取放、夜间结算和存档往返。只有实际运行证据可标记“实机通过”；未知结果停止，不做自动回滚/补扣。
6. 用户已授权适当验证后提交、直接推送 Eviadc/DeepSpaceAE main；不强推。干净提交后重建，`scripts/package-game.ps1` 校验内嵌提交及素材白名单；退出游戏后 `scripts/deploy-game.ps1` 构建、打包、备份、部署。不要启动游戏或自行操作存档。

## 原生契约和回归映射

| 功能 | 主要自有实现 | 需核验的原生契约 | 用例 |
| --- | --- | --- | --- |
| 主库存/占格拓扑 | GameSession, InventoryLayout, GridConnection, PhysicalNetworkProjection | EmporiumEntry.invElement；modifiedShape 旋转/翻转与 ownership；不要使用独立 PlayerStore.GetStoreInventory | NET-* |
| 频道/成本 | NetworkEvaluation, NetworkTopology | 属于模组规则，不从游戏 API 猜频道；新增叶子不能桥接 | NET-03～06, PWR-01 |
| 多面线缆升级 | PartStorage, CablePorts, CableVisuals, NativeViews | 稳定子图；slot 回调先后顺序；双击指针坐标；原生旋转坐标变换 | CBL-*, BUS-* |
| 原生界面/槽包装 | BorrowedInventoryView, NativeViews | MachineryWidget 尺寸、偏移清理和关闭归还；GameSlotInventory 支持 1×2；GridPixelElement 子图顺序 | DISK-01, SAVE-01～02 |
| 终端镜像 | NativeItemMirror, VirtualInventory | TreeNodeRender 绑定真实 GameItem；原版 sprite/层/动画更新；不得移动真实 handler/middle | TERM-01～03,07 |
| 框选/群拖 | TerminalSelection, NativeDrag, NativeDragPatches | ItemMultiSelectHandler 私有选择/状态/预览/清理；原生按键拆分分支 | TERM-04～06 |
| 实际读写 | GameSession.NativeMove/Placement | MayRemove/MaxNumRemove；TryFindOneValidInventorySlot；SlotMarker.TryAcceptOnce 的 Expel/Accept 顺序；失败副作用 | TERM-10～12 |
| 面积盘库存 | DiskStorage, CellCapacity | GameGridInventory 虚方法、UncheckedAccept/Expel/ValidateShapeState；保存真实 items 子图；Shape/state modifiers 与物品准入委托 | DISK-*, SAVE-01 |
| 普通电池 | NightReservation, PartPower | PowerHelper.CanDraw/DrawPowerSource；超科技输出限制；睡眠/日计数顺序；持久化 state | PWR-*, SAVE-03 |
| 原版电网 | PartPower 中两项 Harmony 补丁 | CurrentConverterHelper.GetHostMachine/machinery_drawn_power、UpdateServiceCost、费率/停电；RunStats.OnServicePaid 必须在实际现金扣除之后 | GRID-* |
| 夜间生命周期 | ModEntry, GameSession.Night/NightLate | OnGoingSleepEarly 在原生 service 扣款前；OnGoingSleepLate 在其后；isNightCycle 开启时机；StartDay 日计数 | PWR-04, GRID-03～05 |
| 注册/本地化/掉落 | ModEntry, UiText | 官方 ModItemDirectory、资源就绪钩子、en/zh CSV 参数格式、拾荒组注入/移除 | LOAD-01, LIFE-01 |
| 保存安全 | PartStorage, DiskStorage, GameSession.Reset | 原有核心/线缆/总线子图索引不变；新物品独立图；DebitStarted 不重放 | SAVE-* |

## 已知限制和诊断证据

- 没有真实游戏宿主自动化。静态审计只验证补丁目标与字段/参数，不执行 Harmony/Unity；编译与模型不能证明保存安全。
- 批量移动逐项处理，无跨容器事务；未知结果锁写。收集出现问题的操作、原生物品 ID、当日预算/阶段、相关 Player.log，勿删除物品掩盖问题。
- 新版本若改变保存图，先明确旧存档迁移策略再实现；不擅自丢盘、卡或真实内容。用户只豁免旧“独立总线”的兼容。
- 贴图生成使用固定 AE2 来源和 Pillow 12.3.0；常规构建不需 Pillow。新增贴图必须同步生成脚本、package-game.py 白名单/尺寸、测试、许可说明与资产数量。
- 维护 API 记录时注明版本、程序集哈希、发现方式、调用前后副作用及待实机项。文档之间冲突时，以当前版本规格和已核验代码为准，明确标注历史段落。
