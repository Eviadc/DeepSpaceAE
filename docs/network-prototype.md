# 存储网络原型

此原型回应“使用占位素材构建 AE 核心、管线、物品存储总线、终端，保留频道，并让终端访问接入物品”。**可运行的是逻辑网络与内存终端，尚不能安装到游戏。** 没有真实游戏程序集，本轮不更改 GameAdapter 门禁，不猜测正式游戏 API，也不访问 Steam 或本地电脑。

## 四类部件与占位素材

| 部件 | 逻辑职责 | 频道规则 | 占位图 |
| --- | --- | --- | --- |
| AE 核心 | 为连通分量提供单一网络身份 | 默认承载 32 | [core.svg](../assets/placeholders/core.svg) |
| 管线 | 传递连接与频道 | 每段默认承载 8，共享路径计数 | [pipeline.svg](../assets/placeholders/pipeline.svg) |
| 物品存储总线 | 绑定一个模型容器 ID，不拥有或复制物品 | 在线时消耗 1 | [item-storage-bus.svg](../assets/placeholders/item-storage-bus.svg) |
| 终端 | 浏览同一核心下所有在线总线的容器索引 | 在线时消耗 1 | [terminal.svg](../assets/placeholders/terminal.svg) |

64×64 SVG 是原创设计占位，画布大小不表示库存占格。素材清单 `assets/placeholders/parts.json` 尚不是游戏物品注册文件；没有声称 SVG 可直接被目标游戏当作 Sprite 载入。CLI 以文字显示部件，可导出自包含 HTML 把这些占位图与当前网络状态、终端物品目录一起呈现。HTML 是离线只读快照，不伪装成可交互游戏 UI；没有运行后台服务，也没有第二份 JavaScript 算法。

## 连通与频道

采用显式、无向的节点连接，不把游戏主库存当作已知二维网格。原型中终端/总线是单连接叶子，不充当管线；这是可替换的模型规则，不能由此推断正式游戏端口或占格。

每个连通分量恰有一个核心才能工作：无核心则 `NoCore`；多个核心则 `ConflictingCores`，全部设备停止访问。不会自动合并多核心网络，也不默默选择其中一个。

频道默认开启。终端/总线的每个已分配路径消耗核心以及沿途每段管线各 1 频道；核心和管线本身不额外消耗设备频道。核心容量 32、管线容量 8 为本原型默认策略，不是游戏原生能力，也不声称完整复刻其他 AE 模组的面/线缆规则。容量可为 0，可关闭限制，但连通和单核心条件始终保留。

分配顺序：终端优先，其次总线，同类按节点 ID 的 ordinal 顺序。对每台设备执行有剩余容量的 BFS，稳定选择最短可用路径，允许绕过已满管线；用量不超过开启限制时的容量。设备无可用路径时 `ChannelExhausted`。断线、重连或修改设置后重新计算。

这是确定性的贪心方案，不保证全局最大接入量、公平性或旧路径稳定，也没有对标正式 AE 的最优路由。一次计算约 O(D×(V+E))，D 为设备数；当前按终端请求刷新，并非每帧扫描。真实事件、失效时机和性能基准核验后，再做增量缓存/调度，避免用缓存掩盖断线后的过期访问。

## 终端访问

`TerminalCatalog` 接受拓扑与调用方提供的容器快照，只包含与终端同核心、已获频道的总线所绑定的容器。两个总线绑定同一容器时只索引一次，但两台设备仍各占频道。未接入/其他网络的容器不显示；缺失容器单独列为 `MissingContainerIds`，不补造数据；观测到重复实例仍由 `SnapshotIndex` 拒绝。

CLI 中 `take <实例ID前缀>` 将指定实例从当前可见容器移到玩家模拟背包；`put <实例ID前缀> <总线ID>` 存入指定在线总线接入的容器。每次操作重新读取拓扑和容器，不使用上一页的目录授权写入。相同类型但状态不同的电池各有独立实例 ID，不合并、不重建。容量不足或拒收保持源物品不变；断线/离线时拒绝写入。

这些写入仅操作 Tests 中的内存字典，没有生产游戏容器接口或原生搬运绑定。不覆盖并发、事件回调、写入后抛异常或保存中断，不保证跨容器原子性。真实安全验证仍见 [API 清单](api-verification.md)。

## 复现

```bash
cd /workspace/DeepSpaceAE
bash scripts/install-env.sh
bash scripts/verify.sh
bash scripts/demo.sh --smoke
bash scripts/demo.sh
bash scripts/demo.sh --report artifacts/network-prototype.html
```

交互命令：

```text
help
status
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
export artifacts/after-move.html
quit
```

`channels on 2 32` 表示管线上限 2、核心上限 32。修改设置立即重算；`channels off` 仅取消容量限制。实例前缀至少 4 位且必须唯一。启动时每次创建同一份确定性样例：总线 A 两节不同电量电池，总线 B 一件矿石，另有未接入的隐藏容器。退出即丢弃状态，不读写存档。

报告父目录需已存在（运行验证后 `artifacts` 已创建），目标文件不能已存在，避免无意覆盖。命令行 `--report` 导出初始样例；交互 `export` 导出操作后的当前状态。两个入口都从同一个 C# `TerminalCatalog` 读取数据，图标内嵌无需网络；物品文本转义后输出。

保留原来的三个项目和固定 SDK，无新 NuGet 依赖。Tests 使用自己的控制台入口作为开发宿主，`dotnet test` 继续由 Test SDK/xUnit 运行；不会将测试宿主作为模组分发。
