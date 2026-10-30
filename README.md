# DeepSpaceAE

《深空当铺：可能是偷的》的存储网络模组工程。目标依次是高效率、可扩展、高兼容：计划在主物品栏摆放核心、连接件和接口形成网络，首版聚焦真实容器的统一索引与安全取放，自动合成后置。

**当前交付为可运行的存储网络内存原型，不是可玩的游戏模组。真实游戏 API 尚未核验。** 已有核心、管线、存储总线、终端的逻辑拓扑、可配置频道、终端只读索引与测试宿主中的模拟取放，以及原创 SVG 占位图标。尚无游戏入口、manifest、Unity 对象替身、真实库存放置或自动合成；不宣称游戏兼容、存档安全或跨容器原子事务。

## 工程

一个 `DeepSpaceAE.sln`，三个项目：

| 项目 | 目标框架 | 本轮边界 |
| --- | --- | --- |
| `src/DeepSpaceAE.Core` | `netstandard2.1` | 不可变模型、显式拓扑、频道分配、终端按实例的统一索引；无游戏依赖 |
| `src/DeepSpaceAE.GameAdapter` | `netstandard2.1` | 仅依赖检查和边界；默认不参与编译，显式编译会报出依赖/未实现原因 |
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

默认解决方案的 Debug/Release 构建都只选择 Core 与 Tests；GameAdapter 在解决方案中可见，但无 `Build.0`。所有项目可还原，GameAdapter 的还原不代表适配完成。必需检查为锁定还原、核心/测试项目干净构建和非零测试运行；真实游戏检查是下一阶段的必需前提，本轮未运行。

依赖网络仅需 `builds.dotnet.microsoft.com`、`api.nuget.org` 及 NuGet 返回的下载端点。查阅指南需要 GitHub。没有应用服务要启动，没有端口要开放，也没有密钥要求。禁止关闭 TLS、签名或校验验证来绕过失败。

## 适配层的明确失败

```bash
source scripts/env.sh
dotnet build src/DeepSpaceAE.GameAdapter/DeepSpaceAE.GameAdapter.csproj -c Release --no-restore
```

无引用时应以非零退出并报告 `EAE001`（`GameManagedDir` 未设置）。显式指向空目录则报告 `EAE002`（缺少真实 DLL）。未来只允许使用合法可用、与目标版本匹配的真实程序集；本轮不获取这些文件。即使文件齐全，也会报告 `EAE003`：真实绑定尚未实现/核验，不生成空壳 DLL 冒充成功。不应通过添加假游戏接口或跳过检查让它变绿。

未来核验后才可决定实际引用、加载入口及发布清单。目前候选引用沿用指南中的 `Assembly-CSharp`、`UnityEngine`、`UnityEngine.CoreModule`、`0Harmony`，全部 `Private=false`，不拷贝到输出。文件存在本身不证明版本匹配或 API 安全。

## 测试含义与后续

测试按实例而非物品类型计数，同 ID 不同状态不合并，同 ID 同状态也不自动堆叠。容量不足/拒收保持源实例；模拟批次逐项报告结果，允许前项成功后项失败。重复请求只在同一个内存会话内重放；同请求 ID 不同载荷报错，不承诺跨重启、持久化、并发或未知结果下的幂等。

这些断言只证明模拟模型，不证明真实游戏已经安全。实际搬运不能以“先删源、再造目标”代替原生入口。下一步优先完成 [真实 API 待验证清单](docs/api-verification.md)，再实现最小只读观测和受约束的原生搬运试验。核心算法可独立推进，但不要立即扩展成完整 AE。

详见 [架构边界](docs/architecture.md)、[初始化验证记录](docs/validation.md)、[网络原型与本轮验证](docs/network-prototype.md)。仓库和构建输出不得包含游戏 DLL、本体、存档或凭据；`.gitignore` 只是第一道防线，提交前仍需审查。三个项目均禁用 NuGet 打包；当前不生成或发布模组包，测试输出也不可作为模组分发。
