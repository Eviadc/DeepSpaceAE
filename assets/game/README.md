# 游戏测试素材

四张 16×16 RGBA PNG 是 `assets/placeholders` 中原创 SVG 的缩小转换，沿用该目录图标的 CC0-1.0 授权。使用 CairoSVG 2.8.2 转换，画布中心不透明。它们是自有美术，不是游戏资源。

真实程序集的 UISettings 默认 gridSize=16；游戏序列化配置可能改变该值，因此注册时仍显式把自有部件定义为 1×1，而不把素材像素映射用于推断其他物品的占格。PNG 自身不证明运行时显示正确，仍需本地测试。

`manifest.xml` 只列出 Core 和 GameAdapter 两个自有 DLL，Capabilities 为空。不拷贝、打包或重新分发上传的编译引用。
