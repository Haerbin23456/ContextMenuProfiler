# Phase A - 止血与可验证（执行中）

## 目标
- 保证“分析文件”路径不再退化为系统扫描。
- 建立最小但稳定的本地质量门禁，减少人工回归循环。
- 在不破坏现有功能的前提下，清理明显的试探性/硬编码实现。

## 已完成
- [x] 新增本地一键验证脚本 `scripts/verify_local.bat`。
  - 顺序执行：Hook 构建（验证模式）→ `dotnet build` → `QualityChecks`。
- [x] `scripts/build_hook.bat` 支持 `CMP_SKIP_ROOT_COPY=1`。
  - 避免 Explorer 占用根目录 DLL 时，验证流程被误判失败。
- [x] 扩展 `ContextMenuProfiler.QualityChecks/Program.cs`：
  - IPC 请求构造/JSON 包络解析校验。
  - 中英文本地化 key 完整性与关键 key 非空校验。
  - “分析文件语义守卫”：断言 `BenchmarkService` 保持按路径分析逻辑。
- [x] 清理 `BenchmarkService` 中冗余分支：`mode == ScanMode.Full ? false : false`。
- [x] 清理 `DashboardViewModel`/`LoadTimeToTextConverter` 中关键硬编码文案，统一接入本地化。

## 下一批（进行中）
- [ ] 将更多“字符串状态判断”收敛为类型化状态（先从 UI 展示层开始）。
- [ ] 补充可重复的手动 Smoke 清单（与自动验证互补）。
- [ ] 审核 Hook IPC 的请求/响应边界与错误码一致性（为后续协议类型化做准备）。

## 回归命令
- `scripts\verify_local.bat`

## 说明
- 当前阶段优先“可验证”和“防回归”，暂不做大规模架构拆分。
- 任何改动需保证上述回归命令通过。
