# DeepSpaceAE 0.5.0-alpha · 安装和联合验收

编译目标为本机真实安装的 100-Rev6 / Steam Build 26337366，Windows x64。新阶段游戏行为、存档往返及夜间现金扣费仍须联合测试。93 项模型/贴图测试及 31 项 PE 补丁审计不是 Unity/Harmony 实际运行证据。

## 安装和本地开发

退出游戏，将构建 ZIP 内 DeepSpaceAE 文件夹完整放入游戏 Mods，替换旧模组，避免混用版本。正常启用程序集模组，不需要 F8/F9、Unity Editor 或新的系统级依赖。已固定本地 SDK，Pillow 只用于可选素材重绘，普通构建无需安装它。

Windows PowerShell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-game.ps1
# 已提交且干净的版本：编译 / 审计 / 打包 / 备份旧版 / 部署
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/deploy-game.ps1
```

默认游戏目录 G:\SteamLibrary\steamapps\common\Probably Stolen，部署位置为其 Mods\DeepSpaceAE。部署脚本发现游戏运行会停止，不强制结束游戏。旧版备份位于仓库 artifacts/mod-backups，安装包位于 artifacts/packages。

包内仅两个自有 DLL、67 张授权 PNG、2 CSV、manifest、说明、来源/许可、日志提取脚本及构建记录，不含游戏引用、测试宿主、存档或凭据。源码完整说明见 docs/feature-spec.md、docs/functional-cases.md、docs/version-maintenance.md；包内另附“功能规格.md”“功能用例.md”“版本维护.md”。

## 本轮优先测试

1. 主栏摆核心→线缆→终端，驱动器贴线缆。核心 3×3，驱动器 2×2，其他网络部件 1×1。双击驱动器出现六个 DNA 风格硬盘槽，仅可放本模组小/大盘；单击保留原生拖拽。
2. 小盘 64 格、大盘 256 格，均占 1×2。通过终端存入不同形状物品：五格物品只计五格；小盘 60/64 时五格拒收、四格可入。取出释放容量；拆盘换另一驱动器，内容应保持。
3. 普通电池足量插入后锁定。增添设备使峰值超出电量，应立即断电并解锁；减少设备不能清除当天峰值，换足量新电池恢复。一直不足到睡觉，则次日未结算峰值清零，不追扣。
4. 核心放开启的原版 current_converter，记录现金和电力服务账单。睡觉后只按原版总账单支付一次；禁用电力服务使之未付，则次日锁定源并断电；重新启用服务在下一夜实际付款后恢复。保持其他原版机器的正常合并收费。
5. 存档重载，核验六盘内容、物品状态、总线方向/卡片/筛选、峰值和电源锁。原有核心/总线子图不改，但必须实测往返。
6. 一并回归终端双视图、双击/右键/拖动/框选、取消/滚动；总线四面直接命中、旋转后扳手拆指定面；筛选栏整行扩展、无滚动条。

全量步骤、预期和待填结果见“功能用例.md”。终端整实例取放，不拆分/自动堆叠，群拖逐项处理，不承诺全成全败。盘禁止嵌套盘/驱动器。源容器与终端同时可见，终端镜像不拥有真实物品。

## 异常与卸载

测试前自行备份存档。未知移动或扣费结果出现时停止，保留操作步骤、物品 ID、当日/预算及 Player.log，不反复点击补偿。DebitStarted/Fault 不自动重放，也不直接解锁来掩盖异常。加载问题按“加载诊断.md”提取日志，不全局静音错误。

卸载前先通过终端取回全部盘内物品，再拆盘、电源、总线和升级，移除所有模组物品；勿直接删模组导致原生保存图失去物品工厂。
