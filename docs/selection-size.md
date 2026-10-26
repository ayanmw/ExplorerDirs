# 选中项目的可取得大小

2026-10-20。源码已接入，未发布，真实多窗格显示与响应速度待 GUI 验收。

## 实现与显示语义

大小聚合只用于状态栏。文件命令菜单的选区检查只读取项目数量，
不顺带获取选择集属性存储，避免菜单展开/调用前增加不需要的元数据查询。
此改动减少调用范围，未据此宣称真实大选区的响应时间已验收。

从当前原生视图的 `IFolderView.Items(SVGIO_SELECTION)` 取得 `IShellItemArray`，
不重建路径列表，不扫描目录。接口支持取得选择集数组，见
[微软 IFolderView.Items 文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-items)。

选择集通过 `GetPropertyStore(GPS_FASTPROPERTIESONLY)` 读取 `System.Size`。
该标志要求使用快速属性，不为了补齐缺失值打开磁盘/网络上的内容；属性数组按各属性的聚合规则汇总。
见 [快速属性标志](https://learn.microsoft.com/en-us/windows/win32/api/propsys/ne-propsys-getpropertystoreflags)
和 [选择集属性聚合](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitemarray-getpropertystore)。

- 仅接受 S_OK 和 VT_UI8。有效的 0 字节保留；VT_EMPTY、失败、类型不符都视为未知。
- 显示“可取得大小”，提示未提供大小的项目不计入。它不是承诺完整的选择总大小。
- 不递归文件夹、不启用慢属性处理器、不为缺失项添加 FileInfo/文件读取兜底。
- 数组项目数量必须与本次统计一致；整个读取后再次检查导航代际与 Shell 选择版本。
  任何变化都丢弃这一帧统计，下一次刷新重读。
- 文件大小和磁盘容量共用字节格式化；窄窗格截断统计文字并通过提示保留完整内容。

当前随可见窗格统计刷新，在所属 Shell STA 读取；GPS_FASTPROPERTIESONLY 避免主动补读慢内容，
但进程内 COM 提供方仍可能耗时。尚未据短小本地样本证明网络位置、云提供方或超大选区的响应时间。

2026-10-24 起，状态栏仍每 750 ms 刷新项目数；大小聚合另按视图代际、Shell 选择版本和选中数缓存。
选区或视图变化会在下一次统计刷新立即重读；选区保持不变时最多每 5 秒重读一次，
让原地文件大小变更仍有周期刷新，同时避免静态大选区每 750 ms 重复占用 Shell STA。
未知大小结果同样缓存至下一次选区变化或 5 秒到期。该节流尚未做可见四窗格响应测量。

### 本地属性汇总规模探针（2026-10-24）

新增 `--selection-properties-scale` 诊断，只通过 `SHParseDisplayName` 和
`SHCreateShellItemArrayFromIDLists` 构造本地测试项数组，不创建 ExplorerBrowser、不显示窗口，
并直接计时生产 `ShellSelectionSize.Read`。本机 NTFS、同一 STA 的单次读数：25 项 19.12 ms、
250 项 6.78 ms、1,000 项 28.12 ms；每组聚合字节断言通过。计时只覆盖属性数组读取，
不含建文件、PIDL 解析、窗格刷新、Dispatcher/UI 响应，且首次调用/属性缓存会影响小样本，
因此仅能说明本次 1,000 项本地快速属性读取约 28 ms，不能证明大选区或可见体验不卡顿。

尝试把夹具扩大至 10,000 项时，准备阶段约 90 秒只完成了约 4,300 个文件/PIDL；
在进入计时前中止并清理了隔离临时目录。该准备成本属于探针逐项创建/解析路径，
不是生产大小读取结果；10,000 项属性汇总仍未测。后续如需覆盖，应先采用更快的批量夹具准备方式。

## 已执行验证

```powershell
dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --selection-properties
dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --selection-properties-scale
dotnet run --project src/ExplorerDirs.NativeProbe -c Release -- --navigation-capability
```

第一条命令创建独立测试文件，通过真实 Shell 属性存储及生产读取代码交叉验证：

| 选择 | 文件字节安排 | 生产读取结果 |
| --- | --- | --- |
| 单文件 | 3 | 3 |
| 两个文件 | 3 + 5 | 8 |
| 空文件 | 0 | 有效的 0 |
| 文件夹 | 内有 7 字节文件 | 未提供大小 |
| 混合 | 3 + 文件夹 + 5 | 8，未计入文件夹内容 |

每组还验证数组数量与期望选区数量不一致时返回未知。先使用未实现读取器运行，
在单文件 3 字节断言处失败；实现后上述场景全部通过。

第二条命令在隐藏 ExplorerBrowser 内，以 `IFolderView.SelectItem` 只选择项目，不设置焦点，
从真实视图选择数组读到三个选中项、可取得大小 8 字节。原生父窗口始终不可见，
本次前台窗口对比未改变。该检查覆盖视图→选择数组→生产大小读取连接，但不等于可见 App GUI 验收。

## 待验收

- 鼠标、键盘多选及清空选择；不同选区数量相同的切换；导航/切标签时不残留旧值。
- 四窗格和紧凑布局下的统计、容量、错误提示空间分配。
- 大选区、网络与云位置的延迟、缺失值和属性缓存刷新。
- 可见大选区交互与跨窗格 UI 响应；本轮直接属性探针不创建 ExplorerBrowser。
- 原地修改已选文件后大小更新；发布版的真实 UI 和焦点。
