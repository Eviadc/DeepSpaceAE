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

## 0.6 液体维护增量

LiquidStorage / LiquidComposition 读取原生化学成分，GameSession.Read 发现无子库存的总线前方液体容器，VirtualInventory 复用可见区域 UI 镜像并增加液体卡片。NativeLiquidInteraction 只桥接原生选择/Target、权限和刷新。

新增六项具体补丁：ItemSelectHandler.SelectItem、OnEventReset，GameItem.Target，WaterHelper.TransferLiquid，GameItemElement.GetTooltip、GetTooltipAdvanced（总计 37）。升级须复查原生 Target 回调及空滴管反向语义、TransferLiquid 的比例/过滤/温度/水质副作用和 skipValidate 更新时机。不能将旧 LiquidContainerHelper.TransferLiquid(target,source) 与现代 WaterHelper.TransferLiquid(source,target) 混用。

TreeNodeRender.RaycastElement 的首次非空 element 决定原生目标。液体卡片文字必须用以真实容器为命中身份的纯渲染树，不能直接嵌独立 MachineryText 拦截原生目标；Tooltip 在液体镜像下局部替换，源主栏仍用原版提示。UI 与倒液实机用例为 LIQ-*。

## 0.7 机器输入输出维护

IoBus.MachineSlots 按真实 MachineryPanel.role 遍历到直属机器的 GameInventory，分别只认 output/input；未知角色停止，不回退到整机 Contents。新增机器适配必须确认真实角色、NO_HOPPER、MayRemove/MaxNumRemove 和准入回调，再补 IO 用例；不要把电池/模块/便签槽当料槽。

Automation.RunAutomation 以原生 HopperHelper.RunWindow Postfix 驱动，手动入口复用同一路径。每批一次网络评估和每核心一份候选观测；根物品/部件结构或配置变化使批次停止，每项实际写入仍复查原生来源、锁、数量、政策和槽。每总线 8 次/全批 64 次，禁止每帧重建网络补货。

AutomationMove 调用 SlotMarker.TryAcceptOnce 并验证原实例在源中的剩余量或完整移除结果，允许原生拆分与堆叠；异常结果锁写，保留 slot.item 可能产生的孤立分裂实例。升级时复核来源对象销毁/数量清理语义，不能套用终端“整实例不变”的检查到料斗。

PartPower 的夜间分支只承认同日、同源、已结算且不超已付预算，避免先扣完电池后把已付网络当成没电，也不能绕过未付/未知扣费。新增 IO 类型只追加 NetworkPart 枚举，保存图不修改既有节点顺序。新增 20 PNG，总白名单 87。

## 0.8 获取路径维护

AcquisitionCatalog 集中记录 13 种组件的基础价格、史蒂夫有货概率/数量及基础礼包；新增组件必须同步工厂、该表和测试，避免商品目录漏项。费用 200、首日 15、每日 5% 是用户确认的初始平衡值，可后续修改，不是游戏原生常数。

Acquisition 使用 AddNetworkUpgrade 与 GenerateCustomerNormal 官方钩子，无额外 Harmony 目标。升级本地化必须用 provider 延迟解析；礼包先持久化 in-flight，确认实际主栏放置后再推进。必须重新核验 PlayerStore.ModData 的保存边界、NetworkUpgrade.Unlock 调用时序、SlotMarker 接受结果和原生满库存兜底，禁止恢复 EmporiumTryAdd 的丢弃路径来“完成”礼包。

客户蓝图/队列/当前客户不序列化。每日 roll/货单/served 记录用于重建未到访候选与阻止当天重复供货，不能声称可恢复未购货台。升级时核验 AddBasicDialog、ClientIntent.SELL、货台购买和 owned 标记，不给 NPC 手写现金结算。头像只登记独立键 deepspaceae.steve，保持原版 sprite 高度/轴心，禁用只移除自有内容。

头像由内置 imagegen 生成，原图、提示词和缩放说明保存在 assets/steve-source；最终 PNG 256×384、透明。它不属于 AE2 素材许可；包总计 88 PNG，需同时分发独立来源说明。

## 0.9 供电池维护

新增 NetworkPart 只能追加枚举值。energy_receiver 是 2×2、三真实槽、1 频道/0 kJ 叶子；PowerSelection 的最少数和同数保留偏好需同时保持。Core pool_active、pool_plan、pool_rejected 与各电源 supply_core/share/day/phase 共同定义保存协议，必须保留 GetUniqueID 的稳定身份，不用 UI 镜像身份或数组下标代替。

核验 PowerHelper.CanDrawPowerSource 对整数 kJ 的单调性、DrawPowerSource 的实际普通/无限电池行为、GetAvailableEnergyFromItem 的计量，以及 CurrentConverterHelper/RunStats 账单和付款时序。不要用电池剩余能量否定已付夜间预算，也不要让未选转换器沿用宿主旧用量。断线只影响供电，不解除已有费用责任；未知扣费与欠费锁定必须跨加载保持。新源注册应事件触发，禁止恢复逐帧扫描。

回归 ENERGY-01～14；适配时保留旧核心/线缆保存节点顺序，首次启用池仅迁移已存在的单槽预留，不重扣。接收器商品价值 350、有货概率 30%、数量 1，基础礼包仍 16 件；新增 PNG 为 energy-receiver 及其 off 版，32×32，白名单总量 90。

## 0.9.1 审查后的边界

NightReservation.CanSettle 是普通/共用/电网账单一致的入口约束；不得让 Settled 在核心未重新在线预留时跨日续费。源 Token 必须与计划的核心、份额、日期和阶段一致；未知源不可重新选入，已知欠费的源不一致时转 Fault 而非 Shortfall 解锁。

NativeTransfer 统一紧邻写入的原生槽复核，覆盖终端/扳手和 IO；不要因 TryAcceptOnce 名字含 Try 就省略这一层，它会直接移除来源再调用 UncheckedAccept。自动化还须检查目标根库存及增量，拆分后 SlotMarker.item 可能变为新实例。供电候选 PowerSnapshot 只活在本次读图/结算内，不能缓存跨事件；它不是实际扣电授权。稳定拓扑身份必须从 GetUniqueID 获得。

PowerSelection 用前缀和完成保留数量的可行性检查，排序后线性选择；不要恢复每个保留数重建数组/求和。供电恢复锁只访问当前核心和该核心原有预留宿主，批次索引一次遍历全部供电宿主；路由查 ID 使用字典而非每个候选 Single 枚举。更新时同时回归 REVIEW 和 ENERGY、IO 用例。
# 0.10 新增核验点

升级时核验TagState各数值类型/启用状态、原生物品特征字段，以及StoreClientInstance.GetClientBlueprint / StoreClient.IsClientBuyingThisItem / IsClientBusinessDone。抽屉真实数据与五卡子图、新终端单卡子图必须做保存/重载验收。保持原生交换门禁覆盖DrawerInventory及新卡槽；本地主栏抽屉用独立原生面板，终端保持镜像，两窗口必须可同时存在。代表物品缓存仅在库存事件更新，逐物品回调不得重复复制整份childItems。取出后须恢复真实渲染可见。本地免电不能让网络终端绕过供电。素材总数91及分立MIT/CC许可见 [0.10说明](drawers-and-filters.md)。
