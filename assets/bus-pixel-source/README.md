# 总线物品图标来源

正式源为scripts/bus_pixel_art.py，经generate-game-assets.py生成三个16×16独立图标。可见长边14px，alpha为0/255。
使用AE2固定提交629d953f987f37a768fbf96551c97d3dfd07b7ee的各自物品模型与纹理，保留顶点、层厚、UV及GUI角度[30,225,0]。
存储/输入/输出分别以(0.25,0)、(0,0.25)、(0,-0.25)像素位移对齐最终网格，无分轴拉伸。材料改配暖灰金属、深紫及蓝灰色组。安装态采用单独四向资源。
Source: https://github.com/AppliedEnergistics/Applied-Energistics-2
模型署名Sea_Kerman；纹理Copyright (c) 2020 Ridanisaurus Rid，Copyright (c) 2013–2020 AlgorithmX2 and AE2 contributors。
DeepSpaceAE投影及配色修改采用CC BY-NC-SA 3.0。逐文件来源见assets/ae2-source/sources.json，完整署名见assets/THIRD-PARTY-NOTICES.md。
