# API 核验与剩余未知

2026-11-02：0.11网络工程师开局在独立14号新槽完成START五项实测，真实满电100kJ电池可供赠送核心，原生回调不重发；见 [开局运行记录](engineer-start-result.md)。测试跳过序章，未执行保存后重载或菜单鼠标输入。

0.11开局静态核验：ModHelper.AddStartingScenario/RemoveStartingScenarios为公开注册接口；NewGameData.HandleInitialItem的MODDED onStart在OnNewGameStarted之前，需先记权益后发放。普通APPRENTICE分支扣50并生成存储区/生活物资，skipIntro时加两标牌。原生职业以NetworkUpgrade.state=2直接预解锁，不能调用Unlock重放奖励。PowerHelper.SetPowerSourceAt会更新有效状态与电池图，最大能量读POWER_SOURCE_ITEM_MAX_ENERGY。详见 [0.11说明](engineer-start.md)，真实运行另记。

2026-11-02：0.10.1在独立新存档完成10/10运行时用例，包含抽屉/终端双窗口可见、17件代表显示、真实总线及终端标签/顾客规则；见 [0.10系列运行记录](runtime-pilot-results-0.10.md)。鼠标、旧存档迁移、保存重载及NPC实交易仍未验收。

2026-11-01 的独立新存档已完成八项真实运行时用例及操作提示复跑，见 [精简运行时试验结果](runtime-pilot-results-20261101.md)。下文各版本的“未运行”是当时的静态核验边界；本轮仅补充报告中列出的八项，不覆盖鼠标、跨日和读取存档。

## 0.8.0 升级、来访及礼包（2026-11-01）

同一 Rev6 真实程序集核验：ModHelper.AddNetworkUpgrade 接受本地化提供器、source、费用、分类和一次性回调；NetworkUpgrade.Unlock 先设 state=2 再调用 UnlockActions，随后发事件。原版 WINEMAKER 是基础页 Referral、费用 200。RegisterModUpgrade 为旧存档补定义但保留已有运行状态。

ModHook.OnGenerateCustomerNormal 在原版供应/故事客户之后、普通随机客户之前，可通过 StoreClientManager.AddClient 添加来源标记的自定义 StoreClient。原版 currentClientInstance、clientStack 是 ES3NonSerializable，不能承诺原样恢复正在交易的 NPC。ModHelper 的 Set/GetModDataInt/String 使用 PlayerStore.ModData 的存档内字典，适合记录每日判定、货单、供货日和礼包进度。

原版商人用 ClientIntent.SELL、原生货台和 AddBasicDialog/CompleteClientCreation。TryAddDirectSellingItemToTable 返回放置是否成功；底层满货台可能清理未放置商品，须记录拒收，不据此补货重刷。SpriteDict.spriteDictionary 可登记自有像素头像，GetSprite 按键解析；头像尺寸和柜台比例仍须实机。

重要差异：原版酿酒奖励使用 GraphUtils.EmporiumTryAdd，其所有库存均满时 DestroyIfUnplaced。本模组只向正确主库存预检并排队，未进入原生写入前可清理全新候选，未知写入后不销毁/重发。基础升级物品和价格配置已编译核验，108 项模型/贴图测试；未新增补丁，仍为 38 项 PE 审计。

## 0.7.0 原版料斗与机器槽（2026-11-01）

仍为同一 Rev6 真实程序集。HopperHelper.RunUnguarded 在真实 parentInventory 之间，先 GraphUtils.CanAccept 再 TryAcceptAll；后者内部最多 40 次 SlotMarker.TryAcceptOnce，返回的是剩余量。不能把返回值误读为移动量。SlotMarker 原生负责拆分、堆叠及进出库存回调。

MachineryPanel.role 为公开字段；熔炉/打印机实际使用 input / output，power/module/chip/note 另列；搅拌机的液体容器是 container，不能当通用 output。NO_HOPPER_INVENTORY_TAG 用于排除不可放料斗的库存，本模组同样排除。料斗物品自身可借 MayBypassGridRules 放入槽，但本模组不伪造该标签来绕过真实原料规则。

HopperHelper.WindowOf 把 1–5/6–10/11–15 映射到三个夜间阶段。新补丁位于具体静态 RunWindow(int) 的 Postfix，共 38 项元数据审计；本模组批次在每段原生料斗之后，不声称按 order 与原生料斗逐项合并排序。夜间供电复用当天已核验结算预算，不调用第二次扣电。

102 项模型/贴图测试覆盖 AE2 方向、频道成本、已付夜间预算、拆分/堆叠来源后置条件和四面 alpha 命中几何。机器收料、加工副作用、夜间顺序和存档必须按 IO 用例验收，静态元数据不证明 Harmony 执行成功。

## 0.6.0 液体增量（2026-11-01）

仍绑定同一 Rev6 / Build 26337366 DLL。真实反编译与编译核验：现代 WaterHelper 以 LIQUID_CONTAINER_CAPACITY 与 Liquid.Liquids 的 CURRENT_PART_* 整数存储，单位为 μl。TransferLiquid(source,target) 按接收空间和源体积取较小值，SplitProportionally 分配后经 HandleFilter、HandleReceiveLiquidTemp 和水质回调；不是可重复的纯函数，也不保证跨容器原子性。

原版 ItemSelectHandler 选中容器后，以 GameItem.MayTarget/CanTarget/Target 执行真实回调；空滴管回调反向调用 TransferLiquid(target,source)。WineHelper 某些状态禁用化学液体标签，不能只凭容器名称推测成分。旧 LiquidContainerHelper 共享 tag 但使用不同 current/max 字段和相反参数顺序，未作为现代化学液体接入。

新增六项具体补丁，共 37 项元数据审计；97 项模型/贴图测试覆盖成分精度、比例、零量、顺序、溢出和独立来源目录。倒液、文字区域命中、过滤、温度、原生菜单/酒瓶限制与保存往返仍待实机，见 LIQ 用例。

## 0.5.0 本地 Rev6 增量（2026-11-01）

当前目标 100-Rev6 / Steam Build 26337366；本机合法安装 Managed 中 Assembly-CSharp.dll SHA-256 为 `6398d44aa1675e1f02b63bc668868f970e0f5d3ae5a50f2aced8d06ca084e3b5`。以下由真实反编译实现及编译绑定核验，仍不是游戏执行证据。下文 REV3 保留为历史记录。

- GameGridInventory 的真实 items 列表、UncheckedAccept/Expel 回调和保存子图可供 DiskAreaInventory 复用；仅覆盖准入/形状预检，把几何排布换成面积预算。SlotMarker.TryAcceptOnce 会先 Expel，再 UncheckedAccept，因此预检必须在调用前完成，未知失败禁止重试。加载时保留超容量实例，不丢弃。
- GameSlotInventory 支持 `(defaultGridWidth, defaultGridHeight)`，驱动器六槽使用 `(1,2)`；原生 DNA 背景键为 Items/items_hydroponic_plant / dna_disk_black_full。仅引用原生资源，不复制进模组包。
- 原版电网物品 identifier 为 `current_converter`，CurrentConverterHelper.GetHostMachine 识别父物品 machinery_drawn_power 标签，费率/开关/停电走原生方法。
- PlayerStore.EndNight：OnGoingSleepEarly → UpdatePowerState → isNightCycle=true → 原生服务现金扣款及 RunStats.OnServicePaid → isNightCycle=false → OnGoingSleepLate。不能在 Early 直接调用 Supply，否则可能按白天再次扣现金。
- UpdateServiceCost 原生跳过 ITEM_BEHIND_LOCK_TAG、关闭转换器；本模组对自有核心已累计峰值保留账单，其他机器按原生规则合并。RunStats.OnServicePaid 位于真实现金扣款之后，作为结算凭据；Service.IsActive 只说明启用，不能当成已付款。
- 新增两项公开具体静态 Harmony 目标：CurrentConverterHelper.UpdateServiceCost(StoreService)、RunStats.OnServicePaid(string,int)。当前总计 31 项补丁由 PE 门禁核验目标、参数/字段绑定；不执行 Harmony。

待本地联合验收：六槽布局/真实拖放，硬盘内容原生保存往返，特殊物品在非几何库存的副作用，电费总账单/付款/未付款/停电时序。详细合同和操作见 [功能规格](feature-spec.md)、[用例表](functional-cases.md)。

## 历史 REV3 基线

目标：游戏内 `100-REV3` / Steam Build `26284217` / Windows x64，由用户提供；尚未独立确认 label/build 对应关系或“最新正式版”状态。用户明确授权上传必要真实编译引用供云端核验与开发；这些文件存放在 checkout 外，不提交、不重新分发。没有下载游戏本体、登录 Steam、操作本地电脑或安装 Unity/Wine。

## 已接收引用

| 文件 | SHA-256 |
| --- | --- |
| Assembly-CSharp.dll | `0fd3de05d961c8a415758f8f78a8e0a32692b8b45fb34350df885ed8bd5e8eb0` |
| UnityEngine.dll | `6abba25864b843149b594c7831dbf5eb7afe87c55d5112a4739ca4da09b91280` |
| UnityEngine.CoreModule.dll | `e468e19789d608b41e2c7fe4f367c7082fece7d1883f568e75870f6c5384cc86` |
| 0Harmony.dll | `40f48b42973b1f7084bfb215f12aec0bb1decbdaa8fa28ae0c9e3c18de8da17f` |

使用 ILSpyCmd 10.0.1.8346 在外部目录检查真实类型/方法和调用顺序，没有执行上传程序集的游戏代码。没有把反编译源码放入仓库或安装包。开发构建固定校验游戏主程序集哈希，所有游戏引用 `Private=false`。

## 静态确认的契约

| 部分 | 从真实程序集确认的 API / 行为 | 运行证据 |
| --- | --- | --- |
| 加载 | `IMod.Init(ModManifest)`、`OnEnable()`、`OnDisable()`；manifest 包含 Assemblies/Capabilities；游戏加载器确有相应检查 | 未运行 |
| 部件注册 | `ModHook.OnModItemDirectoryInit`、`ModItemDirectory.Add`、`ItemDirectory.CreateEmptyItem`、`GameItem.SetSpriteAndShapeFromMod` | 未运行 |
| 测试入口 | `ModHelper.AddDebugFunction` / `RemoveDebugFunctions`、`SpawnItem`、`SetItemOwnerShip`、`GraphUtils.TryAcceptAllOrDestroy` | 未运行 |
| F8/F9 前置条件 | 用户提供的真实 `DevTools.IsEnabled` 检查 `Application.dataPath/../dev.txt`；无标记且不在 Editor 时，`DevConsoleStripper` 销毁 QuantumConsole。`1dev.txt` 属于完整工具开关，本测试无需使用；模组详情的开发者模式是另一项信任设置 | 用户后续截图确认控制台和开发工具能显示；控制台有缺字刷屏，见本地反馈记录 |
| 主库存 | 修订后统一为 `EmporiumEntry.Instance.invElement`：真实代码将其挂到 `invWindow` 并注册 MainInvArea 回调；`PlayerStore.GetStoreInventory()` 返回构造于另一处的独立库存，不能作为主库存。`GameGridInventory.childItems` 是直接 items 列表的副本，不递归嵌套内容 | 原包绑定错误已由静态调用关系定位，吻合用户“手动仅四件、窗口七条总线”的反馈；修订后运行待验证 |
| 占格、方向与旋转 | 真实 GridShape.GetLocal/ForwardTransform 读取占格及方向；空间索引依据这些观测，避免反复调用 IsAdjacent；总线本地东面对应容器、反面对应网络 | 静态核验及模型测试；本次实际方向仍待本地验证 |
| 真实容器 | `GameItem.child`、`GraphNodeStorage.children`、`GameInventory.GetParentItem()`、`childItems`；仅枚举直接库存，遇到库存停止向下遍历 | 未运行 |
| 物品身份 | 真实对象引用与 unitCount；模组使用弱引用会话 Guid 区分实例，不把 uniqueId 当作已证明的持久身份，不序列化完整游戏状态或自行合并 | 未运行 |
| 终端 UI | `CustomUIManager.Window`、`CustomUIBuilder` 标签/按钮/开关/滚动/显示、窗口关闭；部件可挂原生激活回调 | 用户截图确认原包窗口能打开；显示多核心冲突、未接入及旧错误，不证明接入/搬运成功 |
| 变更时机 | 原生库存/物品有逐实例回调，但未确认覆盖所有变更的全局事件；当前窗口订阅真实库存 End/数量事件批量重读，所有写入强制重读；不替换原生回调 | 未运行 |
| 原生搬运 | `MayRemove`、`MaxNumRemove`、`TryFindOneValidInventorySlot`、`SlotMarker.TryAcceptOnce`；原生可能先 Expel 后 UncheckedAccept，中间触发回调 | 未运行 |
| 存档 | 游戏有 SaveItemNode 等原生数据结构；本模组不增加自有存储文件或持久容器身份 | 保存/载入/停用均未运行 |

这些符号已经过真实引用编译，不能由此推导 Unity 实际加载、菜单可见、回调顺序、原生搬运安全或存档正确。尤其原生 TryAcceptOnce 不提供跨容器原子事务；失败不能盲目重试或补造物品。

2026-10-31 用户提供了原包的真实终端窗口及 F8 警告截图，部分加载/UI 已有用户侧证据；问题、范围和源码诊断修订详见 [首次本地反馈](game-runtime-feedback.md)。这不代表修订后的二进制已在游戏验证。

## 0.2 新增静态核验

用户已确认 37f61bf 基础交互可用。0.2 的新 UI、供电、升级、拾荒和存档往返尚未实测，不能沿用前版运行结果。

- PowerHelper 的 power_source_item、能量/最大能量字段、CanDrawPowerSource、DrawPowerSource；普通电池足额才扣，HypertechBatteryHelper 沿用原生输出限制/无限能量语义，CurrentConverter 排除。
- GameSlotInventory、GameGridInventory(5,1)、PixelWindow.Attach、GameItem.SetContentWindow：真实子库存挂在原生存档图中。TagState 与 GameItem.state 字典保存配置和结算，不创建假接口。
- GameInventory.LockInv/UnlockInv、MaxNumRemove 回调；SlotMarker.TryAcceptOnce 不调用 MayRemove，因此必须额外约束 maxSlotRemoveItemFunc；GraphUtils.SwapItemsInPlace 的原生交换亦需限定 AE 子库存锁。
- PlayerStore.EndNight 的 OnGoingSleepEarly 确有调用，先于原版夜间服务；StoreStation.StartDay 增加 dayCounter，使用后置钩子解锁。不把 OnWakingUp 事件声明当作已证明的调用时机。
- SaveManager.EncodeNode 保存原生 state.Clone、uniqueId、子物品和库存节点编号；Decode 按目录工厂重建图、恢复状态和实例后 UncheckedAccept 子物品。加载 Early/Late 范围内禁止执行新电池预留回调，Late 恢复锁；节点顺序保持稳定。
- GameItem.GetGameItemType/modifiedState.dict 提供实例类型和启用标签；排除 deepspaceae.* 内部字段。GetTagReadonly 会创建缺失标签，因此配置只读使用 TryGetValue。
- ToolDirectory 的工具选择/目标委托用于扳手；拆卸通过真实 SlotMarker 移回原卡片。
- LootRegistry.GroupIds/HasTable 与 ModHelper.RegisterLootTable/AddLootGroupEntry/RemoveLootGroupEntry，按运行时 POI_* 组添加自有权重，不替换原生战利品表。
- CustomUIBuilder 的输入、图标、网格、下拉、滚动和清理容器用于正式 UI。额外 Unity 模块未上传，不使用需要这些模块类型出现在签名中的便捷方法，更不伪造枚举或游戏接口。

详见 [架构实现与成本](storage-features.md) 和 [逐项本地验收](game-test.md)。云端未启动 Unity；回调覆盖、存档往返、中断结果、各种原生换电/工具入口及跨模组行为仍待验证。所有新功能真实游戏结果：**未运行**。

指南参考仍为 dragonpan2/probably-stolen-modding 提交 accceead2d7b63bc1746bcb053e6ab41bea75d2a。APIChanges 开发中内容不作版本证据；本版使用的是用户真实 100-REV3 程序集。

## 0.3 UI 与拖放静态核验

已核对 MachineDnaManipulator 原生机器布局、MachineryPanel/Intake/Stack/ProgressBar、默认 1×1 GameSlotInventory、StealPixelElement（只借用渲染，不改真实父库存）、ItemMouseDragHandler 的按下/开始/结束/预览清理、SlotMarker 两种接收入口和 GameItemElement.Destroy 覆盖。公开物品类别的本地化实际是 TypeHelper.GetTypeDisplayName → Mechanic/type_*，并非直接展示状态标签键或假定 Data 表。实现及边界见 [原生 UI 迁移](native-ui.md)。

这些签名已使用用户现有 REV3 引用构建，不代表 REV5 核验。新 UI 实际比例、鼠标事件顺序和旧存档往返仍未在云端运行。

## 0.3.1 加载排查

发现真实 GameItem.Destroy 是 abstract，旧 6d03e55 的 Harmony 声明不可修补，发生在 ModLoader 调用 Init/OnEnable 之前。已删除该目标，仅保留 GameItemElement.Destroy 的受管覆盖。新增只读元数据审计：旧 DLL 14 通过/1 预期失败，新 DLL 14 通过/0 失败；不等同 Harmony 真正运行。

2026-10-31（Asia/Shanghai）实际读取用户上传的 Player.log：加载器报告 DeepSpaceAE v0.3.0-alpha，Harmony.PatchAll 对抽象 GameItem.Destroy 抛出 BadImageFormatException: Method has no body，随后 Init failed、running=False。故旧版加载阻断已有实机失败证据；修复版 0.3.1 的实机结果仍未知。该日志的入口名为 ModLoader.LoadModDll，不将它与现有 REV3 的 LoadModAssembly 混同，也不据此声称 REV5 全部 API 兼容。原日志不入库，关键行及范围见加载诊断。

语言键与字体另行区分：原生机器 UI 选择当前 locale 的 Noto_Sans，OfficeCodePro/[Text] 缺字需要原始日志定位。自有显示参数中的中文为合法纯文本，并无中文作为 GetLocalized 键的证据；现改为官方 Localization/en.csv、zh.csv 和 UiText ASCII 键。完整证据、官方链接和免 F8 日志步骤见 [加载诊断](load-troubleshooting.md)。

## 0.3.2 槽位、输入与动画核验

新增静态核对：MachineryContainerUtils.Refresh/TryResize 只自动调用 MachineryContainer/MachineryWidget，StealPixelElement 本身不在该范围且 Attach 保留位置。修订借用包装以主动传播真实槽尺寸并归还位置，保持原始库存保存父图。TreeNodeRender.FindParentElement<T> 沿渲染祖先查找；终端仅命中自有投影片，再将原生按下状态指向实际来源实例。

CustomUIMachineryStyle.SCALE=2，Reskin 会替换所有 Image，输入/下拉依赖原版 prefab；本版使用正确缩放并保留其子装饰。SpriteAnimationHelper 的显式帧列表通过 GameItemElement.ResolveSpriteByName 读取本模组精灵，原生管理器 Tick 调用 ApplyAnimationFrame，不改占格。现复用此接口播放 12 帧灯光，离线移除自有动画。这些依据均来自现有真实 REV3 引用，未据 APIChanges 假定 REV5 签名相同。实现、成本及用户侧复测项见 [UI 修订](native-ui.md#032-本地反馈修订)。

## Windows / 100-Rev6 本地迁移（2026-10-31）

用户授权适配 G 盘合法安装并部署。Steam `appmanifest_4348910.acf` 的 buildid 为 `26337366`；当前 Assembly-CSharp.dll 的用户字符串包含 `VERSION 100-Rev6`。主程序集 SHA-256 为 `6398d44aa1675e1f02b63bc668868f970e0f5d3ae5a50f2aced8d06ca084e3b5`。

Windows 脚本直接引用安装目录，不上传或入库游戏 DLL。此前 REV3 的行为分析是历史依据；本次须以当前 DLL 的编译与补丁审计另行记录，均不证明真实游戏交互、搬运或存档安全。

本次 Windows 实际验证：SDK 10.0.403；`scripts/verify.ps1` 的 77 项模型测试全部通过；`scripts/build-game.ps1` 使用上述 Rev6 引用编译成功，0 警告 / 0 错误，14 项补丁元数据审计全部 PASS。现有 API 调用未需业务逻辑修改。审计只读 PE，不执行游戏或 Harmony；本次未运行游戏，也未验证真实交互或存档往返。

## 0.9 共用电池核验（2026-11-01，100-Rev6）

在同一已门禁的本地 Assembly-CSharp 中只读核对：PowerHelper.CanDrawPowerSource 的普通电池分支只检查 power_source_item_energy >= amount，不限制瞬时 bandwidth，整数份额上单调；DrawPowerSource 对普通电池扣相应 energy 并更新精灵，对 Hypertech 使用 CanSupply，对转换器调用 Supply。GetAvailableEnergyFromItem(source,false) 用于普通电池扣前/扣后核对，不能以编译代替实际扣电试验。

CurrentConverterHelper.GetHostMachine 通过 converter.parentInventory.GetParentItem 读取带 machinery_drawn_power 的宿主，GetHostUsage 读取其整数值；UpdateServiceCost 遍历原版物品并汇总宿主用量。Supply 在非夜间阶段会直接扣现金，因此 AE 继续只在原版服务账单阶段结算，不对转换器调用 DrawPowerSource。多槽宿主存在未选转换器时必须按已选来源重建 AE 部分账单，避免三个槽重复计算宿主峰值。

接收器复用已有 GameSlotInventory、MachineryIntake 与借用槽包装，不新增 Harmony 目标。0.9 的 114 项模型/贴图测试、真实引用构建和 38 项补丁元数据审计通过；未执行游戏、原生扣费或保存往返。

## 0.9.1 写入和身份审查（2026-11-01，100-Rev6）

同一本地引用的只读方法体显示：GameItem.GetUniqueID 在 uniqueId == 0 时向 PlayerStore 分配身份；直接读字段不保证已分配。SlotMarker.TryAcceptOnce 只裁剪数量/MaxNumRemove，堆叠时直接 StackItemUnchecked，否则移除/拆分来源后 UncheckedAccept，不重新验证目标准入。因此增加 NativeTransfer，在写入前调用实际目标 TryInventorySlot 或 GetStackSlot，并检查实时来源、拥有权、锁及循环包含；自动化写入后核对目标入库和增量。

GameInventory.CheckAcceptParentShapeStateModifier 处理新物品加入父链，CheckChangeParentShapeStateModifier 用于在库变化；硬盘新接收改用前者。GameSlotInventory 的现有物品自校验与新物品准入分开，避免为防堆叠而拒绝同一个已在槽实例。未新增 Harmony 目标，仍是 38 项，只读核验不等于这些原生回调已经实机运行。

## 0.9.2 首次真实启动发现的注册时序错误

2026-11-01，本机实际 Player.log 记录 0.9.1 在 Acquisition.Enable → ModHelper.AddNetworkUpgrade → NetworkUpgrade.RegisterModUpgrade → PlayerStore.Instance → PlayerStore 构造处抛 NullReferenceException，ModLoader running=False。原因是官方 RegisterModUpgrade 读取惰性 PlayerStore 单例，而 ModLoader.OnEnable 时 NewGameData 尚未存在。修订只在已有 PlayerStore/新游戏开始/读档初始化时注册，读档晚期做幂等兜底；禁用清理也先检查 HasInstance，避免清理触发构造。模型与补丁审计未覆盖这一真实冷启动时序。本轮实测结果另见独立运行时试验记录，不能将修订编译成功填作实机通过。

## 0.9.3 新开局生命周期

独立运行时插件真实进入 InventoryScene 和新建的 6 号测试存档，确认场景 deepspaceae.runtime-pilot、原生 PlayerStore/主栏及开局回调均存在，但 GameSession.Loading 仍为 true，八项用例未能开始。OnGameLoadedLate 是读档事件，不能替代 OnNewGameStarted。补上新开局 session.Reset → AttachInventory/DayStarted，防止首次新档一直拒绝网络供电，并在同进程新开局时清理前档会话状态。结果以随后完整运行时报告为准。
## 0.10 抽屉与筛选核验（100-Rev6）

真实程序集确认TagState的启用位及int/long/float/double/bool/string字段；GameItem.MaxNumAdd会查询目标库存maxSlotAddItemFunc，原生GetStackSlot也会走已有目标的MaxNumAdd。抽屉以该回调阻止原生堆叠绕过件数预算，原有实例的CheckInventorySlotStillValid不复用新插入规则，避免状态变化或加载超容时误拒已有实例。没有新增Harmony目标，仍38项元数据审计。

当前顾客来自PlayerStore.currentClientInstance.GetClientBlueprint，原版IsClientBuyingThisItem会先检查IsClientRefusingItem；使用真实购买意图与IsClientBusinessDone过滤非收购/已完成情况。未根据客户需求文案自行推测标签。122项模型/素材测试及适配器编译通过，运行结果按后续独立测试记录解释，未证明鼠标输入或保存重载。
