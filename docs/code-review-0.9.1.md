# 0.9.1 全组件代码审查

日期：2026-11-01。起点为 main 的 fc140ad（0.9.0），目标游戏为本机 100-Rev6 / Build 26337366。范围是已有功能的代码准确性、可提前发现的缺陷、复杂度、模块边界及原生兼容方式；未添加自动合成等新阶段功能，未启动游戏、读写存档或进行真实游戏写入试验。

## 审查范围与结论依据

| 组件/边界 | 已检查的代码路径 | 结论及证据范围 |
| --- | --- | --- |
| 核心、线缆、频道 | InventoryLayout → PhysicalNetworkProjection → NetworkEvaluation；真实占格、方向、阻断面、单核心、叶子与频道路由 | 空间哈希及共享搜索保留；修复排序身份重载后变化。随机图基线、旋转/掩码、冲突及频道模型回归通过 |
| 存储总线、卡片、扳手 | PartStorage、StoragePolicy、ExtractionFilter、CablePorts、NativeViews 幽灵格及 ConfigureMove | 四面升级/五卡槽真实子图不改；权限并集、固定九列扩行和按部位拆卸保留。写入增加最终槽复核；无全游戏物品枚举 |
| 终端物品模式 | CatalogSearch、VirtualInventoryLayout、VirtualInventory、NativeItemMirror、NativeDrag、TerminalSelection | 保留连续滚动、可见镜像、原生单/群组操作和同一真实身份；镜像拒绝所有接收，不借走源物品渲染。目录仅用于展示，移动重新读网并原生预检 |
| 硬盘/驱动器 | DiskStorage、DiskAreaInventory、CellCapacity、驱动器目录及保存图 | 六槽/64、256 格独立计容、真实实例、防递归和超容量保留不变；修复新加入物品使用错误父形状校验的问题 |
| 液体 | LiquidComposition、LiquidStorage、NativeLiquidInteraction、液体卡片 | 精确微升成分与来源独立，原生 select/target 回调负责混合/过滤；没有直接改成分。修复卡片矩形及按每来源体积排序 |
| 输入/输出总线 | IoBus.MachineSlots → Automation → SlotMarker | 按 AE2 方向和原生 panel.role，NO_HOPPER/非料槽排除，有界 8/64 批次、付费夜间预算保留；增加目标回执与最终准入检查 |
| 控制器、电网、能源接收器 | NightReservation、PartPower、PowerSelection、PoolPower、EnergyReceiver | 修复跨日旧计划再次收费、未分配 ID、Shortfall 模式迁移和交换准入；未知扣费仍停用锁定。选择与宿主索引复杂度改善 |
| AE 工程师、史蒂夫 | AcquisitionCatalog、DeliveryProgress、Acquisition 及原生 ModHelper/StoreClient 方法体 | 费用 200、16 件礼包、满主栏排队/in-flight、每日一次 5% 候选、十三组件货单配置保持；未发现需改变经济或发放协议的代码缺陷。实际交易/队列/画像比例仍待游戏验证 |
| 生命周期、本地化、补丁 | ModEntry 注册/退订、加载/次日/夜间、弱引用、BorrowedInventoryView 归还、UiText 和 en/zh CSV | Core 无游戏依赖；原窗口子图不因镜像重排；补丁仍局限已有目标与 AE 状态。实际调用和字体/窗口比例不能由元数据证明 |
| 构建与分发 | 固定 SDK/锁定还原、游戏 DLL 哈希门禁、PE 审计、package-game 显式白名单、部署脚本 | 不更新依赖、不提交引用/缓存；90 PNG 的来源区分不变；命名包绑定干净源码提交和当前审计的自有 DLL |

## 已修复的具体问题

1. **旧计划重复结算/电网续费**：已 Settled 的核心移出主栏后没有当日 ReadBoard 预留，但 PoolSettle 仅排除“本日已结算”，后续夜间仍可能扣旧份额；单槽逻辑也会为离线旧核心重新预留最低费用。服务账单原条件同时允许旧 Settled 用量。统一用 NightReservation.CanSettle 限制进入结算，只有 Reserved 或已知欠费的 DebitStarted 合法；已有欠费按固定原账单登记付款回执，断电期间增添设备不能使原账单付了却无法确认。离线已付计划不自动续费，在线新日仍正常预留。
2. **原生身份未分配/不稳定**：GameItem.uniqueId 允许为 0，GetUniqueID 才保证原生分配；单槽预留和 IO 原顺序直接使用字段，而拓扑使用会话随机 GUID。供电/自动化改用 GetUniqueID，拓扑和路由采用稳定的原生 ID。窗口目录 GUID 仅保留为会话内键。
3. **原生 SlotMarker 不会自动重查槽**：真实 TryAcceptOnce 方法体会直接堆叠或从来源移除后 UncheckedAccept。旧代码在挑选位置后还有网络/供电回调，目标可能已变化。新增 NativeTransfer 统一重新调用真实 TryInventorySlot/GetStackSlot，检查当前来源、锁、拥有权及递归包含；已知拒绝在写入前结束，不假报未知写入。
4. **IO 只看来源不能证明收到货**：来源扣减正常但拆出的目标未入库、或堆叠目标未增加，都可能通过旧后置条件。现在同时核对实际目标的库存归属与增加量，异常维持现有 writeFault/孤立实例保护，不补偿、不重试。
5. **硬盘使用了在库变更校验来检查新加入**：TryInventorySlot 通过 CheckInventorySlotStillValid 间接调用 CheckChangeParentShapeStateModifier，和原版新接收的 CheckAcceptParentShapeStateModifier 不同。改用新接收合同，并核对原生可取数量；保留已有超容量数据。硬盘/电源槽对同一已在槽实例的检查与新物品准入分开。
6. **供电槽交换绕过规则**：旧交换补丁只检查锁和电源类型，未完整执行核心峰值/接收器准入。补上真实替换准入；错误物品、未拥有或不足电源不能靠 unchecked swap 绕过。直接取出再插入的正常流程保留。
7. **首次进入共用模式丢失 Shortfall 拒绝记录**：单槽已不足时接入空接收器，旧代码可能把同一块电池当新源。现在迁移旧失败源身份；Fault/未知源不参与重新分配，欠费源回执不一致转 Fault，不能转 Shortfall 自动解锁。
8. **液体排序/矩形**：同成分多罐曾沿用物品总量分组排序，导致两罐 70 ml 排到单罐 100 ml 前；现按每罐体积。液体卡片根矩形曾被实际罐形状覆盖，现使用声明的六列×三行尺寸，保留原生来源身份与文本命中。
9. **可避免的重复遍历**：PowerSelection 每试一种保留数量都重建数组并求和，最坏 O(P²)；改为前缀和，排序后线性选择。每核心重复扫描全部供电宿主改为每批一个 PowerSnapshot 并按核心索引旧源，锁恢复只处理相关宿主；候选与计划连接使用字典/集合。IO 目标路由按设备 ID 查找也改用字典。
10. **单槽无限电池的夜间输出限制**：旧单槽已付预算路径只复查转换器状态，漏查无限电池的原生输出上限。现在与共用路径一致，按已付份额复查特殊电源能力；普通电池已经扣去的能量不重复要求。

PowerSnapshot 只是本次调用的候选索引，不跨原生事件保留。实际扣费逐项重查电源仍在真实供电槽、所属计划、日期、阶段、所有权和可供量；物品网络写入仍有当前网络授权和独立的最终原生预检。不把缓存页面或快照变成写入授权。

## 验证与保留边界

- 120 项 Core/模型/素材回归通过。新增回归：离线已付计划不续费；欠费网络加设备仍能结清原账单；Shortfall/未知结算不得重放；4,096 源的最少组合；来源变化但目标未入库/未增量；液体按单容器体积排序。原 160 组选择穷举对照及网络随机基线继续通过。
- 内存原型 smoke 通过：浏览、取出、存入、断线、频道不足与重连。它只操作 Tests 内的模拟容器。
- 真实 Rev6 引用构建和 38 项只读 PE 补丁元数据审计通过。没有新增补丁目标；不代表 Harmony 在游戏中已执行。
- 同机五次中位数的 Core 性能探针见 performance.md。20,001 节点仍为共享搜索的 2 轮（频道开）/1 轮（关），没有退回逐设备全图 BFS；未测量 Unity 帧率或其他模组回调。

当前未发现必须改变既有物品归属、复制物品、重排旧保存图或放宽原生机器角色来解决的问题。已有真实 UI/存档/原生副作用无法在模型中证明，继续按 TERM、DISK、LIQ、IO、ACQ、ENERGY 及新增 REVIEW 用例由用户实机验收。未知原生写入/扣电仍要求停止；不承诺跨容器原子事务或所有第三方模组的任意场景兼容。
