# 100-REV3 API 核验与剩余未知

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
