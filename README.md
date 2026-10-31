# DeepSpaceAE

《深空当铺：可能是偷的》的存储网络模组工程。目标依次是高效率、可扩展、高兼容：计划在主物品栏摆放核心、连接件和接口形成网络，首版聚焦真实容器的统一索引与安全取放，自动合成后置。

**当前版本 0.3.2-alpha，目标 100-REV3。** 基础交互版本 37f61bf 已由用户确认可用。本版继续实现整网库存搜索、标签筛选与排序、总线优先级和访问规则、容量/标签升级、扳手、拾荒掉落、真实电池槽、按夜结算及次日解锁。移除原测试/诊断 UI；所有组件双击打开，离线时熄灭。核心 3×3，总线箭头接容器、背面接线，线缆自动连接。

0.3.2 修正终端投影命中和拖拽来源、借用槽尺寸与偏移，取消手动刷新；搜索/下拉/复选使用原版控件及正确比例，统计不再用按钮。线缆连接段采自 AE2 模型 UV，供电核心播放 12 帧灯光。游戏内验收仍待完成，详见 [UI 修订](docs/native-ui.md#032-本地反馈修订)。

0.3.1 修复了 0.3.0 对抽象 GameItem.Destroy 打补丁导致的加载阻断，新增真实引用的补丁元数据门禁及官方双语 CSV。用户上传的 Player.log 已确认旧版因此加载失败、无法注册组件；修复版实机结果仍待验证。F8 字体警告暂不处理，详见 [加载诊断](docs/load-troubleshooting.md)。

0.3 按用户反馈改用原生机器面板、真实拖放电池槽、AE 九列幽灵筛选和按实际占格展示的终端投影；移除自动取电池及主栏候选列表。电缆耗电归零，核心/总线/终端下调至每晚 2/1/1 kJ。详见 [原生 UI 迁移](docs/native-ui.md)。

**本版重构未在真实游戏运行。REV5 对应程序集仍未提供，当前仅以 REV3 引用编译核验。** 已使用用户提供的真实程序集核验并编译；模型测试不证明真实移动、夜间回调或存档安全，不承诺跨容器原子事务。终端现在可直接执行整实例取放，未知原生结果停止操作；自动合成仍后置。

[安装和本地验收](docs/game-test.md) · [新功能边界](docs/storage-features.md) · [复杂度](docs/performance.md)。AE2 素材采用 CC BY-NC-SA 3.0，已保留[署名和许可](assets/THIRD-PARTY-NOTICES.md)。原始纹理和模型来自公开 AE2 源码固定提交，没有下载本游戏资源或 Minecraft 本体。

## 工程

一个 `DeepSpaceAE.sln`，三个项目：

| 项目 | 目标框架 | 本轮边界 |
| --- | --- | --- |
| `src/DeepSpaceAE.Core` | `netstandard2.1` | 不可变模型、显式拓扑、频道分配、终端按实例的统一索引；无游戏依赖 |
| `src/DeepSpaceAE.GameAdapter` | `netstandard2.1` | 真实游戏入口、原生电池/升级槽、容器观测与正式 UI；显式指定合法引用并核验哈希后构建 |
| `tests/DeepSpaceAE.Tests` | `net10.0` | xUnit 测试、测试专用内存容器、可操作的开发终端（CLI） |

`netstandard2.1` 是库的兼容性目标，不是游戏运行验证。测试进程运行在现代 .NET 10，不是 Unity/Mono。

## 运行终端原型

完成下面的环境安装后：

```bash
bash scripts/demo.sh --smoke # 自动验证浏览、取出、存入、断线、频道不足与重连
bash scripts/demo.sh         # 交互终端；输入 help 查看命令，quit 退出
bash scripts/demo.sh --report artifacts/network-prototype.html # 输出带占位图标的离线快照，文件需尚不存在
```

默认连接：核心 → 管线 → 终端、总线 A、总线 B。总线 A 接入两节状态不同的电池，总线 B 接入矿石；另一个未接入的容器不可见。示例操作：

```text
list battery
take 1111
inventory
put 1111 bus-b
disconnect trunk bus-a
list
channels on 1
status
channels off
connect trunk bus-a
list
```

`1111` 为实例 ID 前缀，不是物品类型 ID。默认终端和总线各占 1 频道，核心承载 32、每段管线承载 8；可调整或关闭限制。具体规则、算法限制见 [网络原型说明](docs/network-prototype.md)。[占位素材](assets/placeholders/README.md) 用于离线 HTML 快照，尚未导入游戏；CLI 以文字显示设备，不是游戏内 UI。交互中可用 `export artifacts/after-move.html` 导出当前真实模型状态，不存在独立的 JavaScript 网络实现。

## 云端安装、干净构建与测试

前提：Linux x64、Bash、curl、tar、sha512sum，以及 .NET 所需的系统运行库（本次 Debian 13 已具备）。不需要 Steam、游戏引用、Unity Editor、Wine、数据库、后台服务或凭据。

```bash
cd /workspace/DeepSpaceAE
bash scripts/install-env.sh
bash scripts/verify.sh
```

安装器从 Microsoft 下载 **.NET 10 LTS SDK 10.0.403**，验证固定 SHA-512，安装到 `/workspace/.deepspaceae/dotnet`，然后锁定还原。`global.json` 禁止 SDK 自动滚动；三个 `packages.lock.json` 固定依赖图。SDK 选择依据为 Microsoft [10.0 发布元数据](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)（核查日：2026-10-30；当时通道为 active/LTS，支持至 2028-11-14）。升级必须显式更新版本、校验值和验证记录。

脚本默认把 SDK、CLI、NuGet 缓存和 XDG 状态放在仓库外；保留 `HOME` 不变。可在执行前设置非敏感变量 `DEEPSPACEAE_ENV_ROOT` 更改工具目录。其他平台请从 Microsoft 安装完全相同的 SDK；本轮只验证 Linux x64。

逐项命令（同一开发 shell）：

```bash
source scripts/env.sh
dotnet --version
dotnet restore DeepSpaceAE.sln --locked-mode
dotnet restore src/DeepSpaceAE.GameAdapter/DeepSpaceAE.GameAdapter.csproj --locked-mode
dotnet clean DeepSpaceAE.sln -c Release --verbosity minimal
dotnet build DeepSpaceAE.sln -c Release --no-restore --maxcpucount:2
dotnet test tests/DeepSpaceAE.Tests/DeepSpaceAE.Tests.csproj -c Release --no-build --no-restore \
  --logger 'trx;LogFileName=core-model.trx' --results-directory artifacts/test-results
```

默认解决方案的 Debug/Release 构建都只选择 Core 与 Tests；GameAdapter 在解决方案中可见，但无 `Build.0`，使无游戏引用的环境仍能开发核心。游戏构建需另行执行下面的显式命令；真实游戏运行结果与编译结果分别记录。

依赖网络仅需 `builds.dotnet.microsoft.com`、`api.nuget.org` 及 NuGet 返回的下载端点。查阅指南需要 GitHub。没有应用服务要启动，没有端口要开放，也没有密钥要求。禁止关闭 TLS、签名或校验验证来绕过失败。

## 构建游戏测试包（开发端）

```bash
source scripts/env.sh
GameManagedDir=/workspace/.deepspaceae/game-references/100-REV3 bash scripts/build-game.sh
# 提交/审查当前源代码后打包；文件名带源码提交，拒绝覆盖已有包
python scripts/package-game.py
```

引用由用户授权提供，置于仓库外。无引用时报告 `EAE001`；缺文件时报告 `EAE002`；`Assembly-CSharp.dll` SHA-256 与已核验的用户版本不符时报告 `EAE004`，拒绝静默适配其他版本。原先“尚未实现”的 `EAE003` 门禁已在完成真实绑定后移除，没有用替身 DLL 绕过编译。

开发端引用 `Assembly-CSharp`、`UnityEngine`、`UnityEngine.CoreModule`、`0Harmony`，全部 `Private=false`，不拷贝到输出。PNG 与 manifest 在 `assets/game`，打包使用逐文件白名单，绝不递归打包 bin 或 Managed。NuGet 打包仍禁用；自有 ZIP 安装包是单独审查的流程。文件存在与编译通过仍不证明运行安全。

## 测试含义与后续

测试按实例而非物品类型计数，同 ID 不同状态不合并，同 ID 同状态也不自动堆叠。容量不足/拒收保持源实例；模拟批次逐项报告结果，允许前项成功后项失败。重复请求只在同一个内存会话内重放；同请求 ID 不同载荷报错，不承诺跨重启、持久化、并发或未知结果下的幂等。

这些断言只证明模拟模型，不证明真实游戏已经安全。游戏适配用 `SlotMarker.TryAcceptOnce` 做受限的整实例搬运，不自行删源/造目标；未知结果锁定写入。在独立测试档按 [游戏验收说明](docs/game-test.md) 验证真实行为；本版供电与存储边界见 [0.2 功能](docs/storage-features.md)。

详见 [架构边界](docs/architecture.md)、[初始化验证记录](docs/validation.md)、[内存原型历史验证](docs/network-prototype.md)。仓库与安装包不得包含游戏 DLL、本体、存档或凭据；`.gitignore` 只是第一道防线，提交与打包前仍需审查。测试输出不可作为模组分发；修复完成适当验证后按用户授权推送源码；安装包另行交付，不自动发布创意工坊。
