# DeepSpaceAE 开发约束

- 开始前检查现有变更，使用当前 checkout；云任务本身隔离，未经明确要求不要创建 worktree。
- 先阅读 README.md、docs/architecture.md 和 docs/api-verification.md。默认工作流是 Core 构建与模型测试，不是自动补齐完整 AE。
- 在 Linux x64 云端执行 `bash scripts/install-env.sh` 和 `bash scripts/verify.sh`。使用 `global.json` 固定的 SDK 和锁定还原；不要无故更新依赖。
- Core 保持 `netstandard2.1` 且不依赖游戏对象。Tests 使用现代 .NET，模拟容器仅位于测试项目。
- 存储网络内存原型见 docs/network-prototype.md；运行 `bash scripts/demo.sh --smoke` 验证终端闭环。显式拓扑、单连接叶子及频道默认值是原型策略，不能视为游戏 API。不要把测试宿主作为模组发布。
- GameAdapter 默认不随解决方案编译。已有针对用户提供 100-REV3 引用的实际绑定；使用 `GameManagedDir=<批准的仓库外目录> bash scripts/build-game.sh` 显式构建，缺引用或游戏程序集哈希不符必须失败。不能伪造游戏接口或跳过门禁来声称兼容。打包用 `python scripts/package-game.py`，仅允许两个自有 DLL、四张自有 PNG、清单和说明；不得携带游戏引用。说明见 docs/game-test.md。
- 用户已明确授权上传来自其合法安装、目标版本 100-REV3（Steam Build 26284217）的必要游戏编译引用，用于云端 API 核验和开发。此授权取代此前对这些引用上传的禁止，仅限开发使用；接收后放在 checkout 外，不提交、不随模组或源码包分发。不自行登录 Steam 或获取游戏本体、资源、存档、凭据。
- 已核验的真实 API 与剩余未知见 docs/api-verification.md。静态 API 核验/编译通过不等于游戏运行通过；真实加载、移动副作用和存档往返仍须本地测试。APIChanges 中开发中内容不能单凭文档当成已发布接口。
- 100-REV3 的屏幕主库存是 EmporiumEntry.Instance.invElement，由 GameSession.MainInventory() 统一取得。不要把 PlayerStore.GetStoreInventory() 当成主库存：它返回另一份独立且未挂主窗口的库存，原包 c5d6faa 已因此出现不可见生成和错误网络观测；详见 docs/game-runtime-feedback.md。禁止靠改计数、重复过滤或自动销毁物品掩盖错误根库存。
- 模型测试通过不证明游戏安全，不承诺跨容器原子事务。真实写入试验必须有相应授权和证据。
- 游戏 DLL、本体、存档、凭据和构建缓存不入库；未来输出包只允许自有产物白名单，不递归打包测试或游戏引用目录。
- 用户已明确授权：今后其要求的修复，完成适当验证后默认提交并直接推送到 Eviadc/DeepSpaceAE 的 main，无需逐次确认。保留远端已有工作，不强推；若无法安全合并或认证失败，报告具体障碍。安装 ZIP、游戏引用、存档和凭据仍不得进入 Git。
- 模组发布与附件交付按用户已给出的具体授权和渠道要求执行；不要操作本地电脑、登录 Steam、安装 Unity Editor/Wine。
