# DeepSpaceAE 开发约束

- 开始前检查现有变更，使用当前 checkout；云任务本身隔离，未经明确要求不要创建 worktree。
- 先阅读 README.md、docs/architecture.md 和 docs/api-verification.md。默认工作流是 Core 构建与模型测试，不是自动补齐完整 AE。
- 在 Linux x64 云端执行 `bash scripts/install-env.sh` 和 `bash scripts/verify.sh`。使用 `global.json` 固定的 SDK 和锁定还原；不要无故更新依赖。
- Core 保持 `netstandard2.1` 且不依赖游戏对象。Tests 使用现代 .NET，模拟容器仅位于测试项目。
- 存储网络内存原型见 docs/network-prototype.md；运行 `bash scripts/demo.sh --smoke` 验证终端闭环。显式拓扑、单连接叶子及频道默认值是原型策略，不能视为游戏 API。不要把测试宿主作为模组发布。
- GameAdapter 默认不随解决方案编译。已有针对用户提供 100-REV3 引用的实际绑定；使用 `GameManagedDir=<批准的仓库外目录> bash scripts/build-game.sh` 显式构建，缺引用或游戏程序集哈希不符必须失败。不能伪造游戏接口或跳过门禁来声称兼容。打包用 `python scripts/package-game.py`，仅允许两个自有 DLL、四张自有 PNG、清单和说明；不得携带游戏引用。说明见 docs/game-test.md。
- 用户已明确授权上传来自其合法安装、目标版本 100-REV3（Steam Build 26284217）的必要游戏编译引用，用于云端 API 核验和开发。此授权取代此前对这些引用上传的禁止，仅限开发使用；接收后放在 checkout 外，不提交、不随模组或源码包分发。不自行登录 Steam 或获取游戏本体、资源、存档、凭据。
- 已核验的真实 API 与剩余未知见 docs/api-verification.md。静态 API 核验/编译通过不等于游戏运行通过；真实加载、移动副作用和存档往返仍须本地测试。APIChanges 中开发中内容不能单凭文档当成已发布接口。
- 模型测试通过不证明游戏安全，不承诺跨容器原子事务。真实写入试验必须有相应授权和证据。
- 游戏 DLL、本体、存档、凭据和构建缓存不入库；未来输出包只允许自有产物白名单，不递归打包测试或游戏引用目录。
- 未经明确要求，不推送远程或发布模组；不要操作本地电脑、登录 Steam、安装 Unity Editor/Wine。
