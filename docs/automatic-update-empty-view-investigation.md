# 自动变化期间短暂零行：刷新来源调查

2026-11-06，基于00da1e3f。本批定位并补诊断，不声明解决空白或删除问题。

## 当前实测

用既有 EXPLORERDIRS_REFRESH_ORIGIN_TRACE 编译隔离App，并以普通产品宏加 NotificationTrace 编译原生适配器；没有修改日用profile或releases。App副本与准备载荷DLL SHA-256相同（4B5C50FA3EFE83BC2454B7465CA831F67AD28C49316FA0847B22EA12C87D9318）。

- 准备载荷：src/.state/gui-smoke/20261106-060312-447-053377。
- 完整进程日志：src/.state/refresh-origin-native-trace.log。
- 原始日志目录：src/.state/app-session-process/1f6590aa542449f19d9492ba777dfbee/app。
- 关联分析：src/.state/refresh-origin-analysis.json。

| 观察范围 | 原生EnumObjects调用 | 应用刷新请求 |
| --- | ---: | ---: |
| 第一进程 before-create 至 settled-file-burst-delete（490164187–490175437） | 0 | 0 |
| 第二进程 before-create 至 settled-file-burst-create（490184796–490198843） | 0 | 1 |

第一进程自动变化阶段已出现短暂零行，但没有应用刷新请求或文件系统重新枚举。不能继续把所有空列表都归因于完整枚举。零行采样本身也不等同可见白闪：仍需区分列表结构变动、重绘暂停和真正呈现。

第二进程490191718记录一条manual=False/full=True/soft=True请求，调用栈来自ScheduleInitialVirtualSort；此时探针已进入文件创建阶段。说明保存大小分组的首次恢复可能与可操作后的自动变化重叠。原日志写在ShellRefreshPolicy.Invoke之前，因此它只能证明请求，不能证明策略准备成功或真正进入IShellView.Refresh。这也是本批补充调用入口/返回记录的原因。

第一进程功能流程完成，第二进程三文件批量删除残留并超时；期间共享前台从锁屏变为WXWork且输入时间改变，隔离守卫未通过。只保留定位证据，不作为性能对比、干净隔离成功或普通构建稳定性结论。诊断同步日志IO也会扰动时序。

## 新增诊断与验证

仅EXPLORERDIRS_REFRESH_ORIGIN_TRACE构建记录SysListView32收到的WM_SETREDRAW、LVM_DELETEALLITEMS、LVM_SETITEMCOUNT、LVM_SORTITEMS/EX；不在消息回调内反查Shell状态。正数逐项计数省略，最多512条，写入失败停用。记录的是收到的命令，不能据此推断已转发或绘制；sort-hold字段用于关联应用呈现保护。

刷新来源日志另记录native-enter/native-return，区分请求、策略准备和实际调用。正常构建编译移除两类日志主体，未增加产品配置入口。

普通App/HostProbe、诊断App和SettingsSmoke构建成功；导航焦点请求与软刷新路由定向检查通过。src/.state/list-structure-build-check.json确认普通DLL不含两类日志字符串、显式诊断DLL包含。新增结构消息日志尚未执行窗口验收，不将编译或字符串检查算作运行通过。普通构建已恢复。

下一步：在集中隔离窗口测试中关联结构消息、刷新入口/返回、原生通知及零行时间；另检查是否可对“实际行组已物化”的启动恢复省去完整刷新。现有分组首次物化曾有失败反例，未验证前不得直接删除该分支。真实拖放、导航默认选择及temp.86其它门槛继续保留。


## 2026-11-06 结构消息与名称排序对照补证

当前产品基线89490e66；增加的仅为AppSessionProcessProbe采样，不更改产品刷新、排序或成功判定。

- `src/.state/list-structure-full-app.log`：四窗格1025 filler，批量删除失败。结构日志仅观察到WM_SETREDRAW，未观察到所监听的清空/计数/排序消息；记录到一次启动分组修复真正进入Refresh，返回S_FALSE（不是S_OK）。不能据此称为成功完整枚举。
- `src/.state/list-structure-render-state.log`：采样增加跨进程读取起止tick、IsWindowVisible和SysSetRedraw前后值。仍有短暂零行及批量删除失败；零行样本可见标志保持true，未取得SysSetRedraw非零证据。属性为零也可能是属性不存在，不能认定绘制已启用，更不能把零行直接等同可见黑屏。
- `src/.state/list-structure-name-sort-control.log`：仅探针配置名称主排序，保留四窗格/分组/大目录；外部删除阶段仍超时。本轮foreground-unchanged=True、input-change-observed=False、isolation-clean=True。这是受控失败反例：不能把删除问题限定为Size主排序，也不能用名称排序绕过功能目标。

前两轮受共享输入活动影响，不用于干净性能对比；名称对照的失败无需再用桌面活动解释。三轮均未修改日用profile、未覆盖release。新增计数为诊断性累计样本数，不是闪烁次数或持续时间；跨进程轮询存在采样扰动。

下一步聚焦原生通知投影/列表增删的一致性，保持自动软更新不降级为完整刷新；首次分组恢复优化已单独落地，但没有关闭此问题。可见呈现与真实拖放仍待集中验收。
