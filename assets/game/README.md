# 游戏测试素材

20 张 PNG 均为原创程序化像素素材，源几何位于 scripts/generate-game-assets.py；CC0-1.0。不读取、复制或派生游戏贴图。运行 python3 scripts/generate-game-assets.py 可重复渲染，--check 只校验，依赖仅为 Python 标准库。

- core.png 为 48×48，对应当前默认 16 像素格下的 3×3 核心；注册时显式设置九格占用。
- pipeline.png 是生成器预览。pipeline-0.png 到 pipeline-15.png 是 16 张 16×16 连接贴图，位 0/1/2/3 对应本地北/东/南/西，中心与边缘按端口绘制。微透明背景保留整格点击区域。
- item-storage-bus.png 是 16×16 朝本地东侧的箭头；原生旋转与翻转同时应用于图像和端口。
- terminal.png 为 16×16。

连接贴图沿用固定 1×1 占格，只换 Sprite，不从贴图重新推导形状。运行时预制件和 UI 缩放仍需真实游戏复测，未验证其他 gridSize 的素材尺寸适配。

manifest.xml 仅列两个自有 DLL，能力为空；不捆绑上传的游戏编译引用。
