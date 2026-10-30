# 0.1.0-alpha 游戏测试包构建记录

日期：2026-10-30（Asia/Shanghai）。源码提交：`c5d6faac73b5667d27d87139d093e2edee839d78`。本记录为之后的纯文档提交，构建和打包后没有更改源码、项目、素材或打包输入。

## 实际执行

工作目录 `/workspace/DeepSpaceAE`，Debian 13 Linux x64，固定 .NET SDK 10.0.403：

```bash
bash scripts/verify.sh
GameManagedDir=/workspace/.deepspaceae/game-references/100-REV3 bash scripts/build-game.sh
bash scripts/demo.sh --smoke
python scripts/package-game.py
```

捕获验证日志使用 `set -euo pipefail`，保留原命令退出状态。上传引用的来源、哈希与静态核验情况在 [API 记录](api-verification.md)，均位于仓库外，不进入 Git。

| 检查 | 实际结果 |
| --- | --- |
| 默认 Core/Tests 锁定还原、Release 干净构建 | 通过，0 警告、0 错误 |
| 核心/模型/素材 xUnit 测试 | **42 执行、42 通过、0 失败、0 跳过**；含新增 5 个物理拓扑投影用例 |
| GameAdapter 显式锁定还原、Release 干净构建 | 通过，使用真实用户引用，0 警告、0 错误；不是假游戏接口编译 |
| 内存终端 smoke | 通过，退出 0；不等同于游戏运行 |
| 四张 PNG | 通过，16×16 RGBA、中心不透明，由原创 SVG 转换 |
| 安装包白名单/清单 | 通过，仅 2 个自有 DLL、4 张 PNG、manifest、说明、构建元数据，共 9 个文件；manifest 的程序集列表与包内 DLL 精确一致 |
| 构建源码身份 | 通过，两个 DLL 均包含上述源码提交的 informational version；工作区干净后打包 |
| ZIP 完整性与逐文件 SHA-256 | 通过，CRC 无损坏；各文件与 build-info.json 记录一致 |
| 原生加载器的 IMod 名称预检查 | 通过，适配 DLL 包含 `IMod` 元数据；真实接口实现由编译器对用户 Assembly-CSharp 校验 |
| 引用/输出审查 | 通过，适配 bin 和安装 ZIP 无游戏 DLL、Unity/Harmony/Steam DLL、测试依赖或存档 |
| 游戏扫描器、Unity/Mono 实际加载、F9/终端 UI | **未运行**，待用户本地实测 |
| 真实物品搬运、原生回调、存档往返、停用/卸载、其他模组兼容 | **未运行**；默认只读，实验写入关闭，不保证安全或原子性 |
| 远程推送、工坊发布、云环境快照发布、新任务恢复 | **未执行/未验证** |

初次使用 ImageMagick 转换 SVG 时缺少 rsvg-convert，已改用 CairoSVG 2.8.2 完成格式转换；没有下载游戏贴图或关闭任何验证。PNG 已作为原创素材跟踪，后续构建不需要额外图像工具。

## 交付归档

- 路径：`artifacts/packages/DeepSpaceAE-0.1.0-alpha-100-REV3-c5d6faa.zip`
- 大小：30,193 字节
- SHA-256：`99fd053c1b737d534c5e1cf0f5365b623939f3e85c1ab07647e313b70082ea24`
- 外部校验文件：同路径 `.zip.sha256`
- 包内源码信息：`DeepSpaceAE/build-info.json`
- 用户步骤：[安装与游戏内测试](game-test.md)，同时包含在 ZIP 中。

后续排查补充：仓库中的 `docs/game-test.md` 已补充此真实版本启用 F8/F9 所需的根目录 `dev.txt` 标记，以及无窗口时的分层排查步骤；以上既有 ZIP 未重新构建或修改，因此包内说明仍是打包时的版本，不包含这次补充。

本机证据：`artifacts/validation/game-core-tests.log`、`game-build.log`、`game-prototype-smoke.log` 及当前 `artifacts/test-results/core-model.trx`。这些生成文件被忽略；后续任务需重新执行，不使用旧 TRX 证明新结果。

## 配置状态

云环境草稿的 `start_skill` 已保存真实引用构建方式与包白名单，移除过时的 EAE003/“尚无适配”说明。`install_script` 未变，因为 SDK/依赖安装没有新增运行需求，PNG 已提交。保存草稿不执行或发布；如需后续任务使用更新配置，应在环境设置审阅、保存并发布云环境。未验证新任务能恢复本地源码或用户引用。
