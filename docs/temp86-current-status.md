# temp.86 当前进度与剩余验收

更新：2026-11-05。六项目标仍以 `temp86-goals.md` 中的原始清单为准。本表汇总当前证据，不把源码接线、后台合同、真实 IPC、隔离视图及可见 UI 混为一层。

| 目标 | 当前已有证据 | 尚需完成的验证 |
| --- | --- | --- |
| 自动软刷新 / 手动 F5 | 缺省开启、显式关闭保留、自动逐项更新与手动完整枚举分路的后台合同通过；原生视图真实文件变化及 F5 有独立证据 | 完整 App 用户目录外部创建/删除/重命名、黑屏间隔、选择和键盘焦点，按 temp.80 同目录对比 |
| 地址下拉与最近地址 | 合并入口、隐藏独立历史按钮、地址就近定位与图标模型已接线；导航菜单合同通过 | 重复点击保持展开、主/子菜单定位、真实鼠标/键盘与 DPI；导航按钮右键历史和 F4 树/编辑分别验证 |
| 常用位置与收藏夹 | 用户目录/磁盘/收藏夹入口，树形单层分组管理、顺序、空组、INI v3 迁移备份/冲突保护合同通过 | 真实树形拖动/跨组移动、重命名/删除、空菜单、深浅色、完整 App 跨重启 |
| 标签栏 | 顶部 3 DIP 强调线、右侧两个 28 DIP 溢出按钮、默认空白、隐藏编辑/新建/关闭冗余控件；未挂载生产控件布局及复制合同通过 | 实际溢出/放大/DPI、多行与隐藏模式、复制标签及鼠标拖动、Tab/Shift+Tab 的真实焦点 |
| 文件夹大小 | 默认显示/来源、标准大小列、未知与零、扫描/缓存/取消、每标签分类或混合排序、INI/QDR、隐藏列/分组/重入；常规 DLL 四窗格 × 1,025 行、4,100 个大小显示格、选择/焦点整项通过 | 完整 App 选项切换和跨重启、真实菜单/文件操作、长期内存增长、用户可见首次排序/滚动表现 |
| 目录空间分析 | 显式扫描/取消、来源失败后备、排名/占比、树图准备与下钻数据、关闭清理的源码及后台合同；占比行底色已接线 | 可见列表/树图联动、下钻/返回/定位标签、拖动缩放/主题、关闭重开内存、完整 App 的真实 Everything 来源切换 |

## 新补齐的真实 Everything 证据

- `src/.state/temp86-everything-live-fixture.log`：独立客户端通过真实 IPC 获取三个新目录的 0/5/37 B；精确路径查询通过，约 1,088 ms，前台不变。
- `src/.state/temp86-everything-native-live.log`：常规原生 DLL、真实 Everything 数据、四个 ExplorerBrowser 视图；独立同名目录、12/25 KB 和空目录、失效后 40 KB 补值、F5、列重排、数值排序、选区/焦点、物理通知和分组往返通过；四个列表 HWND 全部销毁，隔离守卫通过。这一用例链接产品源码，但不是生产 App 程序集中的 HwndHost。
- `src/.state/real-everything-app-hosts-final.log`：新 opt-in 用例加载实际 `ExplorerDirs.App.dll`，调用其 Profile Owner、Everything 客户端、大小桥和四个 ExplorerBrowserHost；新建独立夹具先等待真实索引就绪，随后验证独立大小、排序、选区/焦点、导航及分组/筛选路径；并观察到实际大小桥缓存 4 个父目录页；整项与隔离/清理守卫通过。
- 未修改 Everything 配置、未启动搜索窗口、未关闭服务。真实服务可用链路与不可用/pending 注入证据分别保留。仍未在完整 MainWindow 中演练外部服务故障，不将本节写成全部 UI 验收通过。

## 当前回归基线

产品源码最近修复为 `2040aa3`。完整后台报告 `src/.state/background-checks/20261105-051947-7c44c25223d6440290beeb7a5a8ab8ad/summary.json` 为 30/30；报告保留提交前的实际源码/脏状态，不是发布产物证明。大目录通过日志为 `src/.state/timer-rounding-grouped-1025.log`。本次新增真实 IPC/生产程序集用例另有定向执行记录，不放入默认后台白名单，避免总检依赖用户外部服务。

## 后续顺序

1. 修正验收清单中的旧导航/快捷键说明，加入文件夹选项、分组启动及 20,096 B 舍入夹具；仅准备隔离产物，不启动窗口。
2. 集中进行完整 App 的菜单/焦点、四窗格、自动变化与 F5、两种主题、旧配置和跨重启验收；明确记录无法可靠观察的时序项。
3. 补实际运行期间内存观察和必要的稳定性修复；本机单次规模通过不代表长期稳定性或与 Q-Dir 性能持平。
4. 六项目标和相关回归验收完成后，按用户要求覆盖同一 temp.86 目录，保留日用 profile 及可恢复旧载荷，记录版本/提交/哈希。当前未发布。

哈希/NTFS 链接工具、放大镜/托盘及后续窗口工具栏布局仍按已确认的下一版本文档管理，不加入本次交付范围。

## 完整进程恢复补证（2026-11-05，未通过）

新增显式 NativeProbe `--full-app-session-private-desktop <preparedRun>`：复制已准备载荷到独立 profile，种入五标签/四窗格及独立排序、分组，计划核对正常关闭后的实际 INI 重写和第二进程窗口位置恢复。失败只清理本探针创建的进程及经产品所有权校验的专属 Shell 注册，保留转储和夹具。

`src/.state/full-app-session-fourth.log` 与 `full-app-session-fifth.log` 已确认四个真实列表各能读到两个目录，但预期分组未启用，部分列表存在 0→2 的反复变化。后者将观察窗口延长至 60 秒仍超时，不能归因于原 30 秒观察边界。隔离守卫、前台和输入不变、专属注册清理均有日志。尚未通过正常关闭/第二进程恢复阶段；不能宣称完整 App 跨重启已通过。下一步需分离真实分组状态恢复、初始排序恢复及探针读取时序，定位后再修产品。该探针是显式诊断入口，未加入默认后台成功门槛。

## 完整 App 分组恢复修正（2026-11-05）

单变量对照 `src/.state/full-app-hard-refresh-control.log`：同一旧载荷仅在测试 profile 关闭软刷新，分组即启用，随后停在会话未重写检查；该对照不是整项通过。根因接线：`FinishGroupedRecovery` 的 `Refresh()` 走自动软刷新，无法执行它所需要的完整枚举。修复新增内部 `forceFullEnumeration` 参数，只在保存分组首次物化时暂时绕过 NOENUMREFRESH，复用现有恢复 flag/退休视图守卫，不标记为用户 F5，也不改变产品默认软刷新。

新载荷 `src/.state/gui-smoke/20261104-220239-547-dfd365` 含未提交修正，仅供隔离验证，未发布。`src/.state/full-app-group-restore-fixed.log` 整项通过：默认软刷新，两次实际 App 进程（41972/73168）均正常退出0、实际重写会话，五标签/四窗格的路径、模式、列、排序、混排和分组保持；第二进程恢复第一进程调整的 native 窗口位置和大小。专属注册清理及前台/输入隔离守卫通过。此证据补齐该夹具的完整进程恢复，不覆盖鼠标菜单、DPI、实际可见闪烁或长期内存验收。

修正后整轮后台验证通过：`src/.state/background-checks/20261105-060414-50cb09d41a39435c88993201972e3acb/summary.json`。首轮旧源码字符串合同失败，随后更新合同以保留对内部完整枚举例外及表现层边界的检查，再重跑整轮；没有将失败首轮计作通过。

## 完整 App 外部目录变化对照（2026-11-05，删除未通过）

扩展完整进程探针：在四个当前标签的专属夹具目录中从外部创建含64B文件的新目录、等待所有列表2→3，再删除文件/目录等待3→2，同时检查分组保留；两次独立进程均执行。只改测试根目录，不注入输入。日志逐状态记录条目数及空列表采样；采样不等于可见闪烁验收。

- `src/.state/app-external-change-diagnostic.log`：已修启动分组的载荷，默认软刷新，四窗格创建约951ms全部补入且分组保留，删除后全部停在3项，15秒超时。
- 尝试保留软刷新批次预期差量，`src/.state/app-external-delete-fixed.log` 删除仍失败，未解决实际问题；该产品/单测尝试已撤回，不作为修复交付。
- `src/.state/app-external-delete-hard-control.log`：同一对照载荷显式关闭软刷新，两次完整进程均创建/删除更新成功，分组/会话实际重写/窗口位置恢复及隔离清理全部通过；第二进程删除约363ms。不能以完整刷新对照代替默认软刷新目标。

当前发布门槛新增明确反例：软刷新下的真实外部目录删除没有逐项移除。下一步定位 native backing→provider 删除身份通知以及托管删除补偿路径，不通过自动完整枚举掩盖问题。预览不覆盖。

后续删除对照：`app-delete-pidl-only.log`（条目绝对PIDL通知）、`app-delete-pidl-and-delta.log`（PIDL+保留差量）均仍删除失败，两项产品尝试均已撤回。原产品载荷的 `app-delete-settle-control.log` 在实际视图就绪后再等待3秒才执行外部变化，同样删除失败，排除仅因探针紧接启动操作的解释。保留显式 `--settle-external-changes` 对照参数，默认路径仍不等待，避免削弱原有启动边界覆盖。下一步需要直接获取通知阶段证据，不把这些反例记录为已修复。

## 通知链阶段证据（2026-11-05）

新增独立 `build-product-shell-folder-adapter.ps1 -NotificationTrace`，输出专属 `native-product-x64-notification-trace`，保留普通产品编译参数/导出，仅在诊断编译中启用既有有界日志，并筛选物理事件、provider转发和FSNOTIFY回调。完整进程探针支持显式 `--adapter <dll>`，仅复制到新测试副本，记录实际覆盖DLL SHA256；创建/删除阶段写入同一boot tick时间轴，不更改已准备载荷。

`src/.state/app-delete-phase-trace.log` 仍复现删除超时；对应 `src/.state/app-session-process/e4f0ffcd69f1408896ca7d4f097588b2/app/ExplorerDirs.ShellNamespace.dll.size-trace.log`：before-create=375839375；随后四个实例均记录物理UPDATEDIR(0x2000)、provider转发及FSNOTIFY回调；before-delete=375840312，此后直到超时未记录新的物理/provider/回调事件。无逐项RMDIR。因而本次不是已经收到逐项删除后仅行身份匹配失败；需要继续观察托管watcher和补偿计时器是否启动/收到删除/执行。先前“删除通知被合并为UPDATEDIR”的推断尚不能由该日志证实：已抓到的UPDATEDIR位于创建阶段，不把其误归到删除阶段。

诊断DLL SHA256=288101F17E0A284920DC6E08446E1F51B6C00F7329473EF7ECADF536CAE99A3E。该产物不是正常产品验证或发布；普通产品DLL未被诊断构建覆盖，测试隔离清理通过，默认软刷新删除目标仍未完成。

## 软刷新目录删除修复与普通构建补证（2026-11-05）

临时显式诊断编译直接记录真实MainWindow四窗格字段，已在最终源码移除。原始 `app-managed-monitor-run.log` 对应 `f86b907e07134aae9132c81f2e78a76f/app/profile/monitor-probe.log`：四watcher均收到delete=1，但lookup=0，旧计数被重设为baseline=3。保留未完成差量后，`app-managed-stage-run.log` 记录 lookup=1，具体失败为残留目录的 `SIGDN_FILESYSPATH` 显示名 E_INVALIDARG；provider解析名称后 `app-provider-name-run.log` 前进到 Folder 属性 ERROR_FILE_NOT_FOUND。故早期差量/PIDL单项尝试不足，不是这些环节都正确。

最终修复：
- 自然更新仅在实数等于预期时完成批次，保留待处理差量。
- 软刷新按已知删除路径进行有界精确匹配，不依赖初始计数采样完成或正数目标；保留原512行查找上限。
- 物理显示名不可读时，按本profile provider解析名称还原物理路径；不把其他namespace字符串当路径。
- 原生provider仅在文件/路径不存在且请求恰为SFGAO_FOLDER时，返回保留PIDL内的已知类型。不吞访问拒绝、其他属性或VALIDATE请求。
- 用视图残留条目的绝对PIDL发RMDIR，保留每标签root身份；及时释放PIDL和COM引用，仍无自动完整枚举。

诊断构建 `app-delete-identity-run.log` 两进程通过，但首轮普通构建 `app-delete-normal-run.log` 发现分组窗格初始计数尚未完成而跳过修复，不能计为最终成功。移除该软刷新计数门槛后，正常无诊断定义、正常产品DLL的载荷 `src/.state/gui-smoke/20261104-222921-822-c088e4` 经 `src/.state/app-delete-startup-normal-run.log` 两进程全部通过（33732/13148）：四窗格创建/删除、分组、五标签会话实际写回、窗口位置恢复、清理/隔离守卫。第二进程目录删除857ms；该阶段未采样到空列表，不能代替可见黑闪验收。

仍需扩展普通文件/重命名、多删除突发、同名重建、超过512行和网络目录的真实自动更新验证；当前小夹具通过不代表这些边界完成。临时托管监测定义及计时器已清除，诊断编译对照产物仅作历史证据，不发布。

最终本批后台报告：`src/.state/background-checks/20261105-063021-695a33910edf42e5ad734e3c416177e6/summary.json`，整轮成功；新增测试覆盖自然更新延迟计数及软刷新初始计数未就绪/最后一项/查找上限。未把诊断构建成功代替普通构建结果。

## 普通文件删除补证（2026-11-05）

完整进程夹具在目录创建/删除后增加四窗格各一个37B普通文件的外部创建/删除，不改变默认软刷新。`src/.state/app-external-files-first.log`：创建全部补入，删除全部残留，证明仅修文件夹不足。现有删除补偿拒绝所有非文件夹；扩展为按视图保留类型发送SHCNE_DELETE或SHCNE_RMDIR，继续使用相同绝对PIDL及有界查找，未知类型失败不冒充目录。

正常产品载荷 `src/.state/gui-smoke/20261104-223321-901-05027a`，`src/.state/app-external-files-fixed.log` 两个独立App进程38124/69692全部通过：目录与普通文件的外部创建/删除、分组、五标签状态写回、窗口位置恢复、隔离/清理。第二进程文件删除895ms，阶段未采样到空列表，不等同可见闪烁验证。此次仍为每窗格单条变化；多路径突发及大目录边界待继续。

普通文件删除扩展整轮后台回归30/30通过：`src/.state/background-checks/20261105-063445-6fda1abc8d2e41868cc5e702ae64ed5b/summary.json`。

## 多文件删除突发修复（2026-11-05）

在上述完整进程场景中增加每窗格连续创建、删除3个普通文件。`src/.state/app-file-burst-first.log` 删除后四窗格均停在4项而非2项：仅处理最后路径留下前两个文件，属于现有合并器语义遗漏，不是Shell排序问题。

修复保留一批有界、大小写不敏感去重路径，250ms静默期后一次消费；不恢复逐事件dispatcher排队。最多保留最新512个不同路径，超量边界仍需后续方案，不能声称无限突发都覆盖。删除助手对整批只获取一次Shell条目数组，单轮扫描按路径匹配并使用真实类型/绝对PIDL通知；部分成功单独返回PartiallyNotified，避免一个不可读条目阻止其他匹配项。新增合同覆盖多路径保留、重复事件、单次handoff及容量边界。

正常载荷 `src/.state/gui-smoke/20261104-223739-069-c68dd7` 的 `src/.state/app-file-burst-fixed.log` 两进程48196/35864全部通过：各窗格目录、单文件和三文件突发创建/删除，分组、五标签设置实际写回、窗口恢复、隔离和注册清理。两轮批量删除948/935ms，阶段未采样空列表；仍不替代可见刷新验收。此载荷早于最终统计计数对PartiallyNotified的覆盖修订；该修订另随最终整轮构建/合同回归验证。

多路径修复最终整轮后台30/30通过：`src/.state/background-checks/20261105-063907-bbfba6894d8d4cb1a6d35aad04453aab/summary.json`。未覆盖任何预览目录。

## 同名重建边界首轮对照（2026-11-05）

完整进程探针在既有目录/单文件/突发用例后，创建37B文件，立即删除并同路径写入83B文件；额外等待2秒使延迟删除静默期结束，再要求四个列表各3条、实际文件长度83B，最后真正删除并要求各2条。`src/.state/app-same-name-first.log` 两个独立进程58176/58436均通过原有所有阶段、重建后的最终条目数、内容长度、最后删除、分组/会话/窗口位置与隔离清理。

本轮无产品修复。此证据证明该时序下最终没有丢行或改磁盘内容，不证明列表行已经展示83B，也不证明没有中间短暂消失。下一步需要读取实际原生行文本，以覆盖新大小/重命名而非仅靠计数；同名换类型、大目录等仍待验证。

## 实际原生行内容与重命名验证（2026-11-05）

新增NativeProbe专属 `OwnedAppListText`：只接受本探针创建的子进程句柄和同PID列表，通过跨进程LVITEM数据缓冲读取名称/大小；不注入输入或代码。成功调用释放缓冲；超时不提前释放可能仍被接收端访问的指针，随已有失败清理结束专属子进程。普通产品没有这条读取路径。

初轮 `app-native-row-first.log` 因测试预期不正确失败：37B普通文件沿用Explorer的KB显示，实际单元格是1KB，不能用37B→83B区分元数据。夹具改成37KB→83KB，保持产品格式不变。最终 `src/.state/app-native-row-final.log`（构建0警告/错误）两进程53724/68116完整通过：四个窗格分别读到37KB种子、同名重建后的83KB、外部重命名后的external-file.txt.renamed及83KB，并明确拒绝旧名称残留；按每个窗格单独匹配，不能用别的窗格补足总数。原有目录/单文件/突发创建删除、分组、实际会话保存、窗口恢复、专属注册清理和前台/输入守卫均通过。普通产品载荷仍为223739-069-c68dd7，未发布。

本批只改探针，无产品行为修复。该证据证明真实原生文本查询结果更新，不能替代可见重绘时序或中间闪烁验收，也不覆盖大目录/同名换类型/网络路径。

## 大目录自动删除门槛与名称查找对照（2026-11-05，未通过）

新增显式完整进程参数 `--filler-count 513`：每个目录加513个1KB普通文件，基础515项；创建新目录516项，删除应恢复515项。该独立大目录用例只覆盖目录通知和进程恢复，不冒称执行了普通小夹具全部行文本场景。测试仍每次复制正常已准备载荷到新profile，隔离失败清理保持。

`src/.state/app-large-delete-first.log` 复现正常产品超过512行时目录删除未全部同步，已知有界扫描门槛不能作为完整功能交付。随后尝试利用LVM_FINDITEMW按名称找候选，再校验Shell完整身份而不逐行绑定：`app-large-find-diagnostic.log` 的直接跨进程查询确认残留目标可返回索引515/0等，并非该原生名称查找一概不支持。

独立诊断编译 `app-large-find-trace.log`：IFolderView2.GetItem候选名称匹配，但已删目录的SFGAO_FOLDER返回S_FALSE；`app-large-find-array.log` 改用既有IShellItemArray候选，类型返回S_OK和0x20000000，准备了RMDIR(0x10)，仍未全部移除，未形成有效修复。对应run的delete-lookup-probe.log保留候选/身份/类型的阶段证据。不能把“找到行/通知已准备”写成实际删除通过。

未通过的两个产品文件改动已撤回，临时诊断编译定义没有保留到源码；尝试diff留在 `src/.state/app-large-native-find-experiment.patch` 供定位对照。NativeProbe保留只查询本任务子进程的名称查找能力及大目录反例。正常产品源码行为仍为已验证的小视图修复，大目录默认软刷新自动删除继续开放；不覆盖预览。

## 大目录通知后半段与分段实验（2026-11-05，保持开放）

继续原生名称查找对照时，新增身份诊断误把SDK内联包装ILCloneFull当成DLL导出，导致诊断在通知前抛异常。现场NativeLibrary导出核验和SDK头文件确认ILCloneFull包装ILClone，已纠正；该错误仅在未发布诊断源码/产物中。`app-large-retained-fixed-run.log` 两进程通过，但对应实例没有生成托管查找日志，不能据此证明修复走到了托管路径。SHGetIDListFromObject实测返回S_OK；此前缺少后续日志不等于PIDL获取失败。

移除临时诊断后，普通构建 `app-large-normal-clean-run.log` 的515项两进程目录通知通过；扩大场景 `app-large-burst-1025.log` 在后续普通文件创建失败。关闭测试端额外LVM_FINDITEM查询（默认不再查询；只有显式adapter诊断才记录）后，`app-large-no-observer-513.log` 目录删除仍失败。因此原生名称查找方案有观察时序依赖，已撤回，不算产品修复。

另作有界分段实验：沿用身份匹配算法，每32行或约8ms让出Dispatcher，按监视代际取消、同代际单个worker串行，整批收集身份后再通知。普通构建 `src/.state/app-incremental-delete-513.log` 目录删除通过，但普通文件删除超时，未达到整项标准；未声称早退/中途取消的原生生命周期已验证。下一步区分分段时条目数组变化、任务退休及最终通知结果。

完整未通过实现与合同尝试保存在Git跟踪的 `src/ExplorerDirs.NativeProbe/experiments/incremental-delete-repair.patch`，未接入默认编译；默认App和SettingsSmoke已恢复已验证实现。不降低成功标准，不发布预览。NativeProbe保留大目录目录/文件/突发用例与显式大目录adapter组合诊断入口，默认关闭额外名称观察。

## 失败行清单与旧目录重新加入证据（2026-11-05）

NativeProbe在断言超时之后才读取最多2048行名称，打印非filler条目及前后计数；不参与等待条件、不重试断言，避免观察把失败变成通过。已完全移除自动名称查找观察，即使显式adapter诊断也不自动调用LVM_FINDITEM。

`app-incremental-stages-run.log` 的分段数组实验首进程目录/文件/突发阶段通过，第二进程后续创建失败。`app-large-inventory-run.log` 捕获数组扫描ItemNotFound时，视图仍有external-notification-fixture；任务日志current=True，不能归因为任务退休。后续改为IFolderView.Item原始PIDL、所属IShellFolder的缓存解析名称和类型，避免重建IShellItem；`app-raw-view-delete-run.log` 四窗格均找到原始目录身份并通知，仍有残留。原始视图分段版本保存在Git实验补丁 `src/ExplorerDirs.NativeProbe/experiments/raw-view-incremental-delete.patch`，未接入默认编译。

单变量原生枚举标志对照：只让实际NOENUMREFRESH保持关闭，应用软刷新策略仍开启。`src/.state/app-native-reconcile-control-1025.log` 先记录目录删除恢复1027项，随后文件创建等待1028项失败；失败行清单显示新external-file.txt已存在，多出的却是前一步已移除的external-notification-fixture（两窗格1029项）。因此“文件创建没同步”的计数表象实际包含旧目录重新加入。再撤销缺失目录的SFGAO_FOLDER回退，`app-native-reconcile-missing-1025.log` 仍重复该表象，不能把标志或类型回退当作已证实的完整根因。

两项原生/标志控制均已撤回；正常产品保持此前已验证源码。下一步追踪迟到的枚举输出和大小更新通知，与删除/重新出现阶段对齐。标准CompareIDs文档核对及诊断只观察到CANONICALONLY/name比较，未据此修改比较器。现阶段不再把“发出RMDIR”当成行最终移除完成证据，不发布预览。


## 大目录已删除条目复现的通知身份证据（2026-11-05）

`src/.state/app-notification-identity-run.log` 完整 App 四窗格×1,025 filler 失败于 file-create；隔离及输入保护通过。专用诊断 trace 位于 `.state/app-session-process/6927ac257bef4a258f9aa276c9dc6259/app/ExplorerDirs.ShellNamespace.dll.size-trace.log`。tick381371453 四窗格转发已删除目录的 RMDIR；tick381374046/381374062 又转发同名不存在条目的 UPDATEITEM，构造属性变成 FILE_ATTRIBUTE_NORMAL。随后旧目录行复现。该证据支持调查延迟更新通知，不等同修复已验证。

Windows SDK 常量核实：SHCNE_UPDATEDIR=0x1000，SHCNE_UPDATEITEM=0x2000；此前调查中将原始 0x2000 称作 UPDATEDIR 的描述有误，应按本次更正解释。此次仅诊断编译增加叶名称/存在状态，普通产品行为未修改。下一步对照不存在条目的更新通知处理，保留删除/重命名旧身份语义，禁止自动完整刷新回退。


## 延迟 UPDATEITEM 候选处理（2026-11-05，整项未通过）

仅当 UPDATEITEM 的物理直接子项查询返回 FILE_NOT_FOUND/PATH_NOT_FOUND 时，停止构造/转发该过期更新，且不回退为目录刷新。删除、重命名及访问失败行为保留。诊断DLL构建通过；`src/.state/stale-update-run.log` 同一1025行完整App对照通过目录删除、单文件创建/删除及三文件创建，旧目录未再出现，但三文件删除全部残留，整项失败。所用App仍为NoEnumRefresh关闭控制载荷，不替代默认产品验证；过程中存在空列表采样，不符合最终视觉目标。

当前源码删除补偿明确跳过大于512行的视图，下一步需结合原生批量删除通知追踪和有界异步身份查找处理；不得通过扩大同步扫描上限或自动全量刷新掩盖问题。候选尚未正常产品/同名重建回归，不发布。


## 默认产品构建对照（2026-11-05）

正常 PrepareOnly 重新构建（不是 NoEnumRefresh 关闭控制载荷），产物 `src/.state/gui-smoke/20261104-235433-338-1b0daf`。`stale-update-default-run.log` 在默认软刷新开启下同样前进到三文件批量删除失败，确认剩余缺陷不是控制载荷专有现象；期间存在空列表采样，视觉目标未达标。

`stale-update-default-small.log` 正常DLL、无诊断覆盖的小目录完整App两进程64084/59408全部通过：目录/普通文件/三文件突发变化、同名37KB→83KB重建实际单元格、重命名、五标签四窗格保存、窗口位置恢复、注册清理及前台/输入隔离。该结果补齐延迟UPDATEITEM修正的正常产品小目录回归，不代表1025行删除通过。上一轮诊断中只见单文件DELETE，批量删除缺少逐项DELETE；需继续验证合并通知与512行补偿上限的衔接。


## 异步原始PIDL删除补偿组合对照（2026-11-05，未采用）

在默认产品基础上临时应用已保存 `raw-view-incremental-delete.patch`，与延迟UPDATEITEM过滤组合；新载荷 `.state/gui-smoke/20261104-235727-676-af8234`，测试 `combined-delete-run.log`。第一进程大目录创建/删除、单文件及三文件突发阶段通过并完成会话保存，但第二独立进程普通文件删除仅一窗格成功，其余三个残留。过程中仍采样到空列表。故原型不具稳定性，不能以首进程成功激活产品；三个原型接线文件已恢复提交基线，保留原实验补丁。

下一步核对删除事件PIDL与视图身份的比较/生命周期，以及通知期间异步枚举的交错；异步扫描仅缓解UI占用，不证明发送的删除身份能被原生视图正确消费。此轮隔离进程已终止并完成所有权清理，不覆盖预览。


## 删除身份比较定向追踪（2026-11-05）

`delete-compare-run.log` 默认App+显式诊断DLL，1025行批量删除仍失败，隔离清理完成。trace目录 `45a534ee898942209721b73d3cc74377`：同名external-file.txt的37B旧PIDL/0B删除PIDL比较使用code=0x10000000（CANONICALONLY、列0），当前代码走名称比较，而非大小；未观察到“删除身份因大小不同而比较不等”的证据。只在同名external条目元数据不同时记录，常规构建不启用。故不据假设修改CompareIDs；继续追踪批量通知缺失及异步补偿的真实返回。此轮仅增加诊断，未发布。


## 删除补偿阶段结果（2026-11-05，诊断通过非稳定性结论）

组合原型仅在两个App文件顶部临时启用DELETE_REPAIR_TRACE，`.state/gui-smoke/20261105-000429-338-a25729`；`combined-delete-stages-run.log` 两进程56428/6340完整通过。阶段文件 `.state/app-session-process/8ce9ab9121fb4ddd9f9a0db46acdbaae/app/delete-repair-stages.log` 显示四窗格找到全部三条保留PIDL、属性读取成功、notified=3且owner current=True。故不能笼统认为原始PIDL助手不能匹配；但上一无诊断轮失败，当前日志写入可能改变时序，不作为产品稳定证据。

第二进程单文件删除3.632秒，累计198次空列表采样，自动更新体验不达标。全部临时原型/诊断产品接线已恢复，保留现有实验补丁；不发布。下一步应分离通知消费/异步枚举完成与补偿发出时序，并追踪导致空列表的原生刷新路径，而非将本轮一次PASS升级为最终修复。


## 根条目更新过滤负对照（2026-11-05）

临时过滤物理backingRoot自身UPDATEITEM转发，保留缓存失效和子项转发，并组合异步删除原型；载荷 `.state/gui-smoke/20261105-000718-231-9aab6c`，`root-update-filter-run.log` 第一进程批量阶段通过但累计127次空列表，连续创建耗时6.188秒；第二进程再次单文件删除失败。该负对照不支持将根UPDATEITEM过滤作为修复，全部临时接线及过滤均已撤回。下一步给实际IShellView.Refresh调用增加仅诊断来源标记，区分宿主主动完整刷新与DefView内部枚举；不把计数归零直接当成宿主调用Refresh的证据。


## 实际宿主 Refresh 来源（2026-11-05）

临时显式编译EXPLORERDIRS_REFRESH_ORIGIN_TRACE，标记真正调用ShellRefreshPolicy/IShellView.Refresh的位置和堆栈。载荷 `.state/gui-smoke/20261105-000944-468-896213`，`refresh-origin-run.log` 1025行仍在普通文件删除失败且多次空列表。`.state/app-session-process/f268364ed4a448b3b66d3387756b42d2/app/refresh-origin.log` 全程仅1条：tick382513187，manual=False/full=True/soft=True，来自ScheduleInitialVirtualSort.FinishGroupedRecovery启动分组恢复；外部变化期间没有新增宿主Refresh调用。

因此当前反例不支持“监控反复调用这条宿主Refresh路径导致空列表”的判断；后续定位DefView通知处理/内部枚举，并检查是否存在其他刷新入口。保留条件编译诊断块，文件顶部临时#define已移除，普通构建不写日志；不将该结论扩大为所有刷新入口或可见绘制均已排除。不发布。


## 名称排序控制夹具（2026-11-05）

NativeProbe增加显式环境控制EXPLORERDIRS_PROBE_NAME_SORT_CONTROL=1，仅改变测试种入的排序列为名称，保留四窗格、大小列及一窗格Size分组；未设置时原夹具不变，产品不读取该开关。构建通过。`name-sort-control-run.log` 默认软刷新/正常DLL/1025行：创建1209ms、未采样空列表，但目录删除四窗格全残留，整项失败并隔离清理完成。故删除缺陷不是大小排序特有；创建阶段无空列表不能证明后续所有变化无闪烁，控制测试在目录删除阶段已停止。后续需独立处理通知删除可靠性和大小排序/枚举时序，不用关闭大小排序规避需求。


## 原生 RemoveObject 定向移除对照（2026-11-05，未采用）

本机SDK ShlObj.h声明ShellFolderView_RemoveObject通过SHShellFolderView_Message(hwnd,SFVM_REMOVEOBJECT=6,relativePidl)。在异步原始PIDL原型中改为QI IShellView/GetWindow，以保留行PIDL末段调用该入口，不做文件系统删除。`direct-remove-prepare2.log` 构建通过，载荷 `.state/gui-smoke/20261105-001602-689-61ff86`；`direct-remove-run.log` 仍在批量删除失败且多次空列表，不能认为旧接口有效解决当前DefView场景。实验保存为 `experiments/direct-view-remove.patch`，产品三文件接线已恢复。

首次准备因旧零上下文补丁插入方法落在新增诊断行后的错误位置而编译失败，修正方法边界后才构建/执行；失败准备没有运行。不将SDK仍声明该旧接口等同于现代DefView兼容性证明。下一步回到通知/枚举交错，不接入该替代方案。
