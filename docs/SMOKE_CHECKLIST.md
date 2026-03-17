# ContextMenuProfiler 手动冒烟清单

## 0) 执行前准备
- 以普通权限执行：`scripts\verify_local.bat`
- 预期：输出 `Verification passed.`
- 若第 1 步提示 DLL 被占用，这是预期场景（验证模式会自动跳过根目录复制）。

## 1) Hook 连接与状态
1. 启动 `ContextMenuProfiler.UI`。
2. 观察右上角 Hook 状态文案。
3. 点击 `Reconnect / Inject`（若当前为未连接）。
4. 预期：
   - 状态可从“未连接”转为“已连接/已激活”之一。
   - 不出现崩溃或 UI 卡死。

## 2) 系统扫描路径
1. 点击 `Scan System`。
2. 扫描过程中滚动列表、切换排序、输入搜索词。
3. 预期：
   - UI 可响应（允许有短暂忙碌，但不应长时间假死）。
   - 扫描完成后显示结果数量与总耗时。

## 3) 分析文件语义路径（重点）
1. 点击 `Analyze File`，选择一个常见文件（例如 `.txt`）。
2. 观察结果中的 `Registry` / `Location` 信息。
3. 预期：
   - 结果以该文件类型相关项为主（包括 `SystemFileAssociations` 或对应 ProgID 路径）。
   - 不应退化为“等同系统扫描”的全量结果模式。

## 4) 扩展开关管理
1. 在结果中选择一个可安全测试的扩展。
2. 切换启用/禁用开关。
3. 预期：
   - 不崩溃。
   - 状态变更为 pending restart 类提示。
   - 刷新后状态与预期一致。

## 5) 多语言显示
1. 进入 `Settings` 切换语言（中/英）。
2. 回到 Dashboard 观察状态栏与通知文本。
3. 预期：
   - 不出现明显未翻译键名（如 `Dashboard.xxx`）。
   - `None/无`、Hook 重连相关提示按语言切换。

## 6) 故障收集（若失败）
- 记录失败步骤编号（如 `3)`）。
- 附带以下信息：
  - `ContextMenuProfiler.UI/startup_log.txt`
  - `ContextMenuProfiler.UI/crash_log.txt`
  - 根目录 `hook_internal.log`（若存在）
- 尽量提供“前一步操作 + 当前表现 + 预期表现”。

---

这份清单优先覆盖高频回归点：Hook 状态、扫描路径、分析文件语义、UI 交互与本地化显示。
