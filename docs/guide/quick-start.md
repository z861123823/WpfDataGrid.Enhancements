# 快速上手

本教程带你从零开始使用 **WPF DataGrid 增强包**：安装 → 引入命名空间 → 挂载过滤 → 过滤演示 → 导出演示 → 主题切换 → 常见问题。示例代码均为可编译的完整片段，目标框架 `net8.0-windows` 或 `net462`。

## 1. 安装

### 1.1 通过 NuGet（发布后可用）

```bash
dotnet add package WpfDataGrid.Enhancements
```

如需 xlsx / PDF 导出、正则过滤、大数据虚拟化，再安装 Pro 包并完成授权激活（见 [docs/licensing/README.md](../licensing/README.md)）：

```bash
dotnet add package WpfDataGrid.Enhancements.Pro
```

### 1.2 通过项目引用（源码开发）

将 `src/WpfDataGrid.Enhancements`（与可选 `src/WpfDataGrid.Enhancements.Pro`）加入解决方案，并在使用项目中添加项目引用：

```xml
<ItemGroup>
  <ProjectReference Include="..\src\WpfDataGrid.Enhancements\WpfDataGrid.Enhancements.csproj" />
  <ProjectReference Include="..\src\WpfDataGrid.Enhancements.Pro\WpfDataGrid.Enhancements.Pro.csproj" />
</ItemGroup>
```

> 注意：WPF 项目需启用 Windows 桌面 SDK（`<UseWPF>true</UseWPF>`），并设置目标框架为 `net8.0-windows`（或 `net462`）。

## 2. 引入命名空间

XAML 中声明增强包命名空间：

```xml
xmlns:enh="clr-namespace:WpfDataGrid.Enhancements;assembly=WpfDataGrid.Enhancements"
```

C# 中按需引入：

```csharp
using WpfDataGrid.Enhancements;                 // 附加属性、Behavior
using WpfDataGrid.Enhancements.Filtering;       // FilterModel / ColumnFilter / 谓词
using WpfDataGrid.Enhancements.Exporting;       // CsvExportProvider / TableData
using WpfDataGrid.Enhancements.Theming;         // DataGridThemeManager / ThemeMode
```

Pro 功能对应：

```csharp
using WpfDataGrid.Enhancements.Pro.Exporting;      // XlsxExportProvider / PdfExportProvider
using WpfDataGrid.Enhancements.Pro.Filtering;      // RegexPredicate
using WpfDataGrid.Enhancements.Pro.Performance;    // AsyncVirtualizingCollection<T>
using WpfDataGrid.Enhancements.Pro;                // LicenseManager
```

## 3. 配置 DataGridFilterBehavior

### 3.1 方式一：附加属性（推荐，最简）

```xml
<Window ...
        xmlns:enh="clr-namespace:WpfDataGrid.Enhancements;assembly=WpfDataGrid.Enhancements">
    <Grid>
        <DataGrid x:Name="Grid"
                  ItemsSource="{Binding Orders}"
                  enh:DataGridExtensions.EnableFiltering="True" />
    </Grid>
</Window>
```

### 3.2 方式二：Behavior 显式挂载（XAML）

```xml
<Window ...
        xmlns:enh="clr-namespace:WpfDataGrid.Enhancements;assembly=WpfDataGrid.Enhancements"
        xmlns:i="http://schemas.microsoft.com/xaml/behaviors">
    <Grid>
        <DataGrid x:Name="Grid" ItemsSource="{Binding Orders}">
            <i:Interaction.Behaviors>
                <enh:DataGridFilterBehavior />
            </i:Interaction.Behaviors>
        </DataGrid>
    </Grid>
</Window>
```

### 3.3 方式三：代码挂载 + 编程式过滤

```csharp
using System.Windows;
using WpfDataGrid.Enhancements;
using WpfDataGrid.Enhancements.Filtering;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var behavior = new DataGridFilterBehavior();
        Grid.Behaviors.Add(behavior); // Grid 为 XAML 中的 DataGrid

        // 编程式设置过滤模型：客户列包含 "张"，且金额列介于 100~500
        var model = new FilterModel();
        model.GetOrAdd("客户").AddPredicate(new TextContainsPredicate("张"));
        model.GetOrAdd("金额").AddPredicate(new NumericRangePredicate(100, 500));
        behavior.FilterModel = model;
    }
}
```

## 4. 过滤演示

挂载后，点击列头右侧的过滤图标即可弹出过滤面板，按列类型提供不同谓词：

| 列类型 | 谓词 | 说明 |
|--------|------|------|
| 文本 | `TextContainsPredicate` | 包含 / 前缀 / 后缀 / 精确，默认忽略大小写 |
| 数值 | `NumericRangePredicate` | 区间 / 大于 / 小于 / 等于，支持 null 边界 |
| 日期 | `DateRangePredicate` | 起止日期区间 |
| 枚举 | `EnumMultiSelectPredicate` | 多选匹配 |
| 任意（Pro） | `RegexPredicate` | 正则表达式匹配 |

多条件默认 `And` 组合，可通过 `ColumnFilter.Combination` 切换为 `Or`：

```csharp
var filter = model.GetOrAdd("状态");
filter.Combination = PredicateCombination.Or;
filter.AddPredicate(new TextContainsPredicate("已完成"));
filter.AddPredicate(new TextContainsPredicate("已取消"));
```

`FilterModel` 的 `Changed` 事件会在模型变化时触发，UI 过滤面板自动应用；也可手动调用 `behavior.ApplyFilter()` 立即刷新视图。

## 5. 导出演示

### 5.1 CSV 导出（开源）

```csharp
using System.IO;
using WpfDataGrid.Enhancements.Exporting;

// 构造数据表（列名 + 行数据）
var table = new TableData(
    new[] { "订单号", "客户", "金额" },
    new object?[][]
    {
        new object?[] { 1001, "张三", 199.5m },
        new object?[] { 1002, "李四", 88m },
    });

using var stream = File.Create("orders.csv");
var provider = new CsvExportProvider { WriteBom = true }; // 默认写入 UTF-8 BOM
provider.Export(table, stream);
```

通过 `DataGridExportBehavior` 可直接导出 DataGrid 当前视图（需挂载 EnableExporting）：

```csharp
// 导出当前视图所有行
Grid.Export(ExportScope.CurrentView, "orders.csv");

// 仅导出选中行
Grid.Export(ExportScope.SelectedRows, "selected.csv");
```

### 5.2 xlsx 导出（Pro）

```csharp
using System.IO;
using WpfDataGrid.Enhancements.Exporting;
using WpfDataGrid.Enhancements.Pro.Exporting;

using var stream = File.Create("orders.xlsx");
var provider = new XlsxExportProvider
{
    WorksheetName = "订单",
    StyleHeader = true,        // 表头加粗 + 背景色 + 边框
    AutoColumnWidth = true,    // 自动列宽（默认下限 8 / 上限 60）
    FreezeHeaderRow = true,    // 冻结表头行
};
provider.Export(table, stream);
```

Pro 导出前会自动调用 `LicenseManager.EnsureLicensed(ProFeature.XlsxExport)`，未授权时进入试用模式（默认 30 天 / 5000 行），超限抛 `LicenseException`。

### 5.3 PDF 导出（Pro）

```csharp
using WpfDataGrid.Enhancements.Pro.Exporting;

using var stream = File.Create("orders.pdf");
var pdf = new PdfExportProvider
{
    Title = "订单清单",
    Orientation = PdfExportProvider.PageOrientation.Landscape, // 横向
};
pdf.Export(table, stream);
```

## 6. 主题切换

```csharp
using WpfDataGrid.Enhancements.Theming;

DataGridThemeManager.ApplyTheme(ThemeMode.Light);  // 浅色
DataGridThemeManager.ApplyTheme(ThemeMode.Dark);   // 深色
DataGridThemeManager.ApplyTheme(ThemeMode.System); // 跟随系统（需系统主题支持）
DataGridThemeManager.ClearTheme();                 // 还原默认主题
```

在 XAML 中也可用附加属性指定初始主题：

```xml
<DataGrid enh:DataGridExtensions.ThemeMode="Dark" />
```

主题资源位于 `Theming/Themes/`（`Enhancements.Light.xaml` / `Enhancements.Dark.xaml`），可自行替换以定制配色。

## 7. 常见问题（FAQ）

**Q1：过滤图标不显示？**
确认 `ItemsSource` 绑定集合项是公开属性（`PropertyAccessor` 需反射读取），且已挂载 `EnableFiltering` 或 `DataGridFilterBehavior`。

**Q2：导出为空？**
`TableData.Rows` 依赖传入的行集合；使用 `DataGridExportBehavior` 时请确认 DataGrid 当前视图（`ICollectionView`）已加载数据。

**Q3：xlsx 导出抛授权异常？**
未激活 Pro 时走试用通道；试用到期或行数超 `TrialMaxRows`（默认 5000）会抛 `LicenseException`。请按 [docs/licensing/README.md](../licensing/README.md) 放置授权文件或配置 `LicenseManager.LicenseFilePath`。

**Q4：PDF 中文乱码？**
请确保使用支持中文的字体资源；PdfSharp 默认字体可能不含中文字形，建议在 `PdfExportProvider` 输出前设置全局字体（见设计文档附录）。

**Q5：大数据量导出内存暴涨？**
xlsx 使用 Sylvan.Data.Excel 流式写入，内存占用稳定；CSV 亦为流式写出。避免一次性构造超大 `TableData` 内存集合，建议配合 `AsyncVirtualizingCollection<T>` 分页加载。

**Q6：与其它 UI 框架（如 WPF Toolkit DataGrid）兼容吗？**
本库面向原生 `System.Windows.Controls.DataGrid`，不兼容其它控件库的 DataGrid 类型。

## 下一步

- [API 参考](../api/README.md)
- [授权与激活](../licensing/README.md)
- [文档导航](../README.md)
*（内容由AI生成，仅供参考）*
*（内容由AI生成，仅供参考）*
