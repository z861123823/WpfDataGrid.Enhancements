# WpfDataGrid.Enhancements 性能压测报告

> 压测日期：2026-10-05 21:13
> 压测代码：`tools/WpfDataGrid.Enhancements.PerfBench`（独立控制台项目，未污染正式测试）
> 数据规模：10 万行模拟订单数据（`Id / Customer / Amount / Date / Status` 5 列），伪随机种子 42 生成

## 一、测试环境

| 项目 | 值 |
|---|---|
| CPU | Intel(R) Core(TM) Ultra 9 275HX（24 逻辑核心） |
| 内存 | 32 GB |
| OS | Windows 11（Microsoft Windows NT 10.0.26300.0，64 位） |
| 运行时 | .NET 8.0.31 |
| 构建配置 | Release |
| 数据生成 | 10 万行耗时 14 ms |

## 二、AsyncVirtualizingCollection 滚动加载性能

| 测量项 | 参数 | 耗时 |
|---|---|---|
| 全量加载 LoadAll（10 万行） | pageSize=1000，内存切片数据源 | **2 ms** |
| 逐页 LoadPage（100 页异步调度 + 全部落缓存） | 1000 行/页 × 100 页 | **7 ms** |
| 缓存命中随机索引访问（10 万次） | 已加载集合随机 `collection[i]` | **4 ms** |

说明：以上为纯内存数据源（`List<T>.GetRange`）测量，反映集合调度与缓存开销；真实业务场景耗时主要取决于 `PageLoader` 数据源本身（数据库 / 服务端分页），异步 `Task.Run` 已将该开销与 UI 线程隔离。重复访问已加载行零成本（缓存命中 10 万次仅 4 ms）。

## 三、过滤响应时间（10 万行）

| 测量项 | 条件 | 耗时 | 命中行数 |
|---|---|---|---|
| FilterModel.IsMatch 逐行判定 | 客户列包含"张" AND 状态列包含"已完成" | **13 ms** | 1088 |
| ICollectionView（ListCollectionView）过滤 | 相同两个 Contains 条件（同步 Refresh + 枚举） | **3 ms** | 1088 |

说明：两列条件（AND 语义）在 10 万行上过滤均处于个位数毫秒量级。FilterModel 的 13 ms 含 `PropertyAccessor` 反射取值开销，仍可满足实时输入过滤（每键击 ≤13 ms，60 FPS 目标 16.7 ms 内）。

## 四、导出性能（10 万行）

| 测量项 | 耗时 | 文件大小 |
|---|---|---|
| CSV 导出（CsvExportProvider，UTF-8 BOM） | **83 ms** | 5,579,587 字节（约 5.32 MiB） |
| Xlsx 导出（XlsxExportProvider，Sylvan 流式 + 样式后处理） | **1231 ms** | 1,953,526 字节（约 1.86 MiB） |

说明：Xlsx 耗时大头为 Sylvan 流式写入 + OOXML 样式后处理 + zip 压缩，内存占用不随数据量线性增长（流式设计）。压缩后的 xlsx（1.86 MiB）显著小于 csv（5.32 MiB），适合归档与传输。导出文件留存于会话中间目录（`temp/perf`）备查。

## 五、对比与结论

1. **Async 虚拟化的核心价值不在"过滤更快"，而在资源占用与响应性解耦**：纯过滤计算上 ICollectionView（3 ms）与 FilterModel（13 ms）都很轻，但 ICollectionView 要求 10 万行全部物化在内存并同步全量筛选（UI 阻塞）；AsyncVirtualizingCollection 按页懒加载（100 页全量 7 ms）、缓存命中零开销，滚动只渲染可视窗口，适合 10 万+ 行且数据来自服务端/数据库的场景。
2. **过滤链路可用**：FilterModel 每键击 13 ms 在 10 万行仍满足实时交互；若列为固定集合，可进一步缓存属性访问器降低反射开销。
3. **导出可用**：CSV 10 万行 83 ms；Xlsx 10 万行约 1.2 s，均在可接受范围。

## 六、优化建议

1. **Xlsx 大批量导出**（>10 万行）：临时关闭 `AutoColumnWidth` / `StyleHeader` / `FreezeHeaderRow` 可跳过全量列宽估算与样式后处理，显著提速；样式需求可在导出后一次性美化。
2. **ICollectionView 同步过滤阻塞**：10 万行全量筛选虽仅 3 ms，但真实场景含 UI 刷新与容器重建；建议大数据视图默认 AsyncVirtualizingCollection + FilterModel（增量谓词），避免全量物化。
3. **PageLoader 异步化已达标**：如需进一步降低首屏延迟，可在服务端预取前 N 页并预热缓存。
4. **进程内基准 vs 真实负载**：本报告为纯内存测量，接入真实数据源后建议复测一次以校准 PageLoader 侧耗时占比。
*（内容由AI生成，仅供参考）*
