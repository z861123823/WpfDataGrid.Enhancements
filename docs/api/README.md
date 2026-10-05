---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: 871839cd4a3ed3257c2e115376c7f809_23e6f0b8c0bc11f197eb525400393706
    ReservedCode1: EPjsiJgHvXlNWMsa7fVYyaAur9lWs1zmjLe/0kkO8B34iCD39T2g2axuRsPqthxtkOL8YkcZh1/+r3ghcJBFwUseB42eApUFHI6NoIkh6FAFL5qbqFkOr8ykkgRvi8baegFzz0I+0bYbXHhA3owqvVdme42ZPgssTicLQrR483WlyB4FnxXlb10fmNY=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: 871839cd4a3ed3257c2e115376c7f809_23e6f0b8c0bc11f197eb525400393706
    ReservedCode2: EPjsiJgHvXlNWMsa7fVYyaAur9lWs1zmjLe/0kkO8B34iCD39T2g2axuRsPqthxtkOL8YkcZh1/+r3ghcJBFwUseB42eApUFHI6NoIkh6FAFL5qbqFkOr8ykkgRvi8baegFzz0I+0bYbXHhA3owqvVdme42ZPgssTicLQrR483WlyB4FnxXlb10fmNY=
---





# API 参考

本文档索引 WpfDataGrid.Enhancements 解决方案的公开类型清单：每个类型一句话说明 + 主要成员签名摘录。详细用法见 [快速上手](../guide/quick-start.md)。

命名空间速查：

| 命名空间 | 程序集 | 说明 |
|----------|--------|------|
| `WpfDataGrid.Enhancements` | WpfDataGrid.Enhancements | 附加属性入口、授权兜底 |
| `WpfDataGrid.Enhancements.Filtering` | WpfDataGrid.Enhancements | 过滤模型与谓词 |
| `WpfDataGrid.Enhancements.Exporting` | WpfDataGrid.Enhancements / .Pro | 导出框架与 CSV 提供器 |
| `WpfDataGrid.Enhancements.Theming` | WpfDataGrid.Enhancements | 主题管理 |
| `WpfDataGrid.Enhancements.Pro` | WpfDataGrid.Enhancements.Pro | Pro 授权入口 |
| `WpfDataGrid.Enhancements.Pro.Exporting` | WpfDataGrid.Enhancements.Pro | xlsx / PDF 导出 |
| `WpfDataGrid.Enhancements.Pro.Filtering` | WpfDataGrid.Enhancements.Pro | 正则过滤 |
| `WpfDataGrid.Enhancements.Pro.Performance` | WpfDataGrid.Enhancements.Pro | 大数据虚拟化 |

## 开源版核心类型

### DataGridExtensions（静态类，附加属性入口）

以附加属性方式一键启用增强能力，等价于手动挂载 Behavior。

```csharp
public static class DataGridExtensions
{
    public static readonly DependencyProperty EnableFilteringProperty;
    public static readonly DependencyProperty EnableExportingProperty;
    public static readonly DependencyProperty ThemeModeProperty;

    public static bool GetEnableFiltering(DependencyObject element);
    public static void SetEnableFiltering(DependencyObject element, bool value);
    public static bool GetEnableExporting(DependencyObject element);
    public static void SetEnableExporting(DependencyObject element, bool value);
    public static ThemeMode GetThemeMode(DependencyObject element);
    public static void SetThemeMode(DependencyObject element, ThemeMode value);
}
```

### DataGridFilterBehavior（Behavior<DataGrid>，过滤行为）

挂载到 DataGrid 后自动构建过滤面板，通过 `ICollectionView` 应用组合过滤。

```csharp
public class DataGridFilterBehavior : DataGridBehaviorBase
{
    public FilterModel? FilterModel { get; set; }
    public FrameworkElement Root { get; }
    public void ApplyFilter();
    public void SetFilterActive(bool active);
    public void Detach();
}
```

### FilterModel（过滤模型根）

管理多个 `ColumnFilter`，对外暴露变更事件与整体判定。

```csharp
public sealed class FilterModel
{
    public event EventHandler? Changed;
    public IReadOnlyDictionary<string, ColumnFilter> ColumnFilters { get; }
    public bool IsEmpty { get; }

    public ColumnFilter GetOrAdd(string columnName);
    public void Clear();
    public void Invalidate();
    public bool IsMatch(object item);
}
```

### ColumnFilter（单列过滤容器）

```csharp
public sealed class ColumnFilter
{
    public ColumnFilter(string columnName);
    public string ColumnName { get; }
    public List<IFilterPredicate> Predicates { get; }
    public PredicateCombination Combination { get; set; } // And / Or

    public void AddPredicate(IFilterPredicate predicate);
    public void ClearPredicates();
    public bool Matches(object? value);
}
```

### IFilterPredicate（谓词接口）

```csharp
public interface IFilterPredicate
{
    bool Match(object? value);
}
```

### FilterPredicates（内置谓词工厂）

文本包含 / 数值范围 / 日期区间 / 枚举多选，均可直接 `new` 使用。

```csharp
public sealed class TextContainsPredicate : IFilterPredicate
{
    public TextContainsPredicate(string searchText, bool ignoreCase = true,
                                 TextMatchMode mode = TextMatchMode.Contains);
    public string SearchText { get; }
    public bool IgnoreCase { get; }
    public TextMatchMode Mode { get; } // Contains / StartsWith / EndsWith / Exact
}

public sealed class NumericRangePredicate : IFilterPredicate
{
    public NumericRangePredicate(double? min, double? max,
                                 NumericCompareMode mode = NumericCompareMode.Between);
    public double? Min { get; }
    public double? Max { get; }
    public NumericCompareMode Mode { get; } // Between / GreaterThan / LessThan / Equal
}

public sealed class DateRangePredicate : IFilterPredicate
{
    public DateRangePredicate(DateTime? start, DateTime? end);
    public DateTime? Start { get; }
    public DateTime? End { get; }
}

public sealed class EnumMultiSelectPredicate : IFilterPredicate
{
    public EnumMultiSelectPredicate(IEnumerable<object> selectedValues);
    public IReadOnlyCollection<string> SelectedValues { get; }
}
```

### IExportProvider（导出提供器接口）

```csharp
public interface IExportProvider
{
    string FileExtension { get; }
    void Export(TableData tableData, Stream destination);
}
```

### TableData（导出数据载体）

```csharp
public sealed class TableData
{
    public TableData(IReadOnlyList<string> columns, IEnumerable<object?[]> rows);
    public IReadOnlyList<string> Columns { get; }
    public IEnumerable<object?[]> Rows { get; }
    public string? WorksheetName { get; set; }
}
```

### CsvExportProvider（CSV 导出）

UTF-8 输出（默认带 BOM），自动处理逗号 / 引号 / 换行转义。

```csharp
public sealed class CsvExportProvider : IExportProvider
{
    public bool WriteBom { get; set; } = true;
    public string FileExtension => ".csv";
    public void Export(TableData tableData, Stream destination);
}
```

### DataGridExportBehavior（导出行为）

挂载后可将 DataGrid 当前视图或选中行导出到文件。

```csharp
public enum ExportScope { CurrentView, SelectedRows }

public class DataGridExportBehavior : DataGridBehaviorBase
{
    public IExportProvider? ExportProvider { get; set; }
    public ICollection<IExportProvider> Providers { get; }
    public void Export(ExportScope scope, string filePath);
    public void Export(ExportScope scope, string filePath, IProgress<int>? progress);
}
```

### DataGridThemeManager（主题管理）

```csharp
public enum ThemeMode { System = 0, Light = 1, Dark = 2 }

public static class DataGridThemeManager
{
    public const string ThemePackPrefix = "pack://application:,,,/WpfDataGrid.Enhancements;component/Theming/Themes/";
    public const string LightThemeFileName = "Enhancements.Light.xaml";
    public const string DarkThemeFileName = "Enhancements.Dark.xaml";

    public static void ApplyTheme(ThemeMode mode);
    public static void ClearTheme();
}
```

### PropertyAccessor（属性反射辅助）

供过滤 / 排序按属性名动态取值，内部使用。

```csharp
public static class PropertyAccessor
{
    public static Func<object, object?>? GetGetter(Type itemType, string propertyName);
    public static object? GetValue(object item, string propertyName);
    public static Type? GetPropertyType(Type itemType, string propertyName);
    public static string ToDisplayString(object? value);
}
```

## Pro 版核心类型

### LicenseManager（授权校验）

RSA-2048 / RSA-SHA256 签名授权；未激活时进入试用模式（默认 30 天 / 5000 行）。

```csharp
public static class LicenseManager
{
    public const string LicenseFileName = "wpfdatagrid.enhancements.license";
    public static string LicenseFilePath { get; set; }
    public static string TrialStateFilePath { get; set; }
    public static int TrialDays { get; set; }       // 默认 30
    public static int TrialMaxRows { get; set; }    // 默认 5000

    public static LicenseStatus ValidateLicense();
    public static LicenseDocument? TryGetLicense();
    public static bool IsFeatureEnabled(ProFeature feature);
    public static void EnsureLicensed(ProFeature feature);
    public static int GetTrialRemainingDays();
    public static void ResetCache();
}
```

授权文件格式、签发工具与激活步骤见 [授权说明](../licensing/README.md)。

### XlsxExportProvider（xlsx 导出，Pro）

基于 Sylvan.Data.Excel 流式写入，输出后统一美化：表头加粗 + 背景 + 边框、文本左对齐 / 数值右对齐、日期格式保留、自动列宽（默认 8~60）、可选冻结表头。

```csharp
public sealed class XlsxExportProvider : IExportProvider
{
    public const string DefaultWorksheetName = "Sheet1";
    public const int DefaultMinColumnWidth = 8;
    public const int DefaultMaxColumnWidth = 60;

    public string FileExtension => ".xlsx";
    public string? WorksheetName { get; set; }
    public bool StyleHeader { get; set; } = true;
    public bool AutoColumnWidth { get; set; } = true;
    public bool FreezeHeaderRow { get; set; } = true;
    public int MinColumnWidth { get; set; } = DefaultMinColumnWidth;
    public int MaxColumnWidth { get; set; } = DefaultMaxColumnWidth;

    public void Export(TableData tableData, Stream destination);
}
```

### PdfExportProvider（PDF 导出，Pro）

基于 PdfSharp 分页输出，支持标题与横纵向。

```csharp
public sealed class PdfExportProvider : IExportProvider
{
    public enum PageOrientation { Portrait, Landscape }
    public static class PageSize
    {
        public const double A4Width = 595.0;
        public const double A4Height = 842.0;
    }

    public string FileExtension => ".pdf";
    public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;
    public string? Title { get; set; }

    public void Export(TableData tableData, Stream destination);
}
```

### RegexPredicate（正则过滤，Pro）

```csharp
public sealed class RegexPredicate : IFilterPredicate
{
    public RegexPredicate(string pattern);
    public string Pattern { get; }
    public bool Match(object? value);
}
```

### AsyncVirtualizingCollection<T>（大数据虚拟化，Pro）

按页异步加载，通过 `PageLoader` 委托从数据源取数，支持排序与集合通知。

```csharp
public class AsyncVirtualizingCollection<T> : IList<T>, IList,
    INotifyCollectionChanged, INotifyPropertyChanged
{
    public delegate IEnumerable<T> PageLoader(int offset, int count);

    public AsyncVirtualizingCollection(PageLoader pageLoader,
                                       int pageSize = 100,
                                       int totalCount = 0,
                                       Action<Action>? marshal = null);
    public int TotalCount { get; }
    public int PageSize { get; }
    public int LoadedCount { get; }
    public T this[int index] { get; }

    public void SetTotalCount(int totalCount);
    public void LoadPage(int pageIndex);
    public void LoadAll();
    public void ApplySort(string propertyName, bool ascending);
}
```

## 扩展点

- **新增导出格式**：实现 `IExportProvider` 并加入 `DataGridExportBehavior.Providers`。
- **自定义过滤谓词**：实现 `IFilterPredicate.Match(object?)`。
- **自定义主题**：替换 `Theming/Themes/` 下的资源字典后调用 `DataGridThemeManager.ApplyTheme`。
*（内容由AI生成，仅供参考）*
*（内容由AI生成，仅供参考）*
