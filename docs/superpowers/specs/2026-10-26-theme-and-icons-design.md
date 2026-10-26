# Windows 主题色、Q-Dir 颜色兼容与图标设计

## 状态

规划与阶段状态。初始 `System/Light/Dark` 偏好设计见[基础主题设计](2026-10-25-theme-preference.md)。

### 2026-10-26 实施记录

- 每窗格当前选中的标签现通过两 DIP 底边框消费动态 `ExplorerDirs.AccentBrush`；没有替换 TabItem 默认模板，也未改变标签拖动、关闭和原生 Shell 视图行为。静态 XAML smoke 与 Release 构建通过，实际颜色、四窗格观感和高对比度仍待可见验收。
- 第一阶段基础接线已实现：`SystemThemeMonitor` 读取 Windows Accent 并接收颜色变化；应用在 System/Light/Dark 三种模式下都使用最新成功的系统强调色，读取失败继续沿用上次有效值，首次失败回退到默认蓝色。主题 INI 格式未改变。
- 语义色板增加 Accent/AccentText，选中背景会基于浅/深中性表面和系统强调色派生，前景通过相对对比度选择。资源已提供应用级 Accent 画刷，当前活动窗格根边框使用该画刷，非活动窗格仍使用系统边框色；活动标签、焦点环、按钮图标尚未逐个接入。
- 布局工具栏按钮的选中态现使用对比度检查过的选择背景和动态系统强调色边框；未选中布局继续使用中性边框。主题资源切换由 WPF 动态资源处理。
- 每窗格的新建/关闭标签按钮复用应用本地矢量图标；主工具栏的预览/详情窗格开关添加带文字标签的对应图标。图标沿用动态语义画刷，Automation 名称、命令处理器和焦点顺序保持不变。
- 目录树、预览窗格和详情窗格三个 ToggleButton 的启用态现使用选择色底、可读前景和动态 Accent 边框；关闭态继续使用中性控件色，主题状态变化不修改其可见性/导航行为。
- 派生色纯逻辑冒烟、完整设置冒烟及 App Release 构建通过；尚未观察实际 Windows 强调色变化事件、多窗口同步、WPF 控件可见效果或高对比度切换。本阶段仍需桌面验收。

## 意图

先让 Explorer Dirs 的窗口外壳自然跟随 Windows 明暗模式与强调色，再统一应用自有按钮图标。保留 Q-Dir 多窗格工作流，同时分清三种不同能力：应用界面主题、文件/文件夹按规则着色、Windows Shell 原生文件图标。

## 已核实现状

- `Theme.ini` 已保存全局 `System/Light/Dark`；默认跟随 Windows。`SystemThemeMonitor` 现通过 `UISettings` 颜色变化事件读取系统明暗与 `UIColorType.Accent`；`ThemePalette` 可由系统强调色派生选中色与语义 Accent 资源。
- WPF 窗口、菜单、工具栏、导航和状态栏可通过动态系统颜色资源更新。ExplorerBrowser 文件区由 Windows Shell 绘制，不会因替换 WPF 资源而自动变色。
- 文件/文件夹图标由 Shell 提供。应用布局及命令按钮中还有字符或独立绘制图标，需要统一视觉资源。
- Q-Dir 公开资料将整体“Color - Theme”与文件颜色过滤分开列出；文件规则可针对扩展名、目录、只读、隐藏状态设置文字/背景/字体样式，`#BG` 与 `#BG-A` 分别表示普通视图和活动视图背景。FAQ 47 说明启用保存选项后可把颜色设置写入收藏 `.qdr`，但当前本机样本没有规则正向样本，`colorthema=0` 的含义也未确认。
- 隔离 `IShellView` 着色原型不等于产品 ExplorerBrowser 已支持逐项颜色。产品视图宿主迁移仍须通过独立的 Shell 行为等价门槛；导入后文件区黑底也是单独的未定位缺陷。

## 目标与边界

- 全局 `System/Light/Dark` 继续控制应用表面明暗；默认强调色来源为 Windows。
- Windows 强调色变化时，当前所有应用窗口即时更新，不写回偏好文件。
- 主题资源使用语义角色（背景、正文、次级文本、边框、悬停、选中、焦点、禁用、强调色），避免控件硬编码 RGB。
- 应用自有图标使用一致的矢量图形；中性图标保持中性，当前/选中/焦点状态才采用强调色。
- 文件/文件夹图标继续由 Shell 提供；不复制 Q-Dir 图标、预设调色板资产或专有资源。
- 高对比度优先遵循 Windows 系统色。保留当前标准非客户区标题栏；不在首期加入自绘标题栏或私有 DWM 深色开关。
- `.qdr` 导入不得改变全局主题。主题设置、Q-Dir 文件颜色规则与每标签 Shell 视图状态各自有清晰边界。
- 不把 WPF 主题变化声称为 ExplorerBrowser 文件区变色；不以强制白底掩盖黑底缺陷。

## 方案比较与选择

1. 只保留 `System/Light/Dark`，继续使用固定蓝色。实现成本最低，但无法随 Windows 强调色变化，也不能让多窗格活动状态统一体现主题色。
2. **推荐：用 Windows 明暗模式决定基础色板，用 Windows 强调色生成应用主题角色，逐步统一自有矢量图标。** 继续使用原生 Shell 文件区和文件图标。它能达到用户提出的系统集成体验，同时保留现有 ExplorerBrowser 的导航、选择、菜单和文件操作基线。
3. 将产品文件视图改为自绘 `IShellView`，一次承担 Q-Dir 行颜色规则和主题背景。它触及菜单、拖放、焦点、筛选、通知、搜索和虚拟位置等完整 Shell 合同，不能作为主题色功能的轻量方案；在迁移门槛通过前不选。

## 主题行为

### 偏好与颜色来源

- `Mode=System|Light|Dark` 决定基础明暗；缺省仍为 `System`。
- 强调色来源首期为 `System`，通过 `UISettings.GetColorValue(UIColorType.Accent)` 获取，并监听 `ColorValuesChanged`。显式 Light/Dark 只固定明暗，不冻结系统强调色。
- 先不提供自定义色选择器。系统强调色覆盖主路径后，再决定是否在未来加入 `AccentSource=Custom` 与手动颜色编辑。
- 系统主题或强调色读取失败时，沿用最后一次成功值；首次无法读取时使用明确的 Light + 默认蓝色回退。配置损坏、未来版本和外部编辑保护沿用现有 INI 规则。
- 若需新增配置字段，升级 `Theme.ini` 格式并无损读取现有 v1；升级前备份，旧版本不能覆盖新格式。

### 颜色角色

- 中性表面负责长时间阅读；强调色用于活动窗格边框/标记、当前标签、焦点环、选中/悬停提示、进度和可操作控件。
- 依据明暗模式对系统强调色生成浅/深状态层级，不把同一强饱和色铺满所有背景。
- 强调色上的文字按实际对比度选取黑/白前景；普通正文、禁用文字、焦点和选择仍分别有独立角色。
- 检查不同系统强调色下的对比度、细边框可见度和多窗格焦点辨识度；不通过仅改变颜色表达焦点。

### 图标

- 应用自有操作图标改为一个成套的本地矢量资源：布局、查看方式、导航、标签、刷新、复制/剪切/粘贴和常用命令优先。
- 图标以一致的网格、线宽、圆角和光学尺寸绘制；用语义色令其跟随主题，不以字体字符代替小尺寸图标。
- 系统文件类型图标、文件夹图标和 ExplorerBrowser 内容保持 Shell 原生来源。高对比度时优先提供系统可读的轮廓/颜色表现。

## Q-Dir 兼容策略

- “整体主题”是应用级外观；`#`、`#DIR`、`#RO`、`#BG`、`#BG-A` 和扩展名匹配是文件视图着色规则，分成不同设置和配置层。
- 不根据 Q-Dir 主题名称猜测 RGB，也不把未知的 `colorthema` 数值当成可互换主题。先支持本程序自己的 System/Light/Dark + 系统强调色；以后如有官方可读字段或用户提供的 Q-Dir 正向配置样本，再增加明确字段映射。
- `[Filter2]` 颜色规则导入只在公开语法和字段值含义确认后实现；用户选择源文件并先预览。不得自动读取或修改 Q-Dir 安装目录的配置。
- `.qdr` 颜色状态导入/导出需要一份启用了颜色保存、含规则与背景设置的真实样本；导入预览必须指出不支持的规则。没有样本时继续提示颜色未导入。
- 逐项文件文字/背景颜色只有在 Shell 宿主保留标准菜单、选择、排序、分组、键盘、拖放、文件操作和刷新后才进入产品实现。否则继续使用 ExplorerBrowser，不能用同步 WPF/ListView 伪装完整 Shell 文件视图。

## 分阶段计划与成功标准

1. **主题色获取与配置兼容。** 增加系统 Accent 读取与变化通知，确保现有 `Theme.ini` v1 无损升级/读取；验证 Light/Dark × 浅/深系统强调色四种组合、读取失败回退、高对比度优先级和多窗口统一资源刷新。
2. **语义色板。** 将应用自有 WPF 控件使用的硬编码颜色逐步替换为动态语义资源；验证菜单、工具栏、导航、地址栏、标签、状态栏、焦点、选择、禁用及窗口恢复。
3. **自有图标集。** 先完成布局和查看方式图标，再完成导航与文件命令；对比浅/深、高对比度、100%/150%/200% 缩放与四窗格窄宽度。图标更换不得改变命令路由或 Tab 停靠点。
4. **Q-Dir 文件颜色互操作调查。** 获得有效 `.qdr`/`Q-Dir.ini` 正向样本后，解码并验证导入预览；先完成宿主绘制能力与 Shell parity 评审，再独立决定产品接入。该阶段不阻塞系统跟随主题和应用图标发布。
5. **可见验收与独立预览。** 在桌面独占且明确授权后检查 Windows 切色/换强调色、多窗口、多布局、菜单、键盘焦点、高对比度和标题栏；生成隔离 profile 的版本并记录未验收的原生 Shell 限制。

## 验证边界

- 纯逻辑测试覆盖主题解析、调色板派生、对比度前景选择和配置迁移。
- App Release 构建覆盖 WinRT 颜色 API、WPF 资源与图标资产编译。
- 非交互测试不能证明 Windows 实际改变颜色后的事件时序、WPF 菜单样式、非客户区标题栏或 Shell DirectUI 文件区颜色。
- Q-Dir `.qdr` 导入器自己的往返不代表 Q-Dir 能读本程序扩展；实际兼容需在授权隔离桌面做 Q-Dir→Explorer Dirs 和 Explorer Dirs→Q-Dir 两向验收。

## 官方依据

- Q-Dir [FAQ 47：活动窗格背景、规则优先级与收藏保存](https://softwareok.com/?faq=47&seite=faq-Q-DIR)
- Q-Dir [文件扩展名颜色筛选示例](https://www.softwareok.com/?page=Windows/Q-Dir/Tips/9)
- Q-Dir [整体主题与颜色设置](https://www.softwareok.com/?page=Windows/Images/Q-Dir/4)；该页面描述功能，不提供可直接导入的主题色板文件格式。
- Microsoft [`UIColorType`](https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.uicolortype)、[`UISettings.GetColorValue`](https://learn.microsoft.com/uwp/api/windows.ui.viewmanagement.uisettings.getcolorvalue) 与[`ColorValuesChanged`](https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.uisettings.colorvalueschanged)
- Microsoft [Windows 高对比度主题](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes)与 [WPF 动态资源](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/systems/xaml-resources-overview)
