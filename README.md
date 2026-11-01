# DeepSpaceAE

《深空当铺：可能是偷的》的存储网络模组工程。目标依次是高效率、可扩展、高兼容：计划在主物品栏摆放核心、连接件和接口形成网络，首版聚焦真实容器的统一索引与安全取放，自动合成后置。

**当前版本 0.9.3-alpha，本地目标 100-Rev6 / Steam Build 26337366。** 在 0.4.1 终端独立镜像、连续滚动、框选和四面总线升级基础上，新增 2×2 六槽硬盘驱动器、1×2 的 64/256 格硬盘，以及当日峰值电池供电/原版电网夜间账单。新阶段完成后统一开展游戏测试，当前模型/编译结果不等于游戏内验收通过。

0.9.3 补上新开局的会话重置及主库存接入，修复首次新档 Loading 一直为真的问题。

独立测试存档已完成八项原生运行时用例，两次均通过，末次包含开局操作提示自动关闭。范围、发现的问题与实际 token 明细见 [精简运行时试验结果](docs/runtime-pilot-results-20261101.md)；鼠标交互、跨日及读档仍待验收。

0.9.2 修订真实冷启动中 AE工程师注册触发惰性 PlayerStore 的时序错误；独立运行时测试插件见 tests/DeepSpaceAE.RuntimeTests，未纳入正式安装包。

0.9.1 完成一轮全组件代码审查，修正旧计划重复结算、稳定身份、原生写入前复核与自动化目标回执，并缩减供电和路由重复遍历。详见[代码审查记录](docs/code-review-0.9.1.md)，未代替实机验收。

0.9 新增 2×2 能源接收器，双击打开三个真实电池槽。与控制器及所有联网接收器共用电量，选择足以覆盖当日峰值的最少电池组合，仅锁定必要电池；数量相同时优先保留原组合。接收器占 1 频道、不额外耗电，不能代替核心或桥接网络。

0.8 增加获取途径：王尔德的“AE工程师”升级费用 200（与葡萄酒酿造同档），赠送基础存储组件；史蒂夫从第 15 天起每天 5% 概率进入顾客候选队列，随机销售全部十三种 AE 组件，不同组件出现概率不同。基础礼包满主栏时保留待发进度；组件基础价格和货品概率统一在 AcquisitionCatalog 中维护。

0.7 新增输入/输出总线，均作为线缆侧面升级。方向按 AE2：输入总线收取机器 output 槽到网络，输出总线向机器 input 槽补料。保留原生收料、锁定、堆叠/拆分回调，支持筛选、手动执行和原版料斗夜间阶段；无明确 input/output 槽角色时提示不支持，不触碰电源/升级槽。

0.6 增加终端“物品 / 液体”切换。总线朝向原版液体容器即可列出其真实成分、体积和空罐；每个来源独立显示，不合并成分不同或温度/状态不同的液体。倒液沿用原版“选中容器→点击目标”，包括混合、滴管、过滤和温度规则；不把液体制造成物品或让终端持有内容。

终端与源容器同时显示并操作真实实例；总线安装在线缆面上，双击对应部位配置，扳手直接拆卸。核心/线缆/总线/终端/驱动器成本为 2/0/1/1/1 kJ。普通电池不足当日断电解锁，未结算峰值次日清零；电流转换器未付款时次日锁定断电。自动合成后置。

[完整功能规格](docs/feature-spec.md) · [全部功能用例及联合测试](docs/functional-cases.md) · [版本升级维护](docs/version-maintenance.md) · [安装与本地验收](docs/game-test.md) · [真实 API 记录](docs/api-verification.md)。历史设计文档保留版本背景；当前规则以 0.9 规格为准。

安装包仅含自有 DLL、89 张 AE2 衍生 PNG 和 1 张史蒂夫头像、双语本地化和说明。素材采用 CC BY-NC-SA 3.0，保留[署名和许可](assets/THIRD-PARTY-NOTICES.md)；固定来源未包含本游戏或 Minecraft 本体。未知移动/扣费结果保持锁定，不自动补偿，不承诺跨容器原子事务。

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
GameManagedDir=/workspace/.deepspaceae/game-references/100-Rev6 bash scripts/build-game.sh
# 提交/审查当前源代码后打包；文件名带源码提交，拒绝覆盖已有包
python scripts/package-game.py
```

引用由用户授权提供，置于仓库外。无引用时报告 `EAE001`；缺文件时报告 `EAE002`；`Assembly-CSharp.dll` SHA-256 与已核验的用户版本不符时报告 `EAE004`，拒绝静默适配其他版本。原先“尚未实现”的 `EAE003` 门禁已在完成真实绑定后移除，没有用替身 DLL 绕过编译。

开发端引用 `Assembly-CSharp`、`UnityEngine`、`UnityEngine.CoreModule`、`UnityEngine.InputLegacyModule`、`0Harmony`，全部 `Private=false`，不拷贝到输出。PNG 与 manifest 在 `assets/game`，打包使用逐文件白名单，绝不递归打包 bin 或 Managed。NuGet 打包仍禁用；自有 ZIP 安装包是单独审查的流程。文件存在与编译通过仍不证明运行安全。

## 测试含义与后续

测试按实例而非物品类型计数，同 ID 不同状态不合并，同 ID 同状态也不自动堆叠。容量不足/拒收保持源实例；模拟批次逐项报告结果，允许前项成功后项失败。重复请求只在同一个内存会话内重放；同请求 ID 不同载荷报错，不承诺跨重启、持久化、并发或未知结果下的幂等。

这些断言只证明模拟模型，不证明真实游戏已经安全。游戏适配用 `SlotMarker.TryAcceptOnce` 做受限的整实例搬运，不自行删源/造目标；未知结果锁定写入。在独立测试档按 [游戏验收说明](docs/game-test.md) 验证真实行为；本版供电与存储边界见 [0.2 功能](docs/storage-features.md)。

详见 [架构边界](docs/architecture.md)、[初始化验证记录](docs/validation.md)、[内存原型历史验证](docs/network-prototype.md)。仓库与安装包不得包含游戏 DLL、本体、存档或凭据；`.gitignore` 只是第一道防线，提交与打包前仍需审查。测试输出不可作为模组分发；修复完成适当验证后按用户授权推送源码；安装包另行交付，不自动发布创意工坊。

## Windows 本地开发

无需 WSL、Unity Editor 或完整 Visual Studio。SDK 固定为 `global.json` 中的 10.0.403；以下脚本在 Windows PowerShell 5.1 / PowerShell 7 中运行，自动选择 `%LOCALAPPDATA%\DeepSpaceAE\dotnet`，不替换系统已有的 .NET 8。NuGet 依赖使用锁定还原。

```powershell
# 首次安装固定 SDK（微软官方来源及 SHA-512 校验）
powershell -ExecutionPolicy Bypass -File .\scripts\install-env.ps1
# 核心构建和模型测试
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
# 游戏适配器构建 + 只读补丁元数据审计
powershell -ExecutionPolicy Bypass -File .\scripts\build-game.ps1
# 一条命令：构建 → 审计 → 白名单 ZIP → 备份旧模组 → 复制到 Mods
powershell -ExecutionPolicy Bypass -File .\scripts\deploy-game.ps1
```

默认游戏目录为 `G:\SteamLibrary\steamapps\common\Probably Stolen`，可用 `-GameDir` 指定其他安装目录；仅构建时也可用 `-GameManagedDir` 明确指定引用目录。直接读取游戏 Managed 中的四个 DLL，不复制进仓库。运行游戏时部署会停止并提示先退出。

Windows 当前目标为本地 **100-Rev6 / Steam Build 26337366**，主程序集精确哈希门禁不放宽。历史 REV3 验证记录保留为历史证据，不能作为 Rev6 运行验证。打包沿用干净提交、内嵌源码提交、素材白名单和补丁审计检查；先提交源码再构建打包。相同提交及相同内容的已有 ZIP 可复用，内容不同时拒绝覆盖。

旧模组完整备份到 `artifacts/mod-backups/<时间>/DeepSpaceAE`，部署只复制当前安装包中的文件到 `Mods\DeepSpaceAE`，不删除其他模组或旧目录里的自定义文件。独立打包命令为 `scripts/package-game.ps1`。编译、模型测试和元数据审计不等于真实游戏加载、交互或存档验证。

2026-10-31 本机验证：固定 SDK 10.0.403、77 项模型测试、Rev6 编译（0 警告 / 0 错误）及 14 项补丁元数据审计均通过；未启动游戏或验证真实交互。

0.4.1 终端采用独立原版 UI 节点镜像，命中仍指向单一真实物品；已取消借用源容器的物品渲染。原版滚动条和可见区域渲染保留。双窗口同时显示/交互尚待实机验收，详见 [双视图实现](docs/native-ui.md#035-双视图单一真实物品2026-11-01)。

## 0.4.1 本地操作更新

- 筛选栏使用固定九列网格，随容量卡增加行数，无滚动条。
- 数量排序按当前结果中同类物品总量排序，实例和状态仍独立。
- 终端使用普通库存背景；线缆整个占格可命中。
- 将真实存储总线拖到线缆上安装，最多四个，每个占用并阻断一面；优先未连接面。
- 双击总线所在部位配置，用扳手对准该部位卸下；线缆本身不弹出界面，整根线缆拿起旋转。四面装满后无外部线缆连接。
- 旧独立总线不自动迁移，请手动拖到线缆上重新安装。请在测试档验证保存/重载。

83 项模型测试与 23 项补丁元数据审计通过；真实游戏交互及存档验收仍待完成。完整边界见 docs/native-ui.md 的 0.4.1 段。

## 0.4.1 操作

线缆不再提供独立窗口：双击对应侧面的总线模型打开配置，中心不弹窗；扳手直接对准对应部位卸下。外观按现有 AE2 模型与 UV 生成四向薄面板及内部接头，移除旧箭头。

终端从空白处拖框可选中当前可见物品，显示蓝色占格，再从选中物品拖出。滚动/筛选/刷新会清空旧选择；批量存取逐项核验，不保证整批原子成功。85 项模型/贴图测试与 29 项补丁审计通过，实际游戏输入及存档仍须本地验收。构建现在还引用游戏 Managed 中的 UnityEngine.InputLegacyModule.dll，不随包分发。
