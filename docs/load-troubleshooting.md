# 加载与故障排查

- 模组未出现：确认目录根存在manifest.xml，两个自有DLL及Textures/Localization完整。
- 审核或权限提示：在原生模组菜单审核并启用；本模组声明reflection-external用于界面绑定，按提示正常重启。
- 兼容性警告：清单匹配Application.version 0.46D；不要只比较主菜单的100-Rev6标签。
- 编译EAE001/EAE002：配置正确的游戏/Managed路径，引用缺失时不能使用替身DLL。
- 编译EAE004：游戏程序集版本变化，先重新核验绑定，不跳过哈希门禁。
- 工坊更新未生效：同ID的本地Mods目录优先，检查实际来源和模组版本。

可使用仓库内scripts/extract-load-log.py从Player.log提取相关片段。日志可能包含机器路径，提交问题前仅保留必要片段并移除个人信息。
不全局静音错误，不修改游戏信任文件，不通过删除真实物品掩盖故障。
