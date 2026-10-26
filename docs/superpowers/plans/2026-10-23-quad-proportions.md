# 四宫格独立分隔比例

按已授权持续路线图执行，使用 writing-plans / executing-plans / TDD；不等待新的常规实现许可。

目标：上下两排独立左右宽度，共用上下高度；完整读写 QDR SP0/SP1/SP2，保留原窗格实例。

方案：现有 Grid 派生容器只对四宫格进行专用 measure/arrange；三个 Thumb 分隔条控制上下、上排左右、下排左右。其他布局沿用原 GridSplitter。相比嵌套 Grid，此方案无需重新挂载原生宿主；相比动态重排共享列，交叉分隔位置不改变 pane 身份。

配置：LayoutProportions 加可选 BottomHorizontal，缺失继承 Horizontal。INI v4 在升级前备份 v1/v2/v3；QDR 扩展含新字段时升 v3，保留 v1/v2 读取，错标旧版本拒绝。普通窗口缩放仅钳制显示尺寸，不修改保存比例。

## 验证与执行

- [x] 首先使不相等 SP1/SP2 输入检查失败，确认现有缺口。
- [x] 实现模型、INI 迁移与 QDR 读写；非法字段不污染其余布局，旧实际 DLL 拒绝覆盖新 INI。
- [x] 实现专用四宫格几何与分隔拖动；测试两排不同宽度、三个拖动独立、取消、越界钳制、旧分隔条、窗口缩放及窗格父对象保持。
- [x] 构建、完整 Settings smoke、独立代码审查和文档更新。

最终检查：审查提出的 Thumb 越界回拉问题先由回归测试复现，再修复并通过；App Release 构建 0 警告/0 错误，完整 Settings smoke 通过，修改前 v3 实际配置读写器拒绝覆盖 v4。GUI 与 Q-Dir 互操作仍待验收。

边界：无 GUI、无桌面输入、不触碰日常 profile、不发布或提交；真实拖动/焦点/高 DPI/Q-Dir 读取待用户后续验收。
