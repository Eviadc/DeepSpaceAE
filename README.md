# DeepSpaceAE

为《深空当铺 / Probably Stolen》加入 AE 风格的存储网络：把容器、硬盘和机器接入无频道上限的线缆，通过终端统一查找和取放物品。

当前版本：**0.17.1-alpha**。目标游戏：**100-Rev13 / Application.version 0.46D / Steam Build 26525552，Windows x64**。

## 功能

- 核心、四向线缆及存储总线；总线作为线缆升级，支持优先级、读写权限、物品和标签筛选。默认黑名单，空黑名单允许全部，空白名单拒绝全部。
- 原版机器区及扩建机器区可独立组网，核心无需先接入无线连接器；不同物品栏保持各自坐标。
- 2×2 无线连接器按原生料斗的选中／目标方式成对连接，跨任意已拥有物品栏工作，容器窗口关闭后仍有效。
- 放置／吸收总线支持面前一格或整个所在物品栏；占用前格时停止放置，吸收保留 AE 组件。
- 终端、驱动器和接口可转发网络，支持设备直接串联及环路；每个连通网络只允许一个核心。
- 连续滚动终端，内置标签及当前顾客需求筛选；相同物品合并显示数量，每次操作堆叠顶端一件；支持按网络记录的物品堆修改时间排序。
- 终端记忆窗口位置，支持原版平板一样的100%／50%／75%尺寸切换；位置与尺寸随存档保存。
- 十盘位驱动器，初始开放两格，每张容量卡增加两格；64格和256格硬盘按物品实际占格计容。
- 驱动器和各类总线可设置优先级，配置面板直接显示当前数值。
- 抽屉控制器按同栏四向毗邻关系统一访问抽屉，可直接接入 AE 并设置优先级；普通抽屉不传输 AE 信号。
- 激活的 AE 组件自动固定，离线时解除自动部分，保留手动固定。
- 单种物品抽屉，基础16件，最多五张容量卡，每张容量乘四；保留每件物品的实际状态。
- 抽屉可锁定物品种类，取空后仍保留；解锁并清空后可换种类。溢出销毁默认关闭，开启后只销毁新投入的同种超量物品。
- 抽屉支持原生快捷转移键，直接将顶部库存送往主栏、柜台或仓库；大量库存只维护一个可见代表。
- 手动运行输入／输出及面板总线处理所有可移动候选，自动夜间运行保持原有预算。
- 输入总线优先将机器新产出收回网络，拒收或满容量时沿用原版后备库存；输出总线送入机器。接口和存储／输入／输出总线可右键“切换”物品／流体形态，流体接口缓冲20升，物品接口可选自动收纳成交及批发购买的商品。
- 网络工程师开局、王尔德的AE工程师升级，以及拨打6322预约次日来访的福鲁伊克斯研究员。
- 村民商人每次必定提供一至两对无线连接器，拾荒连接器权重提高四倍。
- 开局与升级礼包各含两个无线连接器，不赠送电池；村民商人出售的连接器以双数供货，其他商品批量已缩小，给后列商品留出空间。
- 原版模组设置提供库存读取和抽屉完整布局验证兼容开关，默认保留优化；界面使用公开 UI API，不再声明弃用反射能力。
- 默认免电。模组设置可开启实验性耗电规则；自动合成尚未实现。

安装与玩法见[玩家指南](docs/game-test.md)，详细规则见[功能规格](docs/feature-spec.md)。
Alpha阶段建议使用独立存档；扳手拆卸已安装总线仍有待确认的交互问题。

## 构建

需要 `global.json` 指定的.NET SDK；常规构建不需要Unity Editor、Pillow或图像生成工具。
NuGet使用锁定还原，游戏引用来自自行安装的对应版本，不随仓库分发。

```powershell
# 安装固定版本SDK（如尚未安装）
powershell -ExecutionPolicy Bypass -File .\scripts\install-env.ps1
# 构建、只读补丁审计及游戏原生能力扫描
.\scripts\build-game.ps1 -GameDir '<游戏安装目录>'
# 构建、打包并部署至该安装目录的Mods
.\scripts\deploy-game.ps1 -GameDir '<游戏安装目录>'
```

也可通过环境变量 `DEEPSPACEAE_GAME_DIR`，或被忽略的 `.deepspaceae/local.json` 中的 `gameDir` 字段配置路径。
仅构建时可传入 `-GameManagedDir '<Managed目录>'`。Linux 可直接使用本机 Steam Windows 版游戏的 Managed 程序集进行构建：

```bash
bash scripts/install-env.sh
bash scripts/verify.sh
GameManagedDir='<游戏目录>/Probably Stolen_Data/Managed' bash scripts/build-game.sh
```

引用必须匹配[目标版本](docs/target-build.json)中的程序集哈希。Linux 构建通过不等于已验证 Proton 中的游戏运行。
先提交源码再构建打包，`scripts/package-game.py` 将源码提交写入包信息。产物位于被忽略的 `artifacts/packages/`。

模型测试入口为 `scripts/verify.ps1` / `scripts/verify.sh`；独立运行时测试宿主不属于发布包。
`scripts/runtime-pilot.ps1 -Usability -Launch` 在新建测试存档中检查终端位置与缩放、驱动器与总线优先级、抽屉锁定与溢出销毁、黑白名单、机器区组网、水培产出优先入网、礼包及村民成对供货，包含普通及多页储物区输入／输出，总共17步并验证保存重载。
构建同时运行游戏自带 ModScanner，拒绝弃用能力和待生效发现；不修改扫描规则。编译、PE审计和能力扫描只验证静态绑定及扫描合同，不代表游戏交互或保存读取已经验收。

## 开发文档

- [架构](docs/architecture.md)、[游戏API绑定](docs/api-verification.md)
- [功能用例](docs/functional-cases.md)、[版本维护](docs/version-maintenance.md)
- [终端与原生UI](docs/native-ui.md)、[性能边界](docs/performance.md)
- [工坊发布](docs/workshop.md)

## 许可

自有源码 Copyright (c) 2026 Eviadc，采用 **GPL-3.0-or-later**，完整文本见 [LICENSE](LICENSE)。
素材不因源码许可而重新授权：AE2素材及衍生图采用 **CC BY-NC-SA 3.0**，Storage Drawers抽屉图及AE2 Stuff无线参考素材采用 **MIT**；详见[第三方署名](assets/THIRD-PARTY-NOTICES.md)。
本项目与AE2、Minecraft及游戏开发者无隶属或背书关系。
