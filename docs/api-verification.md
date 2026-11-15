# 游戏API绑定

目标：Windows x64，100-Rev13，Application.version `0.46D`，Steam Build `26525552`。
Assembly-CSharp.dll SHA-256：`725cb999d019dff1ff31bed353c7f9bb8d81a7605a15de1f80f5a3a6cf7b1d05`。
构建对该程序集实施版本门禁；更换游戏版本需重新核验，不能仅修改哈希。

| 功能 | 绑定与约束 |
| --- | --- |
| 主库存 | EmporiumEntry.Instance.invElement；PlayerStore.GetStoreInventory不是屏幕主栏 |
| 物品形状 | GridShape.GetLocal/ForwardTransform，包含旋转和翻转 |
| 搬运 | 原生找槽、MayRemove/MaxNumRemove、SlotMarker.TryAcceptOnce；使用返回数量 |
| 拥有权 | 终端和物品接口使用IsItemEffectivelyOwned，不能以交易准入代替 |
| 界面 | CustomUIManager、MachineryPanel/Row/Intake/Stack、原生控件与库存背景 |
| 机器槽 | 从contentWindow.childElement的真实UI树读取role和直属库存；排除NO_HOPPER及辅助槽 |
| 液体 | WaterHelper微升成分、TransferLiquid(source,target)及原生Target回调 |
| 电源 | PowerHelper.CanDrawPowerSource/DrawPowerSource；扣电bool为准 |
| 电网 | CurrentConverterHelper、服务费用与RunStats.OnServicePaid实际付款事件 |
| 自动化 | HopperHelper.RunWindow之后执行有界批次 |
| 动画 | SpriteAnimationHelper及原生精灵解析；不每帧扫描网络 |
| 开局/获取 | StartingScenario、ModData、NetworkUpgrade及当前世界注册生命周期 |
| 商人 | PaperBook/PhoneUI/通讯录及原生来访队列、供货和交易流程 |

Harmony目标必须是具体方法；不修补抽象GameItem.Destroy。打包前对当前DLL运行56项只读PE目标审计及游戏原生能力扫描。
0.17.1 起不声明弃用能力。界面直接调用原版 UI/TMPro 的公开 API；抽屉优化通过原版 ModReflect 查找并缓存游戏私有成员及绑定委托。引用全部 Private=false，不复制到发布目录。

游戏指南与APIChanges可辅助定位，但开发中API不自动视为已发布接口。
编译与PE审计不执行Unity、Harmony、物品副作用或保存读取；这些范围须按[功能用例](functional-cases.md)分别验收。

## 100-Rev13 核验（0.15.1，2026-11-11）

核验环境为 Ubuntu 26.04 x64、固定 .NET SDK 10.0.403，引用本机 Steam 安装中的 Windows 游戏程序集。Steam 清单为 Build 26525552；程序集中的 PlayerStore.VERSION_NUMBER / VERSION 为 `VERSION 100-Rev13`，globalgamemanagers 中的 Application.version 仍为 `0.46D`，所以模组清单的 GameVersions 继续使用 `0.46D`。

使用 ILSpy 10.1.1.8388 只读核对实际实现，确认以下调用合同仍与适配器一致：

- 单件购买仍先 OnItemBought、再扣款、再 MoveDealItemToInventory；OnItemBought 已设置拥有权。批发先扣整单金额，逐件记录购买，恢复货台后再逐件移动。自动收纳入口无需修改。
- SaveGame 在写入原生槽文件前编码库存及 ModData；AE 的前缀仍可先刷新网络修改记录。ModHelper.SetModData 仍写入当前世界中以模组 ID 为前缀的键。
- 主库存仍为 EmporiumEntry.invElement；GridShape.GetLocal / ForwardTransform 保留；SlotMarker.TryAcceptOnce 仍按 MaxNumRemove 限制、返回实际接收数量，允许拆分及堆叠。
- WaterHelper.TransferLiquid 的源/目标顺序、整数成分、过滤及原生倒液回调保留；PowerHelper 的可用性检查和扣电 bool 返回值保留；夜间实际扣款后仍触发 RunStats.OnServicePaid。
- HopperHelper.RunWindow(int) 仍运行原生批次；MachineryPanel.role/content、Row.Panels、Intake.content 和 Stack.elements 仍可用于寻找机器槽。
- 原生电话、来访队列、供货入口和开局/升级注册接口仍可绑定。终端使用的 items、backgroundImage、highlight、stackCountText 等反射字段，以及 TreeNodeRender.image/text、RenderHandler.SetHighlightColor 和 CustomUIElement.dropdown 均已核对。
- ItemMouseDragHandler.EndDrag 新增 cancelled 参数，并在未取消且没有库存/物品落点时寻找机器页签落点。AE 完成终端或幽灵过滤槽的处理后设置该参数，保留原生预览清理并阻止第二次搬运；原生取消操作不进入 AE 搬运。运行宿主同步显式传入参数，并覆盖原生取消、接口收纳和终端存入。
- 实机日志暴露拾荒表未接入：原实现寻找 POI_* 物品组，但 Rev13 的 ScavHelper / DebrisHelper 使用 dumpingGroundTG，ExpeditionPoiList 使用 dumpingGroundTG / dumpingGroundFreshTG；POI_dumpingGround 本身是地点表。改为绑定这两个已确认的原生物品组，保留 AE 权重 20 和不含能源接收器的15个候选。运行宿主检查新建与保存重载后均只注册一次，并用原生带过滤条件的组抽取验证候选解析；不据此声称完成拾荒 UI 或掉率验收。

完整游戏引用 Release 构建零警告、零错误，49 项 Harmony 补丁 PE 元数据审计全部通过；115 项模型测试全部通过。补齐了两个接口的离线 SVG 图标清单，未改变游戏 PNG 素材。独立运行测试宿主的旧六盘位、32件发放游标断言同步为现有十盘位、含联系纸条的33件发放，并复用工程师开局检查和现有发放标记。上述构建结果仅证明静态绑定与模型验证；实机结果以 artifacts/runtime-tests 中独立运行的报告及其 sourceCommit 为准。宿主调用原生拖拽处理器，不能替代硬件鼠标操作验收。

## 100-Rev12 核验（历史记录）

对本地合法安装的 Build 26496118 读取 PE 元数据及反编译核对；PlayerStore.VERSION 为 100-Rev12，PlayerSettings.bundleVersion 仍为 0.46D。SlotMarker、WaterHelper、PowerHelper、RNG 和 StoreClientListSupplier 与已有参考实现一致（忽略反编译工具提示）。PlayerStore 的原生摆货入口保持不变；HopperHelper 新增颜色、出口配置和溢出路由，但 RunWindow 入口保持；ModHelper 新增数据目录写入保护，本模组使用自身 ModData，不访问其他模组目录。

0.14适配时完整游戏引用构建通过（零警告、零错误），当时46项补丁元数据审计全部通过。此次未启动游戏或运行模型测试，静态核验不代表实机验收。

## 0.15 成交与修改记录绑定

新增PlayerStore.OnItemBought后缀记录真实购买实例；MoveDealItemToInventory前缀仅替换启用接口的购入商品落点。单件BuyItem先处理OnItemBought和扣款再调用移动；NegociationUIManager.OnWholesaleClick先扣整单金额，对每件调用OnItemBought，恢复货台后逐件调用移动。原版自动移至主库存选项关闭时，移动方法仍被调用，接口开关独立生效。

新增PlayerStore.SaveGame前缀在原版存档序列化前刷新网络修改记录；只写当前世界的自有ModData。已有ValidateSelect/ValidateShapeState及库存增减事件支持关闭终端后的修改跟踪。0.15完整引用构建零警告、零错误，49项补丁元数据审计全部通过，无新增素材或权限；未运行模型或游戏测试。

## 0.17.1 反射停用适配

针对当前 Rev13 已有的 ModReflect 接口实施，不宣称已经运行未来 Rev14 二进制。保持实际核验的引用哈希与 0.46D 版本声明；更换目标时重新验证 GameVersions 的点分格式。

原生扫描入口随 `build-game.sh` / `build-game.ps1` 执行，报告 `artifacts/validation/game-mod-scan.json`：加载本机实际 Assembly-CSharp 中的 ModScanner，仅执行 IL 扫描，不实例化游戏或模组。离线宿主没有运行 ModLoader.Awake，因此从本机 Managed 目录提供相同的程序集名称清单以完成引用检查；不修改扫描标志或能力判断。除硬性拒绝外，发现任何待停用能力声明、检测或 Pending 项也使构建失败。打包核对扫描报告与 DLL、manifest 和游戏程序集的哈希。

固定 `typeof(游戏类型)` 字段反射保留用于原版计数节点和完整物品状态比较。优化抽屉只缓存原生真实物品列表引用，不复制物品、不建立另一份所有权图；背景、格子及尺寸 setter 委托由 ModReflect 绑定一次；布局中的边框标志使用明确 typeof(GameGridInventory) 的缓存 FieldInfo，避免每次 ModReflect.Get 的调用栈检查。兼容模式延迟跳过这些私有布局绑定，直接调用原版完整 Validate；库存读取开关使用原版 childItems。两种模式均通过 SlotMarker 进行真实搬运。

官方规则：[APIChanges](https://github.com/dragonpan2/probably-stolen-modding/blob/main/APIChanges.zh-CN.md)、[反射指南](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md#反射)。原生扫描通过不能替代正常玩家加载、界面、吞吐及保存重载验收。
