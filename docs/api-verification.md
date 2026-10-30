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
| 主库存 | `PlayerStore.Instance.GetStoreInventory()` 返回 GameGridInventory；`childItems` 与 `parentInventory` 可交叉检查 | 未运行 |
| 相邻与旋转 | `GameItem.IsAdjacent(GameItem)` 委托同库存原生相邻检查，使用当前 modifiedShape；本模组不猜坐标、占格或旋转映射 | 未运行 |
| 真实容器 | `GameItem.child`、`GraphNodeStorage.children`、`GameInventory.GetParentItem()`、`childItems`；仅枚举直接库存，遇到库存停止向下遍历 | 未运行 |
| 物品身份 | 真实对象引用与 unitCount；模组使用弱引用会话 Guid 区分实例，不把 uniqueId 当作已证明的持久身份，不序列化完整游戏状态或自行合并 | 未运行 |
| 终端 UI | `CustomUIManager.Window`、`CustomUIBuilder` 标签/按钮/开关/滚动/显示、窗口关闭；部件可挂原生激活回调 | 未运行 |
| 变更时机 | 原生库存/物品有逐实例回调，但未确认覆盖所有变更的全局事件；当前只在用户刷新/操作时重读，读取期间不订阅/替换原生库存回调 | 未运行 |
| 原生搬运 | `MayRemove`、`MaxNumRemove`、`TryFindOneValidInventorySlot`、`SlotMarker.TryAcceptOnce`；原生可能先 Expel 后 UncheckedAccept，中间触发回调 | 未运行 |
| 存档 | 游戏有 SaveItemNode 等原生数据结构；本模组不增加自有存储文件或持久容器身份 | 保存/载入/停用均未运行 |

这些符号已经过真实引用编译，不能由此推导 Unity 实际加载、菜单可见、回调顺序、原生搬运安全或存档正确。尤其原生 TryAcceptOnce 不提供跨容器原子事务；失败不能盲目重试或补造物品。

## 当前实现边界

- 四种部件为自有 1×1 定义，PNG 由原创 SVG 转换；不对其他物品的形状做硬编码。
- 核心/管线为传输网络；终端/总线是叶子。同一个端点接触不同传输连通分量时保持离线，不桥接；接触同一分量多段线时稳定选择一个端口。
- 总线侧边接触一个真实、有效拥有的容器时自动接入；多个候选由用户在终端选择。会话选择不保存，载入后重新确认。
- 默认只读。实验取放仅做精确对象的整实例移动，拒绝堆叠、拆分、自动嵌套目标；重新核验连通/频道/归属，异常或未知后置条件锁定本会话写入，不自动回滚/重试。
- 主库存部件、已接入容器不在存入候选中；仅直接容器内容进入索引，嵌套物品不会被递归暴露。

## 待本地验证

按 [安装与测试](game-test.md) 先验证只读，再决定是否在独立测试档开启实验写入。分别记录通过/失败/未运行：

1. 模组扫描、加载入口、F8 日志、F9 两个按钮、四种贴图/1×1 形状。
2. 主库存物品旋转、不同形状容器的原生相邻结果；连接/断开后刷新、不同网络隔离、多核心和频道不足。
3. 总线接入真实容器、同类不同状态实例、数量变化、对象销毁、嵌套不暴露、重复总线不重复计数。
4. 满、拒收、锁定、所有权、目标堆叠、部分接受、回调重入、原生写入后异常；任何未知结果必须停止，不承诺补偿。
5. 保存/载入恢复、模组停用、部件清理后的卸载行为，以及其他容器模组交互。没有完成这些测试前，不声明安全或兼容。

指南参考仍为 `dragonpan2/probably-stolen-modding` 提交 `accceead2d7b63bc1746bcb053e6ab41bea75d2a`。APIChanges 的“下一次更新（开发中）”不作为版本证据；本轮使用的 API 是通过用户实际提供的程序集确认。
