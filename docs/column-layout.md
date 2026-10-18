# 每标签列设置

2026-10-18。已接入源码并通过无窗口回归；实际 Shell 窗口验收尚未运行。

## 行为

- 用户仍在 Shell 详细信息列标题上选列、调宽、拖动排序。宿主不增加另一套列编辑器。
- 每个标签保存可见列的属性键、显示顺序和宽度；复制标签复制独立快照，新标签不指定列，由 Shell 使用默认列。
- 周期保存只查询可见标签，隐藏前捕获；图标/平铺等模式也可通过列接口读写，搜索结果不写入目录的持久配置。
- 调用 `SetColumns` 恢复可见列及顺序，随后逐列 `SetColumnInfo(CM_MASK_WIDTH)` 恢复宽度，最后沿用排序和分组恢复。改变查看样式的入口本身不重新设置列或排序。
- 不完整读取保留整个旧快照并阻止会话覆盖；恢复失败显示提示，阻止把部分恢复结果保存为新配置，重新导航时重试。没有列接口的视图读取时保留缓存；有待恢复列却无法应用时仍视为恢复失败。

## INI 与兼容性

会话 `FormatVersion=2`；每个标签新增：

```ini
DisplayColumnCount=2
DisplayColumn0={b725f130-47ef-101a-a5f1-02608c9eebac}:10,320
DisplayColumn1={b725f130-47ef-101a-a5f1-02608c9eebac}:12,180
```

索引就是可见顺序；不存本地化标题。Count=0 表示没有捕获列布局，保留 Shell 默认值，不隐藏全部列。最多 256 列，键唯一且 GUID 非空；宽度接受 0～65535 像素及 Shell 的默认/自动宽度标记 -1/-2。缺字段、重复键、计数不一致或非法宽度使该列布局失效，保留标签路径、排序和分组；加载时先备份损坏 INI。

v1 无列字段时正常加载。首次保存 v2 前，以唯一 `.before-v2-<id>.bak` 保存此前加载的 v1 原文；备份失败则不写新配置。并发修改仍由原有文件比较与互斥保护拒绝。新源码也拒绝覆盖高于 v2 的格式。

旧预览版遇到 v2 会拒绝加载/写入。需要回退时，先关闭使用同一 profile 的所有实例，另存当前 v2 文件，再手动从对应 `.before-v2-*.bak` 恢复 `ExplorerDirs.ini`；恢复的是升级前会话，升级后的标签修改不会自动合并。源码本轮未运行 GUI，没有升级用户正在使用的 profile。

## 验证证据与剩余验收

已做：列身份/顺序/宽度的 INI 往返、复制快照隔离、v1 备份一次、未来格式拒写、损坏数据备份恢复、读取失败/重复列/无效宽度保留旧缓存。无窗口 COM 测试服务器验证了真实指针 QueryInterface、vtable 调用、数组及结构封送、读回顺序、HRESULT 失败、列数量上限。结构大小和接口顺序以本机 Windows SDK 10.0.26100.0 `ShObjIdl_core.h` 为依据。这些证据不证明真实 Shell 的时序和视觉结果。

空闲桌面验收：

1. 两个标签打开同一测试目录，分别设置不同的列集合、宽度与顺序；切换标签/布局互不串用，排序与分组保持。
2. 复制其中一个标签，初始列设置相同；只修改副本，原标签不变。新建标签使用默认列。
3. 详细信息 → 图标 → 详细信息、进入子目录后返回、关闭重启，检查列布局和所选项/键盘焦点。
4. 不同 DPI、长路径、网络目录及特殊 Shell 视图检查效果和失败提示；本轮未验证这些环境。

官方依据：[IColumnManager](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-icolumnmanager)、[SetColumns](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icolumnmanager-setcolumns)、[CM_COLUMNINFO](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ns-shobjidl_core-cm_columninfo)、[列宽特殊值](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-cm_set_width_value)。
