# DeepSpaceAE

为《深空当铺 / Probably Stolen》加入 AE 风格的存储网络：把容器、硬盘和机器接入线缆，通过终端统一查找和取放物品。

当前版本：**0.14.0-alpha**。目标游戏：**100-Rev12 / Application.version 0.46D / Steam Build 26496118，Windows x64**。

## 功能

- 核心、四向线缆及存储总线；总线作为线缆升级，支持优先级、读写权限、物品和标签筛选。
- 连续滚动终端，内置标签及当前顾客需求筛选；相同物品合并显示数量，每次操作堆叠顶端一件。
- 十盘位驱动器，初始开放两格，每张容量卡增加两格；64格和256格硬盘按物品实际占格计容。
- 单种物品抽屉，基础16件，最多五张容量卡，每张容量乘四；保留每件物品的实际状态。
- 输入总线从机器收回网络，输出总线送入机器；提供物品接口与20升流体接口。
- 网络工程师开局、王尔德的AE工程师升级，以及拨打6322预约次日来访的福鲁伊克斯研究员。
- 默认免电。模组设置可开启实验性耗电规则；自动合成尚未实现。

安装与玩法见[玩家指南](docs/game-test.md)，详细规则见[功能规格](docs/feature-spec.md)。
Alpha阶段建议使用独立存档；扳手拆卸已安装总线仍有待确认的交互问题。

## 构建

需要 `global.json` 指定的.NET SDK；常规构建不需要Unity Editor、Pillow或图像生成工具。
NuGet使用锁定还原，游戏引用来自自行安装的对应版本，不随仓库分发。

```powershell
# 安装固定版本SDK（如尚未安装）
powershell -ExecutionPolicy Bypass -File .\scripts\install-env.ps1
# 构建及只读补丁元数据审计
.\scripts\build-game.ps1 -GameDir '<游戏安装目录>'
# 构建、打包并部署至该安装目录的Mods
.\scripts\deploy-game.ps1 -GameDir '<游戏安装目录>'
```

也可通过环境变量 `DEEPSPACEAE_GAME_DIR`，或被忽略的 `.deepspaceae/local.json` 中的 `gameDir` 字段配置路径。
仅构建时可传入 `-GameManagedDir '<Managed目录>'`。Linux使用 `GameManagedDir='<Managed目录>' bash scripts/build-game.sh`。
先提交源码再构建打包，`scripts/package-game.py` 将源码提交写入包信息。产物位于被忽略的 `artifacts/packages/`。

模型测试入口为 `scripts/verify.ps1` / `scripts/verify.sh`；独立运行时测试宿主不属于发布包。
编译和PE审计只验证静态绑定，不代表游戏交互或保存读取已经验收。

## 开发文档

- [架构](docs/architecture.md)、[游戏API绑定](docs/api-verification.md)
- [功能用例](docs/functional-cases.md)、[版本维护](docs/version-maintenance.md)
- [终端与原生UI](docs/native-ui.md)、[性能边界](docs/performance.md)
- [工坊发布](docs/workshop.md)

## 许可

自有源码 Copyright (c) 2026 Eviadc，采用 **GPL-3.0-or-later**，完整文本见 [LICENSE](LICENSE)。
素材不因源码许可而重新授权：AE2素材及衍生图采用 **CC BY-NC-SA 3.0**，Storage Drawers抽屉图采用 **MIT**；详见[第三方署名](assets/THIRD-PARTY-NOTICES.md)。
本项目与AE2、Minecraft及游戏开发者无隶属或背书关系。
