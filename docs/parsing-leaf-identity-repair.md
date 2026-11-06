# 解析名称与快捷方式显示名混用修复

2026-11-06，基于c9b4b97b。属于现有Shell身份接线，不是新增链接工具，也不替代真实鼠标拖放验收。

## 实际反例

投影的DescribeBackingChild原先用SHGDN_INFOLDER读取名称，再存入SyntheticChild.name。Shell的显示名称可隐藏快捷方式扩展名，因此同目录存在“快捷启动.lnk”和“快捷启动”文件夹时，显式解析前者会把后者的名称当作身份。

无窗口夹具用系统ShellLink接口保存一个真实.lnk，目标是测试BAT（不执行）；同目录建立同名无扩展目录。原生IDataObject的CF_HDROP本应返回“快捷启动.lnk”，实际返回“快捷启动”。旧版明确失败日志：src/.state/parsing-identity-before.log。该证据针对解析后产生的条目；不能外推为所有从枚举得到的快捷方式都会发生此错误。

## 修正与覆盖

保存名称时改用SHGDN_INFOLDER | SHGDN_FORPARSING，使身份保留真实解析叶名称。没有自己补扩展名，没有读取或更改用户的扩展名显示设置，没有重写拖放、复制、删除或快捷方式创建逻辑；原生IDataObject继续由文件系统目录提供。PIDL结构和INI格式不变。

`--product-parsing-identity <product-adapter>` 覆盖：

- 中文快捷方式与同显示名目录共存时，源数据仍指向.lnk自身，不是目录或目标BAT。
- 带空格的另一个.lnk没有同名目录时，解析和源数据同样正确。
- 两个快捷方式保留复制/移动/链接能力；快捷方式和同名目录canonical身份不相等。
- 本夹具指向BAT的快捷方式没有SFGAO_FOLDER，同名实际目录保留该标志。只判断所需标志位，不将Shell返回的其它属性位误当作目录。

最终扩展定向日志：src/.state/parsing-identity-expanded.log；未创建窗口、拖放循环或Shell文件操作进度界面，没有设置系统剪贴板，也没有执行BAT。此用例已加入verify-background.ps1。

真实鼠标修饰键、跨窗格和Explorer双向拖放、重命名及可见菜单仍属集中验收。源码修正不等于这些交互全部通过，也未覆盖当前预览或用户profile。

## 最终后台回归

36/36整轮通过，报告 src/.state/background-checks/20261106-144519-0172a15ed266417596565c3a9a73ca63/summary.json；包括正常/诊断DLL构建、App/HostProbe/NativeProbe/SettingsSmoke、源身份、元数据比较与既有便携配置/刷新路由等合同。此前扩展测试曾把Shell附带的其它属性位误当作FOLDER，现仅检查目标位；原始错误CF_HDROP的红灯证据独立保留。后台合同不等于真实UI、鼠标拖放、复制/删除/重命名操作完成或所有Windows版本验收。
