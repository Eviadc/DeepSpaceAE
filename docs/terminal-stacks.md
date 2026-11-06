# 终端分组与筛选

终端内置标签搜索及当前顾客需求筛选，没有标签卡槽。先筛选真实实例，再按完整规范化状态分组。
比较原始/有效标签、名称描述、外观、特征条件、形状及锁定状态；位置、朝向、实例ID与数量不作为同种判据。
仅忽略BOUGHT_PRICE_TAG的有无及值；未启用且值全为默认的标签视为缺失，标签身份按字典键。启用空标签或禁用非零标签仍区分。比较不修改真实物品；IO精确样品仍使用完整状态。
空原生容器可合并并监听直属库存；有内容或未知扩展状态独立显示。液体始终逐来源显示。

右下角显示成员unitCount总和，命中指向顶端真实物品。拖动、框选和三种原生快捷转移每堆至多一件；数量排序按筛选后的每堆总数。
终端及物品接口仅接收IsItemEffectivelyOwned确认的物品，实际写入在原生移除/拆分前检查一次当前拥有权。
取出或状态变化后下一成员成为顶端；不克隆GameItem、不改变实际归属或unitCount、不借用源渲染。

实现入口：ItemStackState、TerminalStacks、NativeStackBadge、TerminalQuickTransfer、VirtualInventory、NativeDrag。验收用例：TERM-STACK、TERM-OWN、DISPLAY。
