# DeepSpaceAE 开发约束

- 开始前检查现有变更，使用当前 checkout；云任务本身隔离，未经明确要求不要创建 worktree。
- 先阅读 README.md、docs/architecture.md 和 docs/api-verification.md。默认工作流是 Core 构建与模型测试，不是自动补齐完整 AE。
- 在 Linux x64 云端执行 `bash scripts/install-env.sh` 和 `bash scripts/verify.sh`。使用 `global.json` 固定的 SDK 和锁定还原；不要无故更新依赖。
- Core 保持 `netstandard2.1` 且不依赖游戏对象。Tests 使用现代 .NET，模拟容器仅位于测试项目。
- 存储网络内存原型见 docs/network-prototype.md；运行 `bash scripts/demo.sh --smoke` 验证终端闭环。显式拓扑、单连接叶子及频道默认值是原型策略，不能视为游戏 API。不要把测试宿主作为模组发布。
- GameAdapter 默认不编译；显式构建的缺依赖/未实现错误是有意的。不能伪造游戏接口、下载游戏文件或跳过门禁来声称兼容。
- 下一阶段优先真实 API 核验，尤其主物品栏坐标、旋转占格、事件、原生搬运和保存/载入。APIChanges 中开发中内容不能当成已发布接口。
- 模型测试通过不证明游戏安全，不承诺跨容器原子事务。真实写入试验必须有相应授权和证据。
- 游戏 DLL、本体、存档、凭据和构建缓存不入库；未来输出包只允许自有产物白名单，不递归打包测试或游戏引用目录。
- 未经明确要求，不推送远程或发布模组；不要操作本地电脑、登录 Steam、安装 Unity Editor/Wine。
