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
| 变更时机 | 原生库存/物品有逐实例回调，但未确认覆盖所有变更的全局事件；当前只在用户刷新/操作时重读，读取期间不订阅/替换原生库存回调 | 未运行 |
| 原生搬运 | `MayRemove`、`MaxNumRemove`、`TryFindOneValidInventorySlot`、`SlotMarker.TryAcceptOnce`；原生可能先 Expel 后 UncheckedAccept，中间触发回调 | 未运行 |
| 存档 | 游戏有 SaveItemNode 等原生数据结构；本模组不增加自有存储文件或持久容器身份 | 保存/载入/停用均未运行 |

这些符号已经过真实引用编译，不能由此推导 Unity 实际加载、菜单可见、回调顺序、原生搬运安全或存档正确。尤其原生 TryAcceptOnce 不提供跨容器原子事务；失败不能盲目重试或补造物品。

2026-10-31 用户提供了原包的真实终端窗口及 F8 警告截图，部分加载/UI 已有用户侧证据；问题、范围和源码诊断修订详见 [首次本地反馈](game-runtime-feedback.md)。这不代表修订后的二进制已在游戏验证。

## 当前实现边界

- 核心为自有 3×3 定义，其他部件 1×1；20 张 PNG 由原创整数几何脚本生成。不对其他物品占格做硬编码。
- 控制台自有日志与清单显示名称为英文/ASCII；不修补或屏蔽游戏全局字体警告。
- 从真实 DoubleClickAction/ClickAction 与默认激活处理器核验双击入口，已用 typed Harmony 补丁限定到 AE 物品。ApplyAnimationFrame、Sprites 缓存和库存 End 回调支持连接贴图，详细证据和复杂度见 [性能记录](performance.md)。
- 核心/管线为传输网络；终端/总线是叶子。同一个端点接触不同传输连通分量时保持离线，不桥接；接触同一分量多段线时稳定选择一个端口。
- 总线只从旋转/翻转后的箭头面紧邻格识别一个真实、有效拥有的容器；网络仅走背面。其他侧面不能接入。容器接入由当前方向推导，不保存手动容器绑定。
- 默认只读。实验取放仅做精确对象的整实例移动，拒绝堆叠、拆分、自动嵌套目标；重新核验连通/频道/归属，异常或未知后置条件锁定本会话写入，不自动回滚/重试。
- 主库存部件、已接入容器不在存入候选中；仅直接容器内容进入索引，嵌套物品不会被递归暴露。

## 待本地验证

按 [安装与测试](game-test.md) 先验证只读，再决定是否在独立测试档开启实验写入。分别记录通过/失败/未运行：

1. 模组扫描、Harmony 加载、F8 英文日志、F9 三个 AE 按钮、核心 3×3/其他 1×1、20 张贴图、双击和拖拽互不干扰。
2. 总线四方向与镜像、不同形状容器的实际占格接触、连接贴图更新；连接/断开后刷新、不同网络隔离、多核心和频道不足。
3. 总线接入真实容器、同类不同状态实例、数量变化、对象销毁、嵌套不暴露、重复总线不重复计数。
4. 满、拒收、锁定、所有权、目标堆叠、部分接受、回调重入、原生写入后异常；任何未知结果必须停止，不承诺补偿。
5. 保存/载入恢复、模组停用、部件清理后的卸载行为，以及其他容器模组交互。没有完成这些测试前，不声明安全或兼容。

指南参考仍为 `dragonpan2/probably-stolen-modding` 提交 `accceead2d7b63bc1746bcb053e6ab41bea75d2a`。APIChanges 的“下一次更新（开发中）”不作为版本证据；本轮使用的 API 是通过用户实际提供的程序集确认。

本轮用户反馈 F9 生成已经可用（未提供该次包的源码标识）；新一轮双击/方向/贴图改动不沿用这项结果当作游戏运行通过。
