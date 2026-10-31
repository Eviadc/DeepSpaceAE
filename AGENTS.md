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
