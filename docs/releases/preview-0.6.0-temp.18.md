# Explorer Dirs 临时预览 temp.18

本版基于 temp.17，新增标签栏空白处拖入一个文件系统文件夹或驱动器时创建并选中新标签。普通文件、多项拖放、URI 和未解析/非文件夹对象不会创建标签；拖到现有标签仍不导航，标签之间的重排行为保留。虚拟 Shell PIDL 路径通过 Windows `SHCreateDataObject` 生成的 CIDA 数据对象直连生产解析器；Shell 格式查询和 `::{GUID}` 文件夹身份解析均通过无窗口 smoke。该夹具验证格式读取，不模拟真实拖放。

版本使用独立 profile，启动和配置不影响日常预览版或 `src` 调试配置。

已验证：Release `SettingsSmoke` 全量回归、App Release 构建、标签拖放路径决策与普通 `CF_HDROP` 文件夹/文件/多项冒烟、原生 CIDA 虚拟 Shell 文件夹解析 smoke、发布隔离 smoke、EXE/DLL SHA-256 与程序集版本检查。

尚未验证：实际鼠标拖放、标签栏空白区命中、真实虚拟 Shell 拖放来源、拖放后可见导航与跨重启恢复，以及 Q-Dir 对应操作的可见一致性。本次没有启动 GUI、ExplorerBrowser、探针或 Q-Dir。
