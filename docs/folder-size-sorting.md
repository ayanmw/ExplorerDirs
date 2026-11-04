# 文件夹与文件大小排序：接线与验证

## 已确认目标

工具/选项/文件夹集中显示开关、来源和未来的混合排序开关。混合排序默认关闭；关闭时文件夹在前、文件在后分别按大小排序，开启后统一排列。标签独立记忆、复制继承、便携 INI 恢复。当前仅前两类现有设置完成菜单集中；混合开关、会话字段和产品排序接线尚未完成。

## 2026-11-04 原生对照

Microsoft SHCOLSTATE_NOSORTBYFOLDERNESS 明确表示不单独排列文件夹：https://learn.microsoft.com/windows/win32/api/shtypes/ne-shtypes-shcolstate 。它可供大小列策略使用，但文档语义不能替代实际 Shell 行序验证。

第一原型给 SyntheticSortFolder 增加每实例私有 COM 能力，大小列禁用 Shell 独立分类，比较器自行处理分类，并尝试通过 IFolderView2.GetSortColumns 对降序反转作补偿。原型中的默认混合值仅保留旧比较行为，不是用户要求的产品缺省值；产品将来仍必须以 false 应用。

实际 ExplorerBrowser 私有 Desktop 夹具中，设置 IFolderView2.GetFolder 返回对象为独立排序后，直接比较目录和文件确实进入分类分支；但是 Shell 实际排序在别的 SyntheticSortFolder 实例中执行，仍然按大小混排。诊断日志用实例地址确认这一点，例如同一运行中设置/直接比较实例为 2618364072112，实际排序实例为 2618366996080（地址只用于该次进程内对照）。不能把实例字段当成每视图状态，也不能用进程全局开关掩盖同路径标签串用。

证据：src/.state/product-size-mixed-sort-instance-trace.log；隔离后可重复的失败入口为 product-size-mixed-sort-isolated-experiment.log。完整行序断言保留，第一项“关闭混合、升序”仍失败；后续混合/降序未因此记通过。几轮均前台/输入守卫干净，没有控制用户桌面。

## 实验隔离

全部策略接口、分类分支、列标志和实验字段受 EXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT 保护。只有显式开关构建该原型：

```powershell
pwsh -NoProfile -File src/ExplorerDirs.NativeProbe/build-product-shell-folder-adapter-diagnostics.ps1 -SizeSortPolicyExperiment
dotnet run --project src/ExplorerDirs.NativeProbe/ExplorerDirs.NativeProbe.csproj -c Release -- --product-size-sort-policy-private-desktop src/ExplorerDirs.NativeProbe/bin/native-product-diagnostics-x64-sort-policy/ExplorerDirs.ShellNamespace.Diagnostics.dll
```

它预期目前会报告真实行序不符，而非验收通过。实验 DLL 输出独立目录，不覆盖产品或普通诊断库。普通比较诊断记录新增有界的实例标识，帮助确认重新绑定。

普通产品 DLL 未启用实验，SHA-256 仍为 D2E02736BDD01843BE9D71CA4C2CFDBC4516CD6443D671837FF4B9E325F953A7，与此前 native-repeat-hash.log 相同；现有排序二进制行为未被这个失败原型替换。

## 本批其他验证和限制

- MainWindow.xaml 编译通过，现有显示大小及三来源在同一文件夹组且入口不重复，点击/INI处理沿用。无菜单可见验收。
- 27 项后台检查整轮通过：src/.state/background-checks/20261104-215214-e66990263b2246e9ab6c825badca9c04/summary.json，记录提交前源码状态。普通构建不运行实验排序分支。
- 原有独立原生显示探针复跑在外部新增后选中状态由 0x3 变为 0x1，丢失选中位、保留焦点位，整项失败；日志 product-size-display-after-policy-isolation.log。该轮守卫干净，必须保留失败。此夹具不是完整产品 WPF 宿主，尚不能认定产品已复现同一故障。它也不能因产品 DLL 哈希相同而被忽略，后续需与完整宿主的选择恢复路径对照。
- 未覆盖 temp.86，未更改用户配置或系统桌面。

## 后续接线

需要确定可在 Shell 重绑定/后台排序实例中恢复、且仍区分同路径不同标签的视图策略上下文，例如由瞬时视图身份/PIDL携带上下文，而非按物理路径建全局设置表。先验证模式和方向传播，再扩展 ViewState/INI/复制/QDR；不得提前发布一个仅保存勾选状态的产品开关。实际原生排序及原有选择恢复的失败保持未完成。

## 后续隔离对照：身份传播与失败路径

实验 DPR2 PIDL 可将独立/混合策略传给 Shell 重绑定后实际执行比较的对象；product-size-sort-context-rebind-trace.log 的 encoded-policy=0 证明传播，尚不证明最终行序正确。新增无窗口 PIDL 检查覆盖两模式、方向位、1024 字符分段边界、多段根、非法位及跨段不一致；sort-policy-safe-pidl.log 通过。所有新格式和导出只在显式排序实验宏中启用，未接入产品标签或配置。

曾尝试在 GetCurFolder 中查询 IFolderView2.GetSortColumns，再序列化当前方向。encoded-root 初始化出现栈溢出（0xC00000FD，product-size-sort-context-direction.log）；未采集原生栈，不能断言完整递归链。已撤去回调中的视图反查和 HWND 属性路径，比较器只读不可变实验方向。后续必须由明确排序请求传入方向，而非在序列化或比较回调中反查视图。

公开 IColumnManager 状态对照：SetColumnInfo 返回 S_OK，但请求状态 0x5 后 GetColumnInfo 仍读到 0x1。保留失败断言，日志 sort-policy-column-state-readback-final.log；该次初始大小、单项补值、F5 和列重排先通过，失败发生在混合策略写回检查。前台/输入守卫干净，四个宿主销毁后存活列表 HWND=0。故不采用仅调用 SetColumnInfo 即宣称已支持的方案。

清理后后台总检 27 项通过：src/.state/background-checks/20261104-225059-b5799d51aa194301ad94d91e2c892ec2/summary.json（源码为 7796e50 加本批未提交修改）。普通产品重新构建 SHA256 为 36730EAA8DB30C3DB89ECFE015845C530D8E336FC32390B3D9C5ADC7D73EC174，不能再沿用前批哈希不变的结论；普通根解析辅助函数也有源码调整，排序策略本身仍受实验宏隔离。未进行可见 UI 验收，未覆盖预览。
