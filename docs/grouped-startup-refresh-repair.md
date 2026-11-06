# 避免重复恢复已正确的大小分组

2026-11-06，基于7f0f69ea。范围是首次分组恢复的冗余动作，不声明解决所有自动变化空白或删除残留。

## 缺陷与实现

现有恢复等待大小值就绪后，总是下发临时/目标排序；分组模式随后强制完整刷新。已物化且顺序正确的分组也走该流程，可能在启动后与文件变化重叠。

新增前置判定：实际分组键和方向匹配保存状态，所有当前行都有有效GroupId，原始字节值在各组内符合保存方向及混合策略，并且观察期间导航/输入/目录变化没有使结果失效，才结束这轮恢复。原始全局排序检查保留；组内检查独立维护每组前一项，不能拿跨组边界判断大小。

检查放在排序setter之前；试验确认setter会让已经有效的分组进入暂态。在同步输入恢复回调里，IShellView.GetWindow还返回8001010D；最终使用宿主已持有的视图HWND，仅读取列表GroupId，不做Shell窗口call-out。读取按现有批次让出Dispatcher，临时数组/字典不持久保存。

分组不存在、行组未分配、值未知、行序不正确或状态无法确认时，保留既有排序及首次完整刷新。不是删除冷启动分组恢复，也不更改自动软刷新/F5的分路。

## 实际证据

- 红灯：src/.state/grouped-recovery-before.log，实际产品宿主中原生GroupId已经有效，仍触发restore-full-refresh；不是仅根据GroupView enabled判定。
- 新用例 `--product-grouped-recovery-private-desktop <HostProbe.dll> <adapter>`：先建立真实大小组并稳定夹具，再设置选区，重触发恢复；验证groups-already-ordered路径、没有新full refresh、原生分组与选区/焦点项保留。
- 最终定向日志 src/.state/grouped-recovery-final-clean-test.log：file-middle.bin/folder-small/folder-large，GroupId=0/1/1，组内顺序通过，跳过路径与保留断言通过。前台不变，但共享输入活动使隔离守卫失败，不记完整隔离通过或可见验收。
- 冷启动四宿主对照 src/.state/grouped-recovery-cold-four-host.log：每窗格32行，前两窗格GroupView尚未建立、第三窗格组内顺序错误，均保留恢复；第四窗格实际正确而跳过。四窗格最终原始大小顺序/单元格、分组、选区/焦点项及退休清理断言通过；私有桌面监测期间共享前台/输入变化，整轮守卫失败。
- 35/35后台整轮通过：src/.state/background-checks/20261106-143331-c2e790cf9fa74f8882b3db708071530a/summary.json。期间源合同测试识别出上一批诊断把原生Refresh传给局部委托后仍要求旧拼写；现同时检查原生默认赋值及策略调用接线，既有实际软刷新路由断言保持。

35项整轮之后对空分组键另作保守收窄；最终HostProbe Release构建与无窗口navigation-focus-requests回归通过，未再重跑整轮或新增窗口测试。未知值/pending、大目录、完整App自动变化叠加及可见闪烁仍需后续集中验证。正常原生DLL、用户profile和releases未改。
