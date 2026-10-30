# 本轮实际执行记录

日期：2026-10-30（用户时区 Asia/Shanghai）。范围：工程初始化与核心模型测试闭环。

**对应源码提交：`4b691acc7ac474df75d7daadd55ab2ddd47e0019`**，本地分支 `work` 的初始提交。本文在之后的纯文档提交中记录结果；测试后未修改源码、项目配置或脚本。未推送远程，未发布模组。原远程 `main` 不存在，不能声称远程克隆已可复现这些本地源码。

## 环境及执行命令

- 仓库初始为空、无提交、无 AGENTS.md、无未跟踪用户文件；无可用 dotnet。未覆盖已有工作。新增的 AGENTS.md 记录了后续边界。
- Debian 13、Linux x64。SDK `10.0.403`，MSBuild `18.9.15`，运行时 `10.0.14`。SDK 安装在 `/workspace/.deepspaceae/dotnet`。
- SDK 来自 Microsoft HTTPS；安装器固定 SHA-512：`b7b3d8461cf98b07d2552ce9199702fa3ad010ac2671a5f8cd5136f7534e50f6855ba7bbb02b11494563f1d04156276a31cda1103b900e5eecd0d9473dc7b9c5`。未关闭 TLS、包签名或校验验证。
- 首次还原曾失败：NuGet 命名互斥量尝试写只读用户目录。通过 `scripts/env.sh` 把 XDG 运行/数据目录设为仓库外的可写私有目录修复；没有修改 HOME，也没有跳过迁移/验证。

在 `/workspace/DeepSpaceAE` 实际执行：

```bash
# 独立空工具目录：真实下载、SHA-512 校验、安装和锁定还原，退出 0
DEEPSPACEAE_ENV_ROOT=/tmp/deepspaceae-install-check bash scripts/install-env.sh

# 保留工具目录重复安装/还原，退出 0
bash scripts/install-env.sh

# 在上述源码提交上，锁定还原 -> clean -> build -> test，退出 0
bash scripts/verify.sh
```

验证脚本使用 `Release`、`--no-restore` 构建和 `--no-build --no-restore` 测试，构建并发上限为 2。捕获日志时使用 Bash `set -euo pipefail` 与 tee，保留原命令失败状态。

## 结果

| 检查 | 实际结果 |
| --- | --- |
| 独立空目录安装固定 SDK、固定校验值验证、锁定还原 | 通过，退出 0 |
| 保留环境的重复安装/还原（包括显式还原 GameAdapter） | 通过，退出 0 |
| 默认解决方案 clean + Release 构建 | 通过；Core 与 Tests，0 警告、0 错误 |
| xUnit 模型测试 | **13 执行，13 通过，0 失败，0 跳过**；不是零测试运行 |
| 显式 GameAdapter 构建，未设置 GameManagedDir | **构建失败，退出 1，EAE001**；缺依赖门禁符合预期，无适配 DLL |
| 显式 GameAdapter 构建，GameManagedDir 指向缺引用路径 | **构建失败，退出 1，EAE002**；缺文件门禁符合预期，无适配 DLL |
| EAE003（真实引用齐备后的未实现门禁） | 未运行；没有真实引用，不创建假 DLL 测试 |
| 忽略规则样例 | 通过；10/10：引用、游戏目录/本体、存档、凭据、缓存、产物均排除 |
| 源码/构建目录检查 | 通过；源码提交只有 25 个文本/配置文件；src/tests/artifacts 中无 Assembly-CSharp、UnityEngine、Harmony、Steamworks DLL；GameAdapter DLL 数量为 0 |
| Shell 语法、git diff --check | 通过 |
| 真游戏加载、主库存/旋转/事件/搬运/存档、性能与其他模组兼容 | **未运行、未核验** |
| 模组或 NuGet 打包/发布 | 未运行；当前所有项目 IsPackable=false，没有输出包，不分发测试目录 |
| 新云任务的快照恢复 | 未运行；本地提交未推送，不能宣称新任务可恢复 |

测试包含：同物品 ID 不同状态保留两实例、同 ID 同状态也不自动合并、模糊重复实例/容器拒绝、快照与后续集合变更隔离、无效身份拒绝、准确移动指定实例、容量不足、拒收、同请求重放、请求 ID 冲突、批次部分失败且不回滚已成功项目、同一批次重复实例不复制。部分失败仅注入在单项写入前，不模拟原生搬运写入后抛异常、并发或存档崩溃。

当前运行的 TRX 在 `artifacts/test-results/core-model.trx`，安装和完整验证输出在 `artifacts/validation/install.log`、`artifacts/validation/verify.log`，适配诊断输出在同目录 `adapter-unset.log`、`adapter-missing.log`。这些是本机生成且被忽略的证据；新环境可执行脚本重建，不应把旧 TRX 当作新测试结果。

## 云环境配置与下一步

已通过 setup 技能保存云环境草稿的 `install_script` 与 `start_skill`：调用仓库安装器、逐任务激活环境、执行核心验证、说明适配层门禁及隔离 checkout 的使用方式。不需要新增网络域名、变量或密钥。

保存草稿不等于执行或发布快照。当前实例的核心工作流已验证；如需让配置成为后续云任务使用的版本，需在环境设置中审阅、保存并发布**云环境**。本轮未执行发布，也未验证新任务恢复；仓库源代码仍仅存在本地提交中。

下一步优先 [真实 API 核验](api-verification.md)。本轮到此停止，不进入完整可玩模组实现。不把模拟模型通过解释为真实取放或存档安全，不承诺跨容器原子事务。
