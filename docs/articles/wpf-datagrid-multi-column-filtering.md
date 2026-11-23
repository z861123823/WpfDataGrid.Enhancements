---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: 871839cd4a3ed3257c2e115376c7f809_86c11783e75311f184bd52540072b1e8
    ReservedCode1: 9OJpGSb7wUgmY5fTDbEKY+1PpGIzKpYr0URKI+yPoQMJElf1HIUQDEHpkaDxWGoX/oWCYiCTvrcBg7B/7l8TyUiZAnbHzP6kzhld7uzRk6o49n2rgkUtm70qKq5zhmiVXtRxOtFdvq9T0Q+9u8NOvMeJY5gkdEMRE6HrPlxXtKtAlQeJjKQ8gqMp1Pw=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: 871839cd4a3ed3257c2e115376c7f809_86c11783e75311f184bd52540072b1e8
    ReservedCode2: 9OJpGSb7wUgmY5fTDbEKY+1PpGIzKpYr0URKI+yPoQMJElf1HIUQDEHpkaDxWGoX/oWCYiCTvrcBg7B/7l8TyUiZAnbHzP6kzhld7uzRk6o49n2rgkUtm70qKq5zhmiVXtRxOtFdvq9T0Q+9u8NOvMeJY5gkdEMRE6HrPlxXtKtAlQeJjKQ8gqMp1Pw=
---

# WPF DataGrid 多列组合过滤的实现：Behavior + 附加属性方案

> 一行 XAML 打开过滤能力，列的过滤条件可组合、可编程控制、可拦截，全程不修改 DataGrid 的继承结构。

## 一、先说痛点

在 WPF 里做业务表格，绕不开这几件事：客户要求"金额大于 1 万且状态是待审核且客户名包含北京"，运营要求在列头上点一下就筛，再顺手导出。而原生 `DataGrid` 只提供排序、分组、列宽拖拽，过滤能力基本靠自己造。常见的三种做法代价都不低：

| 做法 | 问题 |
| --- | --- |
| 继承 `DataGrid` 派生自定义控件 | 与主题、皮肤、第三方样式强耦合；派生链一旦被现网代码占用就难升级；单元测试难 |
| 引进重型商业表格控件 | 体积大、许可成本高、定制受制于厂商 |
| 手写 `ICollectionView.Filter` + 自制过滤 UI | 每张表重复一遍：谓词拼装、列头按钮、面板定位、刷新时机、可取消逻辑 |

做内部系统那阵踩的就是第三种：五张表写了五套过滤逻辑，需求一改（比如"金额要支持区间"）就得五处同步。后来把这套东西沉淀成了开源库 **WpfDataGrid.Enhancements**，核心思路就两条：**Behavior 挂载、附加属性开关**。下面把这套设计的实现细节完整拆开，看完可以照着写自己的版本，也可以直接用现成包。

## 二、设计目标

1. **零侵入**：不改 `DataGrid` 继承结构，不改业务 ViewModel 的既有属性。
2. **一行开启**：XAML 加一个附加属性就能用，默认交互（列头过滤按钮 + 过滤面板）自动就位。
3. **可编程**：过滤条件能被代码直接改，能被持久化、被还原（"保存筛选方案"这类需求）。
4. **可组合**：列间 AND，列内 AND / OR 自由选。
5. **可拦截**：过滤生效前能取消，比如"未选客户不允许按金额过滤"这种业务约束。
6. **MVVM 友好**：过滤模型可以从 ViewModel 注入，不依赖 code-behind。

关键取舍：**不继承**。继承会把行为和控件焊死，而 Behavior 是"附加能力"，用完可以摘掉，天然适配"只在某几张表上开过滤"的场景。

## 三、整体结构：从附加属性到 ICollectionView.Filter

整条链路只有四层，理解这张图后面看代码就不会迷路：

```
XAML: enh:DataGridExtensions.EnableFiltering="True"
        │  (附加属性变更回调)
        ▼
DataGridFilterBehavior  ──挂载到──►  DataGrid
        │  1) 给每个可过滤列的列头注入过滤按钮 FilterColumnHeader
        │  2) 点击按钮 → 弹出 FilterPanel（按列类型生成编辑器）
        │  3) 面板产出 IFilterPredicate
        ▼
FilterModel  (列名 → ColumnFilter → IFilterPredicate[])
        │  IsMatch(item)   列间 AND 语义
        ▼
CollectionViewSource.GetDefaultView(dataGrid.ItemsSource).Filter = item => model.IsMatch(item)
        │  view.Refresh()
        ▼
       表格过滤生效
```

三个要点：

- **过滤发生在视图层**，`ItemsSource` 本身不动，业务数据不会被过滤逻辑污染，导出时也能按需取原始集合。
- **Behavior 只是"装配工"**，它不持有过滤状态，状态在 `FilterModel` 里，所以模型可以被替换、被共享、被序列化。
- **谓词是可扩展的最小单元**，内置四种覆盖大部分场景，剩下的自己实现一个 `IFilterPredicate` 就行。

## 四、核心类型与代码

### 4.1 挂载：一行附加属性

```xml
<Window x:Class="Demo.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:enh="clr-namespace:WpfDataGrid.Enhancements;assembly=WpfDataGrid.Enhancements">
  <Grid>
    <DataGrid x:Name="Grid"
              ItemsSource="{Binding Orders}"
              AutoGenerateColumns="False"
              enh:DataGridExtensions.EnableFiltering="True"
              enh:DataGridExtensions.EnableExporting="True" />
  </Grid>
</Window>
```

`EnableFiltering` 是个普通的附加属性，变更回调里做的事情很朴素：为 `true` 时通过 Microsoft.Xaml.Behaviors 把 `DataGridFilterBehavior` 挂到 `DataGrid` 上，为 `false` 时摘掉。等价的显式写法（需要自己控制 `FilterModel` 时用这种）：

```xml
<DataGrid ItemsSource="{Binding Orders}"
          xmlns:i="http://schemas.microsoft.com/xaml/behaviors"
          xmlns:f="clr-namespace:WpfDataGrid.Enhancements.Filtering;assembly=WpfDataGrid.Enhancements">
  <i:Interaction.Behaviors>
    <f:DataGridFilterBehavior FilterModel="{Binding FilterModel}" />
  </i:Interaction.Behaviors>
</DataGrid>
```

`FilterModel` 留空时，行为会自动创建一个默认实例——这就是"一行 XAML 就能跑"的原因。

### 4.2 FilterModel：列间 AND 的判定中心

```csharp
public sealed class FilterModel
{
    public event EventHandler? Changed;
    public IReadOnlyDictionary<string, ColumnFilter> ColumnFilters { get; }
    public bool IsEmpty => _columnFilters.Count == 0;

    public ColumnFilter GetOrAdd(string columnName);
    public void Clear();
    public void Invalidate();          // 手工改完子对象后通知重新过滤
    public bool IsMatch(object item);  // 逐列 AND
}
```

`IsMatch` 的语义是：**只要有一列的过滤条件不通过，整行就被过滤掉**（列间 AND）。空列（`Predicates` 为空）自动跳过，所以"只筛金额、其他列不限"天然成立。

有两条使用约束值得提前知道：

1. `GetOrAdd(columnName)` **不会**触发 `Changed`。它只是拿到可编辑的对象，改完谓词必须调用 `Invalidate()`，否则视图不会刷新。
2. 键 `columnName` 与列的绑定路径对应。行为解析列名的顺序是：`Binding.Path.Path` → `SortMemberPath` → `Header.ToString()`。所以用 `DataGridTemplateColumn` 又没设 `SortMemberPath` 时，键会退化成表头文本，手工指定条件时要按这个规则传键。

### 4.3 ColumnFilter：列内 AND / OR

```csharp
public enum PredicateCombination { And, Or }

public sealed class ColumnFilter
{
    public ColumnFilter(string columnName);
    public string ColumnName { get; }
    public List<IFilterPredicate> Predicates { get; }
    public PredicateCombination Combination { get; set; } = PredicateCombination.And;
    public bool IsEmpty => Predicates.Count == 0;
    public void AddPredicate(IFilterPredicate predicate);
    public void ClearPredicates();
    public bool Matches(object? value);
}
```

于是组合能力实际是两层：

- **列间**：固定 AND（由 `FilterModel.IsMatch` 决定）。
- **列内**：`Combination` 决定多个谓词是"全部满足"还是"任一满足"。

"客户名包含北京 **或** 包含上海" → 列内 OR；"金额 1000~5000 **且** 状态为待审核" → 分属两列，天然 AND。

> 再复杂的布尔表达树（带括号的 `(A and B) or C`）不在这一版范围内。需要时用一个自定义谓词把整段逻辑包进去，是完全可行的——`IFilterPredicate` 就是一个 `bool Match(object? value)`。

### 4.4 IFilterPredicate 与内置谓词

```csharp
public interface IFilterPredicate
{
    bool Match(object? value);
}
```

四个内置实现，签名如下：

```csharp
// 文本：包含 / 前缀 / 后缀，默认忽略大小写；空文本视为“匹配全部”
new TextContainsPredicate(string searchText, bool ignoreCase = true, TextMatchMode mode = TextMatchMode.Contains)
// TextMatchMode: Contains / StartsWith / EndsWith

// 数值：等于 / 闭区间 / 大于 / 小于；内部用 double 转换，无法转换时返回 false
new NumericRangePredicate(double? min, double? max, NumericCompareMode mode = NumericCompareMode.Between)
// NumericCompareMode: Equals / Between / GreaterThan / LessThan

// 日期：起止均含边界；要“某一天”语义请把 end 取为当天 23:59:59
new DateRangePredicate(DateTime? start, DateTime? end)

// 枚举 / 布尔多选：勾选集合取并集，任一命中即保留
new EnumMultiSelectPredicate(IEnumerable<object> selectedValues)
```

几个容易忽略的细节，都是踩过的坑：

- `TextContainsPredicate` 对 `null` 值返回 `false`，但**空搜索文本返回 `true`**——面板清空输入框等价于"取消该列过滤"，不会把整张表筛空。
- `NumericRangePredicate` 在 `Equals` 模式下会把 `max` 改写成 `min` 的值，所以 `Equals` 语义下只需要关心 `min`。
- `DateRangePredicate` 只做"含边界"的区间比较，不替你做日期截断，`DateTime.Date` 与含时分的值混用时要自己对齐。
- `EnumMultiSelectPredicate` 先按对象相等（`HashSet<object>`）再按字符串（`Ordinal`）兜底，因此 `bool`、`enum`、字符串化后的值都能命中。

### 4.5 用代码编程式组合条件

面板交互之外，过滤条件完全可以由代码驱动——这是做"保存 / 还原筛选方案"的基础：

```csharp
using System.Linq;
using System.Windows;
using Microsoft.Xaml.Behaviors;      // .NET 8 下用这个包；.NET Framework 可用 System.Windows.Interactivity
using WpfDataGrid.Enhancements.Filtering;

var behavior = Interaction.GetBehaviors(Grid)
    .OfType<DataGridFilterBehavior>()
    .FirstOrDefault();

// 金额 1000~5000，且客户名包含“北京”
behavior?.ApplyColumnFilter("Amount", new IFilterPredicate[]
{
    new NumericRangePredicate(1000, 5000)
});
behavior?.ApplyColumnFilter("CustomerName", new IFilterPredicate[]
{
    new TextContainsPredicate("北京")
});

// 清掉某一列的过滤条件（不动其他列）
behavior?.ClearColumnFilter("CustomerName");

// 按当前模型重新应用一次过滤
behavior?.ApplyFilter();
```

需要列内 OR 时走模型层，因为 `ApplyColumnFilter` 面向的是"一列一组条件"的常规场景：

```csharp
var model = behavior!.FilterModel!;
var column = model.GetOrAdd("CustomerName");
column.Combination = PredicateCombination.Or;
column.ClearPredicates();
column.AddPredicate(new TextContainsPredicate("北京"));
column.AddPredicate(new TextContainsPredicate("上海"));
model.Invalidate();   // 必须调用，否则视图不刷新
```

`ApplyColumnFilter` 返回 `bool`：返回 `false` 说明这次过滤被 `Filtering` 事件取消了，调用方据此回滚 UI 上的勾选状态。

### 4.6 可拦截：Filtering 与 Filtered

```csharp
behavior.Filtering += (sender, e) =>
{
    // e.ColumnName：目标列；e.Predicates：即将生效的谓词（空列表表示清除该列）
    if (e.ColumnName == "Amount" && !ViewModel.HasSelectedCustomer)
    {
        e.Cancel = true;             // 取消本次过滤，视图保持不变
        ViewModel.Hint = "请先选择客户，再按金额筛选";
    }
};

behavior.Filtered += (sender, e) =>
{
    ViewModel.Hint = $"{e.ColumnName}：{e.Predicates.Count} 个条件已生效";
};
```

这对事件的价值在于把"业务校验"从过滤实现里剥离出来：过滤组件只管算，要不要放行由业务层决定。

### 4.7 自定义谓词：十几行覆盖业务规则

内置谓词之外，最常见的需求是"按业务编码规则筛"。这类规则写成谓词最省事，写一次全项目的表都能复用：

```csharp
using System;
using System.Globalization;
using WpfDataGrid.Enhancements.Filtering;

/// <summary>订单号谓词：以 BJ 开头且长度不小于 10 位。</summary>
public sealed class OrderNoPatternPredicate : IFilterPredicate
{
    public bool Match(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return text is { Length: >= 10 }
               && text.StartsWith("BJ", StringComparison.OrdinalIgnoreCase);
    }
}
```

挂上去：

```csharp
behavior?.ApplyColumnFilter("OrderNo", new IFilterPredicate[] { new OrderNoPatternPredicate() });
```

只要能被 `FilterModel.IsMatch` 遍历到，自定义谓词与内置谓词地位完全相同，也可以和内置条件混在同一列里做 AND / OR。

### 4.8 换面板：FilterPanelFactory

默认面板 `FilterPanel` 是个 `UserControl`，按列属性类型决定编辑器：

| 列类型 | 面板生成 |
| --- | --- |
| `string` | 文本框 + 匹配模式（包含 / 前缀 / 后缀）+ 忽略大小写勾选 |
| 数值类型 | 比较模式下拉 + 最小 / 最大值输入 |
| 日期类型 | 起止两个 `DatePicker` |
| `bool` / `enum` | 值清单复选（并集过滤） |
| 其他 | 提示"该列类型暂不支持内置过滤编辑器，请使用自定义谓词" |

要换成自己的 UI，注入 `FilterPanelFactory` 即可：

```csharp
behavior.FilterPanelFactory = (owner, columnName, propertyType, closeAction) =>
    new MyFilterPanel(owner, columnName, propertyType, closeAction);
```

约束：自定义面板必须同时是 `UIElement`（弹层由 WPF 的 `Popup` 承载），并实现 `IFilterPanel` 的 `FocusInput()` 与 `Close()`，否则打开时会抛 `InvalidOperationException`。

## 五、值读取：为什么要单独做缓存

谓词拿到的是**行对象**，要取列值就得读属性。直接 `GetProperty().GetValue()` 在十万行 × 五列的场景下会变成明显的卡顿源，所以库内统一走 `PropertyAccessor`：

- 按 `(类型, 属性名)` 缓存在 `ConcurrentDictionary` 里，避免每行重复走 `Type.GetProperty`；
- 读取失败（属性不存在、getter 抛异常）统一返回 `null`，即**视为不匹配**，而不是让整个过滤崩掉；
- 同一套取值逻辑被过滤和导出复用，保证"筛出来的"和"导出去的"口径一致。

## 六、性能与大数据量处理建议

先给结论：**开源版适合万级行，十万行以上要配合策略**。原因是过滤模型走的是 `ICollectionView.Filter`，`Refresh()` 会对**全部行**逐条调用 `IsMatch`，复杂度 O(N × 列数)。

已经做了的优化：

- 面板文本输入有 **300ms 去抖**（`DispatcherTimer`），连续打字不会每次按键都触发全量刷新；
- 属性取值有反射缓存（见上一节）；
- `FilterModel.IsEmpty` 为真时直接恢复默认视图，不做空谓词遍历；
- 过滤状态按列维护，改一列不会重算其他列的条件。

仍然要注意的五点：

1. **谓词内不要做重活**。`Match` 会被调用 N 次，里面不能有 IO、数据库查询、每次新建 `Regex` 这类操作。要正则请在构造时编译好。
2. **避免频繁直接改模型**。一次交互要改多列时，先批量改完再 `Invalidate()` 一次，而不是每加一个谓词就 `Invalidate()`。
3. **十万行以上考虑把过滤下推到数据源**。行对象全在内存里时，不该指望 UI 层过滤撑着：用分页（每页 500~2000 行）或 `IQueryable` 在数据库侧筛，界面上只保留"条件 → 查询参数"的映射，本库的谓词仍可用来做本地二次筛选。
4. **虚拟化是前提而不是优化**。`DataGrid` 本身要开 `EnableRowVirtualization`（默认开），否则行容器会先把性能拖垮；更大数据量的异步分页集合（`AsyncVirtualizingCollection`）属于 Pro 版能力，开源版不包含。
5. **注意 UI 线程**。`Refresh()` 是同步的，行数大时会让 UI 卡顿零点几秒；能接受就按第 3 点做分页，不能接受就得自己做增量过滤（维护一个"已通过行"的集合增量同步），这属于定制范围。

仓库 `docs/perf/performance-report.md` 里有内部实测数据，机器与数据规模不同结论会有偏移，建议以自己项目的数据为准。

## 七、局限与后续计划

诚实地列一下当前版本的边界，省得你踩进来才发现：

- **面板覆盖的类型有限**：`string` / 数值 / 日期 / `bool` / `enum` 之外的列（自定义结构、集合类型等）会提示"请使用自定义谓词"，需要自己出 UI 或走 `FilterPanelFactory`。
- **无正则、无全局搜索框**：正则过滤属于 Pro 版；也没有"输一个词，所有列一起匹配"的快捷入口，需要的话自己拼 `TextContainsPredicate` 挂到多列。
- **布尔表达能力有限**：列间固定 AND，列内 AND / OR 二选一，不支持带括号的任意表达式树。
- **全量刷新**：每次条件变化都是 `Refresh()` 全量重算，没有增量过滤，大数据量要靠分页或下推。
- **导出能力分层**：CSV 导出在开源版（UTF-8 BOM + 字段转义）；xlsx / PDF 导出、正则过滤、异步虚拟化属于 Pro 版。

后续计划主要是三件事：把过滤条件下推到 `IQueryable`（减少内存侧开销）、面板支持自定义编辑器注册、为"筛选方案保存 / 还原"提供序列化支持（`FilterModel` 本身已是"字典 + 谓词列表"结构，序列化只差一层 DTO）。

## 八、安装与快速开始

包已发布到 NuGet，当前最新版本 **1.0.1**，目标框架 `net8.0-windows`（同时提供 `net462` 资产）：

```bash
dotnet add package WpfDataGrid.Enhancements --version 1.0.1
```

```powershell
Install-Package WpfDataGrid.Enhancements -Version 1.0.2
```

- NuGet 包页：<https://www.nuget.org/packages/WpfDataGrid.Enhancements>
- GitHub 仓库：<https://github.com/z861123823/WpfDataGrid.Enhancements>
- 快速开始：<https://github.com/z861123823/WpfDataGrid.Enhancements/blob/main/docs/guide/quick-start.md>
- 扩展开发指南：<https://github.com/z861123823/WpfDataGrid.Enhancements/blob/main/docs/guide/extensibility.md>

装好后按 4.1 那行附加属性加上，再给列配 `Binding`，过滤按钮就会出现在列头上。开源版协议为 MIT，可免费商用；xlsx / PDF 导出、正则过滤、大数据异步虚拟化等能力在 Pro 版（商业授权，内置 30 天试用），需要时再升级，两版共用同一套 Behavior 与附加属性 API，切换成本只有包引用和许可证初始化。

## 九、小结

多列组合过滤这件事，难点不在"怎么写判断"，而在"怎么不把判断散落到每张表里"。这套方案的三层切分是它好用的关键：

- **交互层**（列头按钮 + 面板）负责让人能操作；
- **模型层**（`FilterModel` + `ColumnFilter`）负责把条件表达成可组合、可序列化的数据；
- **谓词层**（`IFilterPredicate`）负责单值判定，是唯一需要为业务定制的地方。

如果你的项目也在重复写 `CollectionView.Filter`，不妨把这层抽出来。代码在 GitHub 上开源，欢迎提 issue 说场景——尤其是你在面板里需要哪些编辑器、以及大数据量下你更希望哪种过滤下推方式。
*（内容由AI生成，仅供参考）*
