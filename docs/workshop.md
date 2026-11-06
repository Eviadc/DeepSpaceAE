# 创意工坊发布

官方流程：https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md#用-steamcmd-发布

1. 提交源码，执行build-game.ps1和package-game.ps1，确保真实引用编译和PE审计成功。
2. 执行scripts/prepare-workshop.ps1，生成独立的白名单内容目录和被忽略的.deepspaceae/workshop-item.local.vdf。默认私密，封面复用控制器图标。
3. 使用拥有游戏的账号在SteamCMD交互登录，执行workshop_build_item并传入VDF绝对路径。密码和Steam Guard不写入脚本或仓库。
4. 完成首次工坊协议确认，检查上传结果后使用-Visibility 0重新准备并更新同一条目。
5. 保留SteamCMD写回的publishedfileid；脚本自动沿用已有ID，不能为更新重置为0。

AppID为4348910。不要上传整个仓库、Mods总目录、测试宿主或本机配置。
工坊描述源文件为workshop-description.txt，发布内容含源码入口、源码提交和分立素材许可。
检查订阅版时将相同ID的本地Mods副本移出并备份，以免游戏继续加载本地版。
