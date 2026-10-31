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


## 0.2 本次验证

- 环境安装脚本成功；SDK 10.0.403，Linux x64。
- 干净核心构建和真实 100-REV3 引用构建通过，均零警告/零错误。
- xUnit：66 通过、0 失败、0 跳过（含 400 组随机筛选策略、320 组原拓扑比对、跨机器字位集和电池结算状态测试）。
- 内存终端 smoke 通过；43 张 PNG 可重复生成与输入来源哈希检查通过。
- 真实游戏：0.2 加载/UI/夜间结算/工具/掉落/存档往返全部未运行。用户确认可用的是前一版 37f61bf。

Core 模型性能，预热后 5 次中位数。31 个总线筛选策略；过滤列是对给定数量实例逐项查询，目录列含索引构建、文本+标签查询和数量排序。缓存翻页复用结果。不是 Unity/Mono 或帧率保证。

| 节点数 | 实例数 | 拓扑评估 ms | 筛选 ms | 目录构建/查询/排序 ms |
| --- | --- | --- | --- | --- |
| 201 | 100 | 0.672 | 0.045 | 0.073 |
| 2001 | 1000 | 10.743 | 0.517 | 0.77 |
| 10001 | 5000 | 40.506 | 1.353 | 2.956 |
| 20001 | 10000 | 55.644 | 1.726 | 16.746 |

原始测量保留在忽略目录 artifacts/validation/storage-performance.json；安装包 build-info.json 保存对应完整源码提交及逐文件哈希。新功能与本地验收边界见 docs/storage-features.md、docs/game-test.md。


## 0.3 原生 UI 迁移验证（2026-10-31，Asia/Shanghai）

- Core 干净构建、真实 REV3 引用构建：通过，零警告/零错误。
- xUnit：73 通过、0 失败、0 跳过。包含实际占格投影、孔洞、分页、不遗漏大物品、显示重排、实例保留、本地化类别搜索、零电缆功耗和未扣预算迁移。
- 内存 smoke、43 张已授权 PNG 重现检查：通过。
- 原生 UI 比例/鼠标事件/借用槽/旧存档往返：未运行，须游戏实例验收。
- REV5 构建/运行：未运行，缺对应真实程序集。本版编译基线仍是 REV3，未绕过门禁。

Core 投影布局探针，固定每实例 2×3 六格，预热后 5 次中位数；不包含 Unity 绘制/原生拖拽或 FPS。

| 实例数 | 排放 ms | 当前线程分配字节 |
| --- | --- | --- |
| 100 | 0.174 | 99448 |
| 1000 | 1.346 | 952552 |
| 5000 | 3.333 | 4226144 |
| 10000 | 9.524 | 8735504 |

原始结果：忽略目录 artifacts/validation/native-ui-performance.json。对应提交由安装包 build-info.json 记录；[UI 实现边界](native-ui.md) 和 [本地验收](game-test.md) 说明真实运行尚待完成。


## 0.3.1 加载阻断修复（2026-10-31，Asia/Shanghai）

- 旧 6d03e55 自有 DLL 对真实 REV3 元数据审计：14 通过、1 **预期失败**；目标 GameItem.Destroy 是 abstract，没有可修补 IL。这是针对已交付包的负向复现，不是假设。
- 修复后真实引用编译：通过，零警告/零错误；14 个补丁声明的目标方法体、字段/参数元数据检查通过。
- xUnit：76 通过、0 失败、0 跳过。包含不依赖游戏的抽象/具体 CLR 方法体回归和 93 个双语 CSV 键、占位符检查。
- Python 日志提取器：4 项通过，覆盖 2 万条 OfficeCodePro/[Text] 警告夹杂真正异常、其他字体/对象/真实错误不误删、缺失翻译保留、输出上限。
- 43 张授权 PNG 校验通过，图像未改。
- 游戏内 Harmony 执行、注册菜单、F8 字体渲染、REV5 和实际 UI：**未运行/未核验**。元数据通过不代表 Unity 真正加载通过。

旧负向报告在忽略目录 artifacts/validation/old-6d03e55/patch-audit.txt；当前正向报告为 artifacts/validation/game-patch-audit.json，绑定实际 Adapter SHA-256。打包缺少、失败或过期报告必须拒绝。具体根因、官方依据和不依赖 F8 的取证步骤见 [加载诊断](load-troubleshooting.md)。

## 用户日志核对及重新验证（2026-10-31，Asia/Shanghai）

- 用户 Player.log：1,728 行；确认 0.3.0-alpha 在 Harmony.PatchAll → GameItem.Destroy 处报 Method has no body，随后 Init failed、running=False。这是旧版实机**失败**证据；不等同修复版测试。1,100 条 OfficeCodePro/[Text] 字体警告单独计数，原日志不修改、不入库。
- 当前源码已经包含 `9328ca5` 的修复；本轮补充确认的诊断记录，没有新加功能、改变版本或扩大运行时补丁范围。
- `bash scripts/install-env.sh`：通过，锁定还原成功。
- `bash scripts/verify.sh`：通过；干净 Release 构建零警告/零错误，xUnit **76 通过、0 失败、0 跳过**。
- `python3 scripts/test-extract-load-log.py`：**4 通过、0 失败**。实际日志提取完整保留异常栈，报告未截断。
- `GameManagedDir=/workspace/.deepspaceae/game-references/100-REV3 bash scripts/build-game.sh`：通过，零警告/零错误；**14 项补丁元数据检查通过**。
- 再次比较原 6d03e55 ZIP 内的自有 Adapter DLL 与负向试验输入，内容相同；重跑审计退出 1，仅 GameItem.Destroy 失败，14 通过/1 预期失败。门禁能拒绝此次已确认的错误类型。
- 修复版游戏加载、真实终端拖放/存档、REV5 对应引用：**未运行/未核验**。按用户要求搁置字体问题；不把旧日志当作新版通过证据。

实际源码提交和包内逐文件校验以对应安装包的 build-info.json 为准。兼容范围和真实游戏验收仍遵守 [API 核验](api-verification.md) 与 [安装验收](game-test.md)，不跳过失败的安全补丁来宣称兼容新版本。
