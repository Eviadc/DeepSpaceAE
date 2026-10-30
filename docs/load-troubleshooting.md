# 0.3 加载失败与 F8 缺字调查

## 确认的代码缺陷

旧包 DeepSpaceAE-0.3.0-alpha-100-REV3-6d03e55.zip 中，PreserveUnknownProjectionMovePatch 使用 `[HarmonyPatch(typeof(GameItem), nameof(GameItem.Destroy))]`。用户提供的真实 REV3 程序集将该方法声明为 `public abstract void Destroy()`，没有 IL 方法体。编译只检查符号存在，未检查它能否由 Harmony 修补。这是上一轮漏掉的验证。

真实 ModLoader.LoadModAssembly 的顺序是 PatchAll → Init → OnEnable；捕获异常时撤回自己的补丁，并记录 LoadError。注册 OnModItemDirectoryInit 的代码位于 OnEnable，故补丁阶段失败时，7 个 AE 工厂都不会注册。官方故障排查也说明：本地模组加载失败会自动打开 F8。此缺陷足以解释用户报告的“全部组件消失 + F8 自动弹出”，但用户当前 REV5 的第一条实际异常仍须原始日志确认。

0.3.1 删除抽象基类目标，只保留有实际方法体的 GameItemElement.Destroy 保护；没有删除未知移动结果的锁定/保留机制。

## Text 对象、语言键和字体

截图是 TextMeshPro 的 missing-glyph 警告：OfficeCodePro-Regular SDF 没有所需中文字形，被空格替代。`[Text]` 是 Unity 对象名，不能仅凭这个通用名字识别是哪一个画面。

本轮静态查证：TreeNodeRender.InstantiateText 和 CustomUIMachineryStyle 都从 FontLoader 的当前 locale / Noto_Sans 角色选择字体。前者的原生节点名为 Node (Text)。本模组未把 OfficeCodePro 指定给自己的机器界面。旧包若在 PatchAll 已经失败，终端 UI 还未创建。故现有信息优先指向 F8 的显示链路，尚无其模块/现场堆栈以确认每一条 warning 的实际源对象或是否存在转发循环。

已逐一核对自有中文参数：物品 name/shortDescription、MachineryLabel.SetText、PixelWindow.SetTitle 等是显示字符串，不是语言键；官方指南明确允许直接赋纯字符串。未找到把中文传入 GetLocalized 的调用。missing-glyph 与 missing-translation 是不同问题，前者不能证明语言键错误。

0.3.1 仍统一改用官方 Localization/en.csv、zh.csv（中文代码是 zh，不是 zh-CN），93 个稳定 ASCII 键，UTF-8、无表头。UiText.Get 只接受小写 ASCII、数字、下划线，并调用 ModHelper.GetLocalized。静态部件定义只保存键，工厂和游戏钩子在资源就绪后解析；Init/OnEnable 不解析文案。原版物品类别继续用 TypeHelper.GetTypeDisplayName。ModLog 使用实际 manifest，清单 Name 为 ASCII，异常输出经过 ConsoleText 转义。

本修复不修改 Unity/TMP 的全局告警开关，也不声称已经修复游戏控制台字体。Warning 在关闭 Verbose Log 时仍会显示；如果修复加载后 F8 仍刷屏，请使用下方原始文件日志定位。字体消息保留一例和计数即可，优先保留第一条真正的加载异常及调用栈。

## 不依赖 F8 获取第一条异常

1. 完全退出游戏，替换整个模组目录后重启一次。不要混用 0.3.0 的旧 DLL 与新包。
2. 关闭 F8，照常进入模组菜单查看警告图标；记录本次启动的版本和现象。
3. 打开同次启动的原始日志：

    `%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log`

   该目录也有原版 LogWriter 生成的 GameLog_YYYY-MM-DD_HH-mm-ss.txt，Error/Exception 包含调用栈。无需上传游戏 DLL 或存档来读取它。
4. 用记事本搜索 DeepSpaceAE、Harmony、Exception。或者在 Windows PowerShell 运行：

```powershell
$aeLog = Join-Path $env:USERPROFILE 'AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log'
Select-String -LiteralPath $aeLog -Pattern 'DeepSpaceAE|deepspaceae|Harmony|Exception|Error:' -Context 0,15 |
  Out-String -Width 240 |
  Set-Content -Encoding UTF8 (Join-Path ([Environment]::GetFolderPath('Desktop')) 'DeepSpaceAE-load-errors.txt')
```

此命令只读取原文件，写桌面的独立副本；未在云端运行 Windows PowerShell，不声称已在用户电脑验证。用户手动执行即可，不需要额外开发模式。

若已安装 Python，也可使用包内 extract-load-log.py：

```text
python extract-load-log.py "完整的Player.log路径" --output "新的诊断报告路径.txt"
```

提取器只从报告副本省略 OfficeCodePro / [Text] 的同类缺字警告，保留首例与总数；不修改原始日志，不排除其他字体、其他对象、真正 Error/Exception 或缺失翻译提示。输出有上限并明确标记截断，重复字体行不会消耗真正异常的后续上下文。

新包应能看到 ASCII 阶段日志：`initialized`、`Enabled.`、`Assets ready: sprites=43; locales=2.`，进入相应目录初始化后为 `Registration complete: 7 AE item factories.`。缺少哪一阶段可以缩小失败位置；这些消息只证明阶段执行，不证明整套存储功能安全。

## 构建新增门禁与执行结果

scripts/build-game.sh 现在在真实引用编译后运行只读 PE 元数据审计：检查所有 Harmony 目标、非抽象受管方法体、私有字段注入及参数声明。不加载/执行游戏、Unity 或 Harmony；完整 Harmony 运行仍须真实游戏。

- 旧 6d03e55 自有 DLL 对现有真实 REV3 元数据：14 项通过、1 项预期失败（抽象 GameItem.Destroy）。
- 修复后：14 项通过、0 失败。
- 新回归包含抽象/具体方法、93 个双语键与格式占位符；独立日志提取器覆盖 2 万条字体告警夹杂加载异常、其他字体/对象、缺失翻译和输出上限。
- 打包要求元数据报告成功，且其 Adapter SHA-256 与当前 DLL 一致；缺报告、旧报告或失败报告拒绝打包。
- 用户实机加载、F8 字体渲染、REV5 程序集和 UI：未运行/未核验。修复后如仍无组件，请提供同次启动的 Player.log 或提取报告，才能确认真实第一条异常。

依据：官方开发指南固定提交 dc4a00939182d2bf099b075754b79eee42742c20 的“本地化”“用 ModLog 记录日志”“Harmony 补丁”“故障排查”。链接：

- https://github.com/dragonpan2/probably-stolen-modding/blob/dc4a00939182d2bf099b075754b79eee42742c20/ModdingGuide.zh-CN.md#本地化
- https://github.com/dragonpan2/probably-stolen-modding/blob/dc4a00939182d2bf099b075754b79eee42742c20/ModdingGuide.zh-CN.md#故障排查

APIChanges 的开发中接口不作 REV5 已提供的证据；本轮实际元数据仍来自用户合法提供的 REV3 引用。
