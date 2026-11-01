# 游戏素材

89 张 AE2 衍生 PNG：核心、线缆、总线、终端与 16 个线缆连接掩码的在线/熄灭版本，加容量卡、标签卡、扳手。其中 12 张为核心灯光帧。新增输入/输出总线各 10 张（图标在线/离线及四向模型在线/离线）。另有四面存储总线 8 张、驱动器在线/熄灭 2 张、大小盘 2 张。核心及其动画帧 48×48，驱动器 32×32，硬盘 16×32，其余 16×16。

素材采自 AE2 固定提交，原始输入、模型和逐文件哈希在 assets/ae2-source；来源及 CC BY-NC-SA 3.0 署名见 assets/THIRD-PARTY-NOTICES.md。这些 PNG 不适用早期原创占位素材的 CC0 声明。没有本游戏或 Minecraft 本体文件。

可选重绘：Python 3 + Pillow 12.3.0，运行 python3 scripts/generate-game-assets.py；--check 验证已提交输出与输入哈希。标准构建/测试/打包使用已提交 PNG，不需要 Pillow。输出 PNG 使用自有标准库编码器，不受 Pillow 压缩器差异影响。

0.8 另加 steve-portrait.png（256×384，RGBA），总计 88 PNG。它由内置 imagegen 生成、保留透明度缩放，来源/完整提示词和可复现缩放步骤见 assets/steve-source/README.md。该 NPC 头像与 87 张 AE2 衍生组件许可分开，不声称源自 AE2。

0.9 新增 energy-receiver.png 和 energy-receiver-off.png，32×32，以已授权控制器背景与三个电池符号组合；生成过程见 scripts/generate-game-assets.py。当前合计 90 PNG（89 AE2 衍生 + 1 独立史蒂夫头像）。
# 0.10 素材更新

当前91 PNG：89张AE2原图/衍生图、1张MIT Storage Drawers抽屉、1张独立头像。驱动器/硬盘/接收器/输入输出总线已改用对应AE2源素材，抽屉固定来源见 ../drawers-source/sources.json。许可及改动见 ../THIRD-PARTY-NOTICES.md。

Windows生成贴图可用忽略目录内的工具环境：`python -m venv artifacts/artwork-env`，随后运行 `artifacts/artwork-env/Scripts/python.exe -m pip install Pillow==12.3.0` 及 `artifacts/artwork-env/Scripts/python.exe scripts/generate-game-assets.py`。普通模组构建使用已入库PNG，不需要Pillow。
