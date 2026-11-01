# DeepSpaceAE 开发约束

- 开始前检查现有变更，使用当前 checkout；云任务本身隔离，未经明确要求不要创建 worktree。
- 先阅读 README.md、docs/architecture.md 和 docs/api-verification.md。默认工作流是 Core 构建与模型测试，不是自动补齐完整 AE。
- 在 Linux x64 云端执行 `bash scripts/install-env.sh` 和 `bash scripts/verify.sh`。使用 `global.json` 固定的 SDK 和锁定还原；不要无故更新依赖。
- Core 保持 `netstandard2.1` 且不依赖游戏对象。Tests 使用现代 .NET，模拟容器仅位于测试项目。
- 存储网络内存原型见 docs/network-prototype.md；运行 `bash scripts/demo.sh --smoke` 验证终端闭环。显式拓扑、单连接叶子及频道默认值是原型策略，不能视为游戏 API。不要把测试宿主作为模组发布。
- GameAdapter 默认不随解决方案编译。已有针对用户提供 100-REV3 引用的实际绑定；使用 `GameManagedDir=<批准的仓库外目录> bash scripts/build-game.sh` 显式构建，缺引用或游戏程序集哈希不符必须失败。不能伪造游戏接口或跳过门禁来声称兼容。打包用 `python scripts/package-game.py`，仅允许两个自有 DLL、55 张授权 AE2 衍生 PNG、清单、许可/来源和说明；不得携带游戏引用。说明见 docs/game-test.md。
- 用户已明确授权上传来自其合法安装、目标版本 100-REV3（Steam Build 26284217）的必要游戏编译引用，用于云端 API 核验和开发。此授权取代此前对这些引用上传的禁止，仅限开发使用；接收后放在 checkout 外，不提交、不随模组或源码包分发。不自行登录 Steam 或获取游戏本体、资源、存档、凭据。
- 已核验的真实 API 与剩余未知见 docs/api-verification.md。静态 API 核验/编译通过不等于游戏运行通过；真实加载、移动副作用和存档往返仍须本地测试。APIChanges 中开发中内容不能单凭文档当成已发布接口。
- 100-REV3 的屏幕主库存是 EmporiumEntry.Instance.invElement，由 GameSession.MainInventory() 统一取得。不要把 PlayerStore.GetStoreInventory() 当成主库存：它返回另一份独立且未挂主窗口的库存，原包 c5d6faa 已因此出现不可见生成和错误网络观测；详见 docs/game-runtime-feedback.md。禁止靠改计数、重复过滤或自动销毁物品掩盖错误根库存。
- 当前真实输入与性能边界见 docs/performance.md。AE 部件只允许双击打开；核心 3×3；总线箭头面接容器、背面接线；线缆使用 16 种自动连接贴图。禁止恢复组件两两比较、逐设备重复全图 BFS、每帧全量扫描，或用缓存页面授权写入。新增素材仍须显式纳入打包白名单。
- 模型测试通过不证明游戏安全，不承诺跨容器原子事务。真实写入试验必须有相应授权和证据。
- 游戏 DLL、本体、存档、凭据和构建缓存不入库；未来输出包只允许自有产物白名单，不递归打包测试或游戏引用目录。
- 用户已明确授权：今后其要求的修复，完成适当验证后默认提交并直接推送到 Eviadc/DeepSpaceAE 的 main，无需逐次确认。保留远端已有工作，不强推；若无法安全合并或认证失败，报告具体障碍。安装 ZIP、游戏引用、存档和凭据仍不得进入 Git。
- 模组发布与附件交付按用户已给出的具体授权和渠道要求执行；不要操作本地电脑、登录 Steam、安装 Unity Editor/Wine。

- 0.2 存储与供电约束见 docs/storage-features.md。新增真实子库存的节点顺序保持稳定；加载不可重新扣电或预留已结算电池；未知扣电/移动结果保持锁定。开发提示/UI 已按用户要求移除，取放为正式操作，但继续要求每次原生预检和未知结果停止。AE2 素材为 CC BY-NC-SA 3.0，不得沿用早期原创素材的 CC0 声明或漏带署名。

- 0.3 UI 规则见 docs/native-ui.md：控制器用原生机器面板和真实电池槽；总线九列幽灵格，禁止全物品候选枚举；终端展示真实占格投影，不拥有/克隆/暂存真实物品，不添加主栏候选面板。保留旧子库存保存图索引，使用原生 StealPixelElement 借用渲染。原版 TypeHelper.GetTypeDisplayName 本地化公开物品类别。功耗为核心/线缆/总线/终端 2/0/1/1 kJ，迁移只改未扣预算，不重置已扣或未知结算。

- 0.3.1 加载回归见 docs/load-troubleshooting.md：GameItem.Destroy 是 abstract，禁止恢复其 Harmony 目标，仅保留具体 GameItemElement 覆盖。scripts/build-game.sh 必须通过只读 PE 补丁元数据审计；打包校验审计绑定当前 DLL 哈希。显示文案用官方 Localization/en.csv、zh.csv 和 ASCII 键，在工厂/钩子且资源就绪后解析，禁止 Init/OnEnable 解析。补丁/日志问题优先读取 Player.log，不全局静音 TMP 或错误。包白名单另允许 2 CSV、加载诊断说明和独立日志提取脚本。

- 0.3.2 修订见 docs/native-ui.md：借用槽包装必须参与原生 MachineryWidget 尺寸刷新、清除旧偏移并在关闭归还，不改保存图；终端无手动刷新，拖拽期间的 dirty 事件不得丢弃。原版 CustomUI 控件设计比例为 2，要整体缩放并保留下拉/勾选图形。素材现为 55 PNG；核心 12 帧复用真实 SpriteAnimationHelper，离线/停用停止，不每帧扫描网络。

- Windows 本地工作流见 README 的 Windows 本地开发段；用户已授权从本地 Steam 安装引用 DLL、适配当前 100-Rev6 / Build 26337366，并在编译及元数据审计成功后部署到指定 Mods。此项取代上文云端背景下的“不操作本地电脑”限制，仅限本次开发和模组部署，不登录 Steam、不启动游戏或操作存档。

- 0.3.5 终端显示按用户要求使用独立纯 UI 镜像，禁止恢复借用真实物品 handler/middle 的方案。两个界面的命中必须指向同一真实 GameItem；禁止复制 GameItem 或把镜像加入保存图。滚动及可见区域限制保留；原版交互复用和跨模组兼容边界见 docs/native-ui.md。电池/升级槽的既有独立借用布局不在此次终端物品显示改动范围内。

- 0.4.0 取代旧“独立总线背接线缆”的设计：用户明确不要求兼容旧独立总线，手动拆换。总线作为线缆原生子库存中的真实升级，最多四个，方向存在总线 state 并随线缆本地旋转映射；安装面阻断外部连接，优先未连接面，否则切断一个连接。禁止自动转换或删除旧总线。筛选使用独立固定 GameGridInventory，终端保留滚动及双视图；详见 docs/native-ui.md。

- 0.4.1 线缆不得恢复独立 UI，双击总线模型部位直开总线设置，扳手按部位直拆。总线贴图按固定 AE2 模型 UV 投影；新增 8 个显式白名单 PNG，总计 63。终端框选只处理自身可见镜像，复用同一真实物品和原版选择状态；禁止枚举/伪造保存图或移动原容器渲染来支持框选。群组释放仍须逐项真实预检和未知结果停止。详见 docs/native-ui.md。

- 0.5 增量以 docs/feature-spec.md、docs/functional-cases.md、docs/version-maintenance.md 为维护合同。驱动器 2×2、六个真实 1×2 槽；大小盘均 1×2、64/256 实际占格容量，真实实例保存，禁止递归存盘。原有组件保存图索引不改；新增 4 张贴图，总数 67。普通电池 Shortfall 当天断电并解锁，未结算峰值不跨日；电流转换器走原生电力服务，只以实际付款事件结算，已知未付款次日锁槽断电，未知 DebitStarted 不重放。当前阶段完成后统一由用户实机测试，禁止把静态验证写成实机通过。

- 0.6 液体以 docs/feature-spec.md 的液体章节和 LIQ 用例为准。只读现代 WaterHelper 化学模型的真实微升成分；不同成分不合并，同成分也保留来源/温度/过滤器状态。终端液体镜像不产生 GameItem 或保存图，采用原生选中→Target 回调；不得手改成分标签或二次倒液。保留原版主动混液/过滤/滴管反向规则，执行前重查网络权限，液体卡片不物理搬走源罐。原版 RaycastElement 先遇到的 element 即命中身份，文字区也须绑定真实容器。资产白名单仍为 67 PNG。

- 0.7 输入/输出总线方向按 AE2：输入从机器 output 收回网络，输出送入机器 input。只认真实 MachineryPanel.role 及直属库存，排除 NO_HOPPER、电池/模块/芯片/便签等，不猜未知机器槽。沿用四面线缆升级和九列筛选。原生 HopperHelper.RunWindow 后执行有界批次（每总线 8/全批 64），无终端也可运行，不每帧扫描。夜间必须同日同源已结算预算，不二次扣电；实际写入复查原生槽和权限，拆分/堆叠通过原生 SlotMarker，未知结果锁写。新增素材 20 张，总白名单 87。用例 IO-*，保存图旧索引不改。

- 0.8 获取途径：王尔德基础 Referral 分类 AE工程师（deepspaceae.ae_engineer），用户确认费用 200；一次性 16 件基础组件，电池自备。通过原生 ModData 排队并保留 in-flight，正确主栏满时保留权益，禁止用 EmporiumTryAdd 的满库存销毁兜底或未知结果重发。史蒂夫 deepspaceae.steve 自第 15 天起每日 5% 候选，与升级独立；12 种组件价格/独立概率在 AcquisitionCatalog。原版客户队列/当前客户不保存，日内 roll/货单/served 记录防重入，不承诺恢复未购货台。官方扩展点无新增补丁；新增独立头像 1 张，总 PNG 88（87 AE2 + 1 Steve），另带来源/提示词说明，不能混称全部 AE2 授权。用例 ACQ-*，真实交易/头像比例/保存待实机。

- 0.9 能源接收器为 2×2、三真实电源槽、1 频道/0 kJ 叶子。控制器与在线接收器共用最少数量电池，等数量优先保留；只锁必要份额。供电池及核心的持久计划、欠费/未知扣费锁定、断线当日责任见 docs/feature-spec.md 和 ENERGY 用例。不得改变旧核心保存图；新素材白名单总计 90 PNG（89 AE2 衍生 + 1 独立头像）。
- 0.10 抽屉与终端筛选以 docs/drawers-and-filters.md 为准。抽屉2×2、基础16件、五张真实容量卡逐张×4；真实实例保存，状态不同不得混入，拆卡不足拒绝，不暴露卡槽为网络物品。终端追加单张真实标签卡槽，装卡后完整标签/原版当前顾客需求；本地主栏抽屉免电不适用于网络终端。共91 PNG，抽屉MIT与AE2 CC许可分立。禁止把容量/槽静态验证写成保存重载通过。
- 0.10.1抽屉用独立原生面板，和终端镜像可同时显示操作；本地真实库存显示一个代表与总数，取出恢复handler可见。代表对象在库存事件缓存，单物品回调不得重新复制全部childItems。渲染包装不得重排保存图，终端继续纯UI镜像，不借用抽屉代表。见 docs/native-ui.md 与 docs/runtime-pilot-results-0.10.md。
