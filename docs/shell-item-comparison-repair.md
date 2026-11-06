# Shell 条目元数据比较合同

2026-11-06，基于 c60503cf，独立修复与大目录删除调查分开记录。

## 复现与修正

[Microsoft CompareIDs 文档](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-compareids) 区分 canonical 名称/身份比较与 ALLFIELDS 的条目元数据比较。原实现对 ALLFIELDS 主要比较名称，同路径旧/新 PIDL 大小不同仍返回相等。

无窗口真实夹具：同一文件先保存37 B的PIDL，再改为83 B并重新解析。原DLL得到 canonical=0、numeric=-1、all-fields=0；候选得到 canonical=0、numeric=-1、all-fields=-1。

配套发现：ParseDisplayName 的 DescribeBackingChild 只保存 FOLDER/HIDDEN，而 EnumObjects 保存 WIN32 属性；加入元数据比较后，解析/枚举会把本来未变化的文件判成不同。现对可读取物理条目统一属性，并对目录身份/递归快照使用与枚举相同的 reparse/super-hidden eligibility；不能取得物理属性的原有 Shell 路径保留回退。未更改PIDL格式。

ALLFIELDS 比较名称、保存大小、属性、文件/目录计数和可用的文件身份；不把枚举序号及父目录合计当作当前条目元数据。第一层相同时保留现有多层相对PIDL尾部比较。没有增加自动完整刷新或固定重试次数。

新增 `--product-item-comparison <adapter>`：大小变化、只读属性变化、重复解析相等、反对称性、不同名称区分、嵌套叶项变化、相同文件枚举/解析结果一致，均为无窗口原生DLL测试。探针自身首次枚举循环曾错误释放S_FALSE下未写的out槽，退出为堆损坏；已改为明确初始化ref槽，不能将该故障归因于产品视图。

## 集成边界

- `src/.state/product-item-comparison-fixed.log`：扩展后的合同通过，未创建Shell视图。
- `src/.state/allfields-large-full-app.log`：早期候选（尚未统一解析属性）在四窗格各1025 filler的三文件批量删除失败，最后一个窗格残留三项；隔离守卫通过。
- `src/.state/allfields-large-before-control.log`：相同载荷/夹具、仅换回修正前DLL；第一进程通过，第二进程同一批量删除阶段两个窗格残留；隔离守卫通过。因此旧问题确实存在，但这组样本不能证明候选改善或恶化概率。
- `src/.state/allfields-small-full-app-final.log`：最终属性统一候选，两独立进程59212/16028通过创建/删除、三文件突发、同名37→83 KB重建、原生大小单元格、重命名、最终删除、四窗格五标签设置写回及窗口位置恢复；隔离/注册清理通过。探针采样到短暂空列表，不能宣称无闪烁或性能改善。

本修复不证明大目录自动删除可靠，不证明真实导航/拖放或可见绘制通过，不覆盖发布目录。后续仍需按逐条删除路径、通知身份与消费时序定位残留，不用数量收敛代替路径证据。

## 最终候选与回归身份

- 正常DLL SHA-256：98C635101D7E259E72E63F2B505F76EB25D95BDBA48B80986B8C200674EADD8A；保留在 src/.state/allfields-comparison/ExplorerDirs.AfterAllFieldsFinal.dll。前态DLL为 BDFADDC007BFDD51BA160AB5E5B2E6B18459D3FA643AFE706CB5D5E9CF28B1FD。
- 后台35/35通过：src/.state/background-checks/20261106-135414-3a42f7ac6e0d417d909515e7169d17f7/summary.json。之后仅复用已分配的解析路径，减少一次目录字符串分配；正常DLL重新构建及扩展比较检查通过，未再整套重跑。
- 当前源代码准备的完整App隔离载荷为 src/.state/gui-smoke/20261106-055543-371-f8b3e0，manifest记录c60503cf及dirty源码，未启动共享桌面窗口。最终DLL以探针adapter override注入子进程。
- src/.state/allfields-large-current-app-final.log：四窗格各1025 filler、两个独立进程73624/42548通过目录/文件/三文件突发自动增删，分组保持、会话重写与窗口位置恢复；前台/输入/清理守卫通过。累计采到28次空列表，不能称无闪烁或性能通过。这一正例不推翻前面的失败对照，也不能独立关闭间歇性删除问题。

独立复验 src/.state/allfields-large-current-app-repeat.log 也通过：进程53900/57624、同一最终DLL、新隔离profile，目录/文件/三文件突发自动增删、分组/会话/窗口位置、前台/输入/清理全部通过，累计采样24次空列表。最终候选两轮四进程通过是本机此夹具的正证据；不声明任意规模、任意时序均可靠，也不把消除残留等同于无空白刷新。下一步继续查自动变化期间的枚举/呈现路径，以及更大突发与同名重建。
