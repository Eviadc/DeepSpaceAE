# 存储、供电和原生边界

> 当前 0.8.0 行为以 [完整功能规格](feature-spec.md)、[用例矩阵](functional-cases.md) 为准。下文按版本保留历史实现背景，不能恢复旧终端渲染借用、独立总线或旧电源策略。

0.3 的 UI、拖放与计费以 [原生 UI 迁移](native-ui.md) 为准。下文保留 0.2 算法设计背景；旧候选列表、固定按钮格和 8/1/4/4 费率已由 0.3 替换。

本版应用户要求推进可用网络功能，自动合成仍后置。37f61bf 的交互由用户确认可用；新增系统的真实运行仍须本地验收。

Core 新增 StoragePolicy、StorageRouting、ExtractionFilter、CatalogSearch、NightReservation。优先级和筛选只控制授权，不充当库存；搜索记录保留实例，绝不以 ID/标签/数量判定可合并。容量槽 18+9×卡数、最多 5 卡是参考 AE2 存储总线的模组策略，不是本游戏原生限制。标签卡是本模组新增的玩法。

同容器的取出权限是各在线总线权限的并集。正筛选合并成 ID/标签哈希集合，负筛选编译为按键的位集：一个网络默认 32 频道，只需一个 64 位机器字表示各总线。查询不逐条遍历总线；泛化到 B 个排除策略，成本 O((1+T)×ceil(B/64))，T 为实例标签数，另有字符串键成本。构建使用 O(F×ceil(B/64)) 空间的稠密位集，F 为不同负筛选键数；默认 B≤31，禁止误称任意规模都是 O(1)。400 组随机策略与逐策略解释器比对，另覆盖跨 64/128 位边界。插入只在发生存入时按优先级 O(B log B) 排序，已知预检拒收才尝试下一个目标，任何进入原生移动后的异常均停止。

搜索索引构建 O(M+所有标签数)，标签下拉使用倒排列表；文本子串搜索按候选集合线性匹配分词，排序 O(R log R)。相同查询/标签/排序缓存只读结果，翻页仅生成当前 32+16 个实例的 UI，不重扫 M。全量观测和实际取放仍承担空间索引、频道评估及直接库存枚举成本。端点频道与传输分量的预算复用同一次分量遍历，不增加逐核心 BFS。静止时没有每帧全图轮询。

GameAdapter 使用真实 GameSlotInventory（核心电池）和 GameGridInventory(5,1)（升级），挂在稳定的原生 PixelWindow 子图，不能替换为只在内存中的假槽。配置和结算字段写入原生 GameItem.state 下的 deepspaceae.* TagState，随 SaveManager 的原生存档保存；不新增数据库或自有存档文件。读配置使用字典 TryGetValue，避免 GetTagReadonly 隐式创建空标签。物品/标签幽灵配置各保留 63 项，卸卡仅限制有效前缀。

供电状态 Empty→Reserved→DebitStarted→Settled；未知结果→Fault。Reserved 保存 day、最高预算和真实 battery.uniqueId，结算前先持久化 DebitStarted；加载后不得重放中断的扣电。只有 Empty 或已结算且日计数推进才允许更换。日预算固定 kJ：核心 8、线缆 1、总线/终端 4。原生 PowerHelper.CanDrawPowerSource/DrawPowerSource 执行供电；普通电池检查前后能量精确差额，超科技电池遵守原版无限能量/输出上限，不伪造能量下降。转换器拒收。现有预留即使核心移入袋中也在睡眠遍历原生物品时结算；未激活且从未预留的袋内核心不额外收费。

静态调用顺序已核验：PlayerStore.EndNight 发 OnGoingSleepEarly，随后处理原生夜间服务，最后进入 MORNING；StoreStation.StartDay 增加 dayCounter。使用前者结算、后者后置钩子解锁；不依赖仅声明而未找到调用者的 OnWakingUp 事件。原生 SlotMarker.TryAcceptOnce 没有重新执行 MayRemove，因此电池/升级槽还通过 maxSlotRemoveItemFunc 禁止取出；GraphUtils.SwapItemsInPlace 增加只作用于 AE 子库存的锁定检查，避免原生电池交换绕过。没有把 LOCKED_TAG 强加给电池覆盖其原生委托。

UI 使用真实 CustomUIBuilder 的 Grid/Input/Dropdown/Icon/Scroll。不需要上传额外 Unity UI/TextRendering DLL：未使用要求这些类型出现在编译签名中的 BeginRow/BeginColumn 等方法，不伪造 TextAnchor。核心管理、总线设置和终端替代原测试界面；移除诊断窗口和自定义 F9 项。原生异常完整记入 ASCII 转义日志，界面只保留操作状态和失败原因。

拾荒使用 LootRegistry 原生 POI_* 组和 ModHelper 注册独立表，权重只添加一次，停用移除自有组入口，不替换原生表。具体 POI 名来自运行时真实注册表，无可用组时明确英文告警。

图库精灵在资源加载后缓存，库存增删/数量、相关物品验证和位姿/拥有状态变化合并下一次更新；更新全部相关明暗和线缆端口。目录窗口订阅其实际容器的原生 End/数量回调，关闭解除；某些其他模组绕过这些事件时仍可手动刷新，所有写入必须新鲜核验。云端没有启动 Unity，不声明穷尽所有入口或存档中断情况，不承诺跨容器原子性。

重复 ValidateShapeState 不直接触发目录重扫：缓存每个已观察实例的数量、名称、精灵和启用标签，只有相关元数据/位姿/拥有状态实际变化才排队刷新。电池只在真实能量/输出变化时使供电失效。此比对与当前被验证实例的标签数成正比，不扫描其余库存。图标仅对当前页按需解析并缓存，不在每次写入预检加载整网精灵。
