# Q-Dir 配置导入

2026-10-18：源码入口“文件 → 导入 Q-Dir 配置 (.qdr)…”。选择文件后先显示标签数量与未支持项，确认后追加到对应窗格；不关闭已有标签、不修改源文件。导入前将当前会话写入同一 profile 下的 `ExplorerDirs.ini.before-qdr-*.bak`。备份或完整快照失败则不追加。

## 已实现范围

- UTF-8、带 BOM 的 UTF-16、系统 ANSI 编码；最大 8 MB，必须有 `Q-Dir Version` 标识。
- `Dir1`～`Dir4` 和编号标签的明文绝对路径、环境变量路径；`Folder1`～`Folder4` 及编号标签中的 `%XX` Shell PIDL。支持可解析为文件系统目录的 `shell:` 位置；虚拟位置无法解析时跳过并提示。
- 按窗格恢复标签，读取 `CurPos` 排序。本机 12.79 文件未为活动标签重复保存编号条目，因此将基础 `FolderN` 放回编号空位，并选中该标签。无效条目被跳过时保留原编号空位语义。
- 导入每标签 `ViewModeN` / `ViewModeN_i`（1～8）；未知值用详细信息并提示。
- 对已核对的 Q-Dir 12.79 `ColsAndSort` / `CODEMODE1` 包装，通过 Windows 属性存储 API 读取图标大小、排序、分组以及可见列顺序和宽度。显式 `ViewModeN` 优先于属性中的模式；缺少视图数据时沿用默认值。
- 已有公开样例和本机证据的 `ViewMode=0` 对应四窗格；其他布局编号保留当前布局并提示，不能按数值猜测。
- 单窗格导入后上限 100 标签。创建过程中异常可能留下部分已追加标签，提示明确说明，并给出原会话备份位置；不声称失败后已自动回滚。

## 明确未导入

未知 `ColsAndSort` 包装或流结构；树、预览、颜色、分隔比例、窗口位置、活动窗格及启动命令。不执行配置里的命令。已识别的视图数据转换成本程序的独立 INI 设置，不保存原始二进制。

视图导入先校验百分号编码、声明长度和已观察到的 52 字节包装，再用 `PSGetNamedPropertyFromPropertyStorage` 读取 Windows 序列化属性。只接受已确认的属性类型、排序方向和列流结构，限制数量与宽度，拒绝重复列。任一必需字段无效就舍弃该标签的整份扩展视图快照，保留目录与显式查看模式，并在预览中提示。`CODEMODE1` 后的有符号字段含义未确认，不声称已验证其校验和；此实现不是所有 Q-Dir 版本的通用解码器。

PIDL 先校验百分号编码、总长度、各子项边界与终止符，再交给 Windows 解析。解析及文件读取在后台执行，预览确认和追加在 UI 线程执行；不递归扫描目录，不以 `Directory.Exists` 作为导入门槛，离线路径可保留。

## 验证

无窗口检查覆盖 UTF-8/UTF-16、中文及 `&` 路径、原编号空位、重排标签、无效路径跳过、未知布局、错误 PIDL、缺少标识及原生 PIDL 往返。本机 `start.qdr` 只读解析识别 42 个标签，四窗格为 12/11/10/9；另一份 Quick-Link 配置识别 28 个标签，四窗格为 12/3/9/4。未将实际工作路径加入 Git，也未通过 GUI 将它们导入测试会话。

扩展检查使用 Windows API 生成合成属性存储，覆盖倒序排序、分组、图标大小、列宽及 INI 往返；错误编码/长度/版本、属性类型、缺失字段、截断流、重复列与非法方向均回退并警告。本机两份文件分别成功读出 42/42、24/28 个标签的视图快照；第二份只有 24 个视图数据字段，其余 4 个使用默认值。Release 编译 0 警告/0 错误，Settings smoke 通过；不代表 Shell 实际应用和重启后的视觉状态已通过。

GUI 待验收：打开选择框 → 选隔离 `.qdr` → 取消不改变标签 → 再次打开确认追加 → 现有标签保留 → 导入标签顺序与选中项正确 → 排序/分组/图标大小/列顺序/列宽一致 → 备份存在 → 重启恢复。另检查无可导入目录、文件超限和单窗格超限的提示。

来源：[Q-Dir 官方明文 `.qdr` 样例](https://www.softwareok.com/?faq=117&seite=faq-Q-DIR)、[官方收藏配置说明](https://www.softwareok.com/?faq=18&seite=faq-Q-DIR)、[SHGetPathFromIDListEx](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shgetpathfromidlistex)。私有格式没有已确认的通用规范，目前兼容范围以这些样例与本机文件为限。

Windows 属性存储读取依据：[PSGetNamedPropertyFromPropertyStorage](https://learn.microsoft.com/en-us/windows/win32/api/propsys/nf-propsys-psgetnamedpropertyfrompropertystorage)。包装及 `Sort`/`ColInfo` 流布局来自本机样例核对，不作为微软公开格式保证。
