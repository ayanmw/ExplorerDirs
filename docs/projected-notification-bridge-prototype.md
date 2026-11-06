# 投影目录逐项删除通知：隔离原型

2026-11-06。状态：单个原生ExplorerBrowser视图原型有正向证据，未接入产品，未关闭temp.86自动更新门槛。

## 原型范围

`-ProjectedNotificationBridgeExperiment`单独编译到`bin/native-product-x64-notification-trace-projected-notify-experiment`，普通构建无此能力。原型通过当前IFolderView取得目录对象的私有试验COM接口，调用SHChangeNotify发送投影绝对PIDL的RMDIR/DELETE；不调用IShellFolderView、ListView删除或Refresh。

接口的暂停物理订阅方法只用于隔离测试：先切断原有物理通知路径，确认磁盘删除后旧行仍在，再验证直接投影通知确实移除对应行。暂停不影响视图自身的投影命名空间订阅。此暂停方法不得接入产品或用户选项。

初版原型由测试提供项目叶名和文件/目录类型；当前原型只接收叶名，类型取自既有原生EnumObjects输出的记录，不在删除后猜类型；拒绝空叶名、点目录、斜线、反斜线及冒号。只有路径明确不存在才发删除通知；重建路径返回S_FALSE。该存在性检查不是并发同名重建的完整保证，仍有检查后发送前的竞态；不据此宣称生产安全性完成。

## 实测证据

- 初始普通DLL对照：IFolderView.GetFolder查询试验IID返回E_NOINTERFACE，测试在能力获取处失败；随后才增加原型实现。
- `src/.state/projected-notification-bridge-small.log`：真实原生视图中分别删除空目录和文件，物理订阅暂停时保留旧行；直接投影通知后移除对应行，未受影响项目的选中和焦点状态保留。重建路径、非法叶名拒绝。该初轮仍创建了三个辅助视图；后续原型分支提前返回，仅保留一个主视图。
- `src/.state/projected-notification-bridge-large.log`：主视图1025个额外文件加4个基本项目，总1029行。相同文件/目录删除、旧行基线、选区/焦点和输入边界断言通过。原型原生EnumObjects累计计数在删除前后不变，不仅是源码没有调用Refresh。销毁1个视图后无残留列表HWND。
- 两轮均共享前台不变、检测到外部输入变化，isolation-clean=False，命令整体退出1；只称业务子断言通过，不称整轮隔离通过或可见闪烁验收成功。
- 普通DLL重新构建且不含试验COM能力标记；无窗口product-item-comparison、product-parsing-identity检查通过。没有覆盖release或日用profile。

## 复现命令

```powershell
pwsh -NoProfile -File src/ExplorerDirs.NativeProbe/build-product-shell-folder-adapter.ps1 -NotificationTrace -ProjectedNotificationBridgeExperiment
dotnet build src/ExplorerDirs.NativeProbe -c Release
dotnet run --project src/ExplorerDirs.NativeProbe -c Release --no-build -- --product-projected-notification-bridge-private-desktop src/ExplorerDirs.NativeProbe/bin/native-product-x64-notification-trace-projected-notify-experiment/ExplorerDirs.ShellNamespace.dll --filler-count 1025
```

命令创建从不切换到用户屏幕的私有Desktop，不注入键鼠输入；会创建并清理专属临时夹具，监测共享前台和输入。不是默认后台整轮脚本的一部分，也不是允许在共享桌面做真实拖放。

## 产品接线前剩余项

1. 初始枚举类型来源已完成原型验证；新增/重命名时的记录更新、文件身份和并发枚举覆盖仍待做。删除查找使用有序映射，不增加同步全列表扫描；仍须评估大目录和多标签的实际成本。
2. 保留已存在/同名重建保护，并验证异步投递期间的重建、导航退休、关闭和COM重入。当前测试只有“调用前已重建”的子项。
3. 将已有FileSystemWatcher事件定向到当前目录能力，保留普通物理/虚拟Shell位置后备语义和手动F5；不增加自动完整刷新。
4. 真实四窗格、分组、大小排序、突发多项、名称变更、会话切换与重启的完整App验证；原型单视图通过不能替代。
5. 原生重新枚举/零行采样、可见闪烁及长期内存验证分别记录。当前计数证明本次未重新枚举，不证明所有窗口呈现已完美。


## 2026-11-06 枚举类型来源与视图销毁验证

试验接口改为只接收叶名，使用新试验IID避免旧原型ABI误用。`src/.state/projected-observed-kind-before.log`在旧原型DLL上获取新能力失败；实现后`src/.state/projected-observed-kind-large.log`的1029行子断言通过。类型来自现有EnumObjects的SyntheticChild属性，经共享存储传递到UI目录实例；没有在删除路径上重新枚举、也没有把未知项猜为普通文件。

记录按物理根引用计数，只为有原生视图的根保存。相同根由Shell临时枚举对象/目录对象共享记录，根最后一个视图销毁时释放。当前临时挂在既有共享snapshot-owner中，字段和方法均只在试验宏下存在，与大小缓存淘汰分开；是否接入产品及是否提取独立所有者要随接线评审决定，不增加普通构建缓存。

`src/.state/projected-observed-kind-lifecycle.log`验证：初始1根/1029条记录；删除目录和文件后1根/1027条；未观察项目返回ERROR_NOT_FOUND；调用前已重建的目录返回S_FALSE。窗口销毁后，在仍持有COM能力引用的情况下读取到roots=0、facts=0，列表HWND失效，旧能力拒绝通知（CO_E_OBJNOTCONNECTED）。证明本次逻辑记录和视图退休，不等同进程Working Set或提交大小已经下降。

大目录测试仍保留旧行基线、选区/焦点和EnumObjects计数断言。业务子项通过，前台不变但共享输入变化，isolation-clean=False，整轮不是全绿。当前只验证初始枚举项目的类型记录；后续新增/重命名记录和“检查不存在后又同名重建”的并发问题尚未解决，仍不得启用产品接线。


补充通知确认边界：SHChangeNotify返回不等于行已移除。最终`src/.state/projected-observed-kind-ack.log`验证发送后类型记录仍在；测试观察实际行移除后才通过独立确认调用退役记录。之后同样验证关闭时零记录及旧能力拒绝。此确认目前由隔离探针完成，产品的确认/世代机制仍未接线，尤其不能把旧确认误用于同名新项目。

后台回归报告`src/.state/background-checks/20261106-162343-0ed9ec4115f348e5b373fbf5bf3eff4d/summary.json`：36项中35项通过，publish-isolation-smoke在自己的临时fixture重命名staging目录时访问被拒绝，整体Passed=False。该夹具按脚本finally自动清理，未取得占用者证据，原因未确定。单独重跑`scripts/publish-smoke.ps1`成功，日志`src/.state/projected-kind-publish-recheck.log`；不改写首轮失败报告，不声称首轮36/36，也未运行真实发布。最后的试验确认协议另有上面的定向运行，普通构建恢复并检查能力排除。
