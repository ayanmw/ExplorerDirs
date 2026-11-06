# 拖动复制与快捷方式：能力契约修复

2026-11-06，基于 6720327f。用户反馈普通 BAT 文件 Ctrl 拖动不能复制、创建快捷方式失败。

## 已确认的缺口

产品仍使用 Windows ExplorerBrowser/DefView；普通目录经本项目的 Shell namespace 投影提供文件夹大小和排序。复用原生视图并不自动保证投影完整履行 IShellFolder 合同。

`StaticShellFolderAdapter.cpp` 已经将文件项 `IDataObject` 和背景 `IDropTarget` 委托给真实文件系统目录，没有必要自行实现复制/移动/快捷方式。但 `GetAttributesOf(0)` 只返回 FOLDER/HASSUBFOLDER，丢失 DROPTARGET 等能力，并忽略调用者的请求掩码。路径标记 PIDL 分支也只查询 FOLDER/FILESYSTEM，导致 IShellItem 取得的目录能力不完整。

修复：已绑定目录的自身属性委托真实目录，并保留请求掩码；路径标记查询传递完整请求。未绑定的 namespace 根仍只声明自身实际支持的文件夹类型。实验性 Shell 分区对照分支不变。

## 证据

- 无窗口红灯：真实目录能力掩码为 0x177，投影返回 0xA0000000；断言失败。
- 修复后：投影目录自身和路径标记 IShellItem 均返回请求的 0x177，零掩码不泄露无关标志。
- 文件项保留 COPY/MOVE/LINK，原生 IDataObject 的 CF_HDROP 中实际路径指向测试 start.bat，而非投影解析名称。
- 直接调用真实 IDropTarget 的 DragEnter/DragOver/Drop：Ctrl 返回 COPY 并产生内容一致的 start.bat；Ctrl+Shift 返回 LINK，产生的 .lnk 用 IShellLink/IPersistFile 验证目标一致，源文件保留。没有执行 BAT，也没有自行进行文件复制或生成快捷方式。
- 上述 transfer 轮虽完成行为断言，Shell OperationStatusWindow 出现在共享桌面，前台隔离守卫失败。因此整轮不能记为通过，也不能证明物理 OLE 拖放恢复。相关测试新增显式 `--AllowInteractiveDesktopControl` 门槛，不属于后台套件。
- 常规后台套件所有构建成功，运行到 pane-tab-copy-order 时因前台变化守卫失败，中止剩余项；不记整套通过。报告位于 `src/.state/background-checks/20261106-120127-cf867b13409043aca1aedda598d5ea95/summary.json`。

## 验收边界

`--product-drop-attributes <product-adapter>` 无窗口检查可在后台执行，已纳入 verify-background.ps1。它验证属性和真实源数据，不启动拖放循环或文件操作。

真实鼠标拖放仍需集中验收：四窗格间和系统 Explorer 双向 Ctrl 复制、Ctrl+Shift/Alt 快捷方式、普通移动、文件夹落点、自动更新及源/目标焦点。不能用本次直接 COM 行为测试替代 OLE 目标注册、命中和实际修饰键派发。

本批只修复已复现的合同缺口；未覆盖当前用户正在使用的 temp.86，也未把尚未验证的完整拖放体验标记为修复完成。
