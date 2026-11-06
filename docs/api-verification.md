# 游戏API绑定

目标：Windows x64，100-Rev12，Application.version `0.46D`，Steam Build `26496118`。
Assembly-CSharp.dll SHA-256：`d9abc029f6b390619c31175df5735901aec6dc071c35c50ce5b3fdf92e3ede46`。
构建对该程序集实施版本门禁；更换游戏版本需重新核验，不能仅修改哈希。

| 功能 | 绑定与约束 |
| --- | --- |
| 主库存 | EmporiumEntry.Instance.invElement；PlayerStore.GetStoreInventory不是屏幕主栏 |
| 物品形状 | GridShape.GetLocal/ForwardTransform，包含旋转和翻转 |
| 搬运 | 原生找槽、MayRemove/MaxNumRemove、SlotMarker.TryAcceptOnce；使用返回数量 |
| 拥有权 | 终端和物品接口使用IsItemEffectivelyOwned，不能以交易准入代替 |
| 界面 | CustomUIManager、MachineryPanel/Row/Intake/Stack、原生控件与库存背景 |
| 机器槽 | 从contentWindow.childElement的真实UI树读取role和直属库存；排除NO_HOPPER及辅助槽 |
| 液体 | WaterHelper微升成分、TransferLiquid(source,target)及原生Target回调 |
| 电源 | PowerHelper.CanDrawPowerSource/DrawPowerSource；扣电bool为准 |
| 电网 | CurrentConverterHelper、服务费用与RunStats.OnServicePaid实际付款事件 |
| 自动化 | HopperHelper.RunWindow之后执行有界批次 |
| 动画 | SpriteAnimationHelper及原生精灵解析；不每帧扫描网络 |
| 开局/获取 | StartingScenario、ModData、NetworkUpgrade及当前世界注册生命周期 |
| 商人 | PaperBook/PhoneUI/通讯录及原生来访队列、供货和交易流程 |

Harmony目标必须是具体方法；不修补抽象GameItem.Destroy。打包前对当前DLL运行46项只读PE目标审计。
`reflection-external`声明用于已核验的界面绑定；不修改游戏信任设置。引用全部Private=false，不复制到发布目录。

游戏指南与APIChanges可辅助定位，但开发中API不自动视为已发布接口。
编译与PE审计不执行Unity、Harmony、物品副作用或保存读取；这些范围须按[功能用例](functional-cases.md)分别验收。

## 100-Rev12 核验

对本地合法安装的 Build 26496118 读取 PE 元数据及反编译核对；PlayerStore.VERSION 为 100-Rev12，PlayerSettings.bundleVersion 仍为 0.46D。SlotMarker、WaterHelper、PowerHelper、RNG 和 StoreClientListSupplier 与已有参考实现一致（忽略反编译工具提示）。PlayerStore 的原生摆货入口保持不变；HopperHelper 新增颜色、出口配置和溢出路由，但 RunWindow 入口保持；ModHelper 新增数据目录写入保护，本模组使用自身 ModData，不访问其他模组目录。

完整游戏引用构建通过（零警告、零错误），最终46项补丁元数据审计全部通过。此次未启动游戏或运行模型测试，静态核验不代表实机验收。
