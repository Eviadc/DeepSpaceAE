# 设备像素素材来源

正式源为scripts/device_pixel_art.py，经generate-game-assets.py生成控制器、驱动器和两种接口的20张工作/无电/动画图。
直接在最终32×32或48×48画布绘制。控制器主体42×42、透明边3px；驱动器和接口主体28×28、边2px；alpha为0/255。
控制器保留AE2原始16×16单元的电路身份，背景间距适配整数像素网格。驱动器两列五行；无电态只改变显示区，12帧只改变电路颜色。
参考AE2固定提交629d953f987f37a768fbf96551c97d3dfd07b7ee；本游戏机器的轮廓、色块和透明边为风格参考，原版图像不分发。
Source: https://github.com/AppliedEnergistics/Applied-Energistics-2
Copyright (c) 2020 Ridanisaurus Rid；Copyright (c) 2013–2020 AlgorithmX2 and AE2 contributors。
DeepSpaceAE像素重排与配色修改采用CC BY-NC-SA 3.0；逐文件来源及署名见assets/ae2-source/sources.json和assets/THIRD-PARTY-NOTICES.md。

无线连接器参考 AE2 Stuff 固定提交 5ed912ea686d98d1a0e5c25a9f02f142cd6f8163 的绿色无线波纹和紫色框架。依照相同最终分辨率流程直接绘制 32×32 PNG、28×28 主体、2px 透明边，工作／离线态只改变发光面。来源与 MIT 许可见 ../ae2stuff-source/。

抽屉控制器采用同一设备金属外框与四格抽屉面板，直接在32×32画布绘制，透明边2px；生成入口为generate-game-assets.py，提供工作与离线两态。
