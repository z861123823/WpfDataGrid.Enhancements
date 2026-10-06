---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: 871839cd4a3ed3257c2e115376c7f809_fd7ad5f6c0c511f18019525400248c00
    ReservedCode1: EUVmvW42t9/daBhZtf+frohshRhjIu2v/kh1pMLQ1JUSErudMPx64ykMgulLIeacBLIryJgEhfhfcB5+PNx3cMQVwqZh5hOBJQjF0lPCofQnbtwf+J2Y2vsFIc1iq0HP+7VZczXZnucInkIbEUV0A6ppaVByjxBG05QOI+j3uk9b2P2VqYKwz6HqU8A=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: 871839cd4a3ed3257c2e115376c7f809_fd7ad5f6c0c511f18019525400248c00
    ReservedCode2: EUVmvW42t9/daBhZtf+frohshRhjIu2v/kh1pMLQ1JUSErudMPx64ykMgulLIeacBLIryJgEhfhfcB5+PNx3cMQVwqZh5hOBJQjF0lPCofQnbtwf+J2Y2vsFIc1iq0HP+7VZczXZnucInkIbEUV0A6ppaVByjxBG05QOI+j3uk9b2P2VqYKwz6HqU8A=
---







# 扩展指南（Extensibility）

本文档以三步教程演示如何在 WpfDataGrid.Enhancements 上做功能扩展，并提供扩展点总览表。
所有示例代码均为**完整可编译**的片段，可直接复制到你的项目中使用。

---

## 第一步：自定义过滤谓词（IFilterPredicate）

过滤谓词负责回答"单元格值是否满足某个条件"。实现 `IFilterPredicate` 接口后，
通过 `ColumnFilter.AddPredicate` 加入过滤模型即可与内置谓词（文本包含 / 数值范围 / 日期区间 / 枚举多选）混用。

### 示例：长度过滤谓词

```csharp
using System;
using WpfDataGrid.Enhancements.Filtering;

/// <summary>
/// 自定义谓词：只保留字符串长度不小于 MinLength 的行。
/// </summary>
public sealed class LengthPredicate : IFilterPredicate
{
    public LengthPredicate(int minLength)
    {
        if (minLength < 0) throw new ArgumentOutOfRangeException(nameof(minLength));
        MinLength = minLength;
    }

    /// <summary>最小长度（含）。</summary>
    public int MinLength { get; }

    public bool Match(object? value) => value is string text && text.Length >= MinLength;

    public override string ToString() => $"长度 ≥ {MinLength}";
}
```

### 接入过滤模型

```csharp
using WpfDataGrid.Enhancements.Filtering;

// 1. 获取（或创建）目标列的过滤条件集合
var model = new FilterModel();
var nameFilter = model.GetOrAdd("Name");

// 2. 添加自定义谓词（可与内置谓词混用）
nameFilter.AddPredicate(new LengthPredicate(4));
nameFilter.AddPredicate(new TextContainsPredicate("a"));

// 3. 通过模型判定数据行是否保留
bool keep = model.IsMatch(order); // order.Name.Length >= 4 且包含 "a"
```

若希望"满足任一条件即保留"，将 `ColumnFilter.Combination` 设为 `PredicateCombination.Or`：

```csharp
nameFilter.Combination = PredicateCombination.Or;
```

在挂载了 `DataGridFilterBehavior` 的 DataGrid 上，也可以直接编程式应用（等价于点击过滤面板"应用"）：

```csharp
// behavior 为 DataGrid 上挂载的 DataGridFilterBehavior
bool applied = behavior.ApplyColumnFilter("Name", new IFilterPredicate[] { new LengthPredicate(4) });
```

---

## 第二步：自定义导出提供器（IExportProvider）

导出框架按**目标文件扩展名**自动选择提供器：实现 `IExportProvider` 后加入
`DataGridExportBehavior.Providers`，调用 `Export` 时自动路由到你的实现。

### 示例：JSON 导出器

```csharp
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WpfDataGrid.Enhancements.Exporting;

/// <summary>
/// 自定义导出器：把表格数据序列化为 JSON（列名 + 行数组）。
/// </summary>
public sealed class JsonExportProvider : IExportProvider
{
    public string FileExtension => ".json";

    public void Export(TableData tableData, Stream destination)
    {
        var payload = new
        {
            columns = tableData.Columns,
            rows = tableData.Rows
                .Select(r => r.Select(v => v?.ToString()).ToArray())
                .ToArray(),
        };
        JsonSerializer.Serialize(destination, payload);
    }
}
```

### 注册并导出

```csharp
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;
using WpfDataGrid.Enhancements.Exporting;

// 方式一：XAML 附加属性启用导出后，从 DataGrid 取行为并注册
var behavior = Interaction.GetBehaviors(dataGrid)
    .OfType<DataGridExportBehavior>()
    .First();
behavior.Providers.Add(new JsonExportProvider());

// 方式二：显式创建行为挂载（适合代码创建 DataGrid 的场景）
var exportBehavior = new DataGridExportBehavior();
exportBehavior.Providers.Add(new JsonExportProvider());
Interaction.GetBehaviors(dataGrid).Add(exportBehavior);

// 导出：按 ".json" 扩展名自动路由到 JsonExportProvider
exportBehavior.Export(ExportScope.CurrentView, @"D:\exports\orders.json");
```

内置的 `CsvExportProvider` 在行为挂载时自动加入 `Providers`；若希望完全替换为自定义实现，
清除 `Providers` 或显式设置 `ExportProvider` 属性即可。

---

## 第三步：自定义主题注册与切换（DataGridThemeManager）

内置浅色 / 深色主题通过 `DataGridEnhancements.*` 资源键控制配色。注册自定义主题的本质是：
**构造一份覆盖这些键的 `ResourceDictionary`，交给 `RegisterTheme` 登记，再用 `ApplyTheme(name)` 切换**。

### 示例：翡翠绿主题

```csharp
using System.Windows;
using System.Windows.Media;
using WpfDataGrid.Enhancements.Theming;

public static class GreenTheme
{
    public const string Name = "翡翠绿";

    /// <summary>构造覆盖核心配色键的绿色主题资源字典。</summary>
    public static ResourceDictionary Build()
    {
        var dict = new ResourceDictionary();

        // 覆盖主题主色：按钮 / 选中态 / 过滤按钮激活态
        dict["DataGridEnhancements.AccentColor"] = Color.FromRgb(0x1F, 0x8A, 0x5F);
        dict["DataGridEnhancements.AccentBrush"] = new SolidColorBrush(Color.FromRgb(0x1F, 0x8A, 0x5F));
        dict["DataGridEnhancements.FilterButtonActiveBackground"] = new SolidColorBrush(Color.FromRgb(0xE3, 0xF5, 0xEC));
        dict["DataGridEnhancements.FilterButtonActiveForeground"] = new SolidColorBrush(Color.FromRgb(0x1F, 0x8A, 0x5F));
        dict["DataGridEnhancements.DemoCheckedBrush"] = new SolidColorBrush(Color.FromRgb(0xE3, 0xF5, 0xEC));

        // 页面底色与卡片：青白基调
        dict["DataGridEnhancements.PageBackground"] = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xF2));
        dict["DataGridEnhancements.CardBackground"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        dict["DataGridEnhancements.CardBorderBrush"] = new SolidColorBrush(Color.FromRgb(0xC9, 0xDF, 0xD4));
        dict["DataGridEnhancements.TextPrimary"] = new SolidColorBrush(Color.FromRgb(0x24, 0x3A, 0x31));
        dict["DataGridEnhancements.TextSecondary"] = new SolidColorBrush(Color.FromRgb(0x5A, 0x6B, 0x63));

        return dict;
    }
}
```

### 运行时注册并切换

```csharp
using WpfDataGrid.Enhancements.Theming;

// 1. 注册（同名重复注册会替换旧字典；若该主题正在生效会立即刷新）
DataGridThemeManager.RegisterTheme(GreenTheme.Name, GreenTheme.Build());

// 2. 切换到自定义主题
bool ok = DataGridThemeManager.ApplyTheme(GreenTheme.Name);
if (ok)
{
    // 当前生效主题名称 / 是否处于自定义主题
    var current = DataGridThemeManager.AppliedCustomThemeName; // "翡翠绿"
    var isCustom = DataGridThemeManager.IsCustomThemeApplied;  // true
}

// 3. 切回内置主题
DataGridThemeManager.ApplyTheme(ThemeMode.Light);  // 浅色
DataGridThemeManager.ApplyTheme(ThemeMode.Dark);   // 深色
DataGridThemeManager.ApplyTheme(ThemeMode.System); // 跟随系统

// 4. 查询 / 清理
var names = DataGridThemeManager.RegisteredThemeNames;
var dict   = DataGridThemeManager.TryGetRegisteredTheme(GreenTheme.Name);
DataGridThemeManager.ClearTheme(); // 移除已合并的自定义主题，恢复默认
```

> 提示：主题资源采用 `DynamicResource` 引用，切换后已打开的窗口会立即换肤，无需重建 UI。

---

## 扩展点总览表

| 扩展方向 | 接口 / 事件 / 注册入口 | 关键成员 | 说明 |
|----------|------------------------|----------|------|
| 自定义过滤谓词 | `IFilterPredicate`（接口） | `bool Match(object? value)` | 实现后经 `ColumnFilter.AddPredicate` 接入过滤模型 |
| 自定义导出提供器 | `IExportProvider`（接口） | `string FileExtension`；`void Export(TableData, Stream)` | 加入 `DataGridExportBehavior.Providers`，按扩展名自动路由 |
| 自定义主题 | `DataGridThemeManager.RegisterTheme(string, ResourceDictionary)` | `ApplyTheme(string)` / `TryGetRegisteredTheme(string)` | 覆盖 `DataGridEnhancements.*` 键即可换肤 |
| 过滤前钩子 | `DataGridFilterBehavior.Filtering`（事件，可取消） | `FilterChangingEventArgs.Cancel` | 参数含列名与待生效谓词；取消则本次过滤不应用 |
| 过滤后钩子 | `DataGridFilterBehavior.Filtered`（事件） | `FilterChangedEventArgs.ColumnName/Predicates` | 过滤已生效后触发 |
| 导出前钩子 | `DataGridExportBehavior.Exporting`（事件，可取消） | `ExportingEventArgs.Cancel/FilePath/RowCount` | 取消则不创建文件 |
| 导出后钩子 | `DataGridExportBehavior.Exported`（事件） | `ExportedEventArgs.FilePath/RowCount` | 导出成功完成后触发 |

API 签名索引见 [API 参考](../api/README.md)；基础用法见 [快速上手](quick-start.md)。
*（内容由AI生成，仅供参考）*
*（内容由AI生成，仅供参考）*
*（内容由AI生成，仅供参考）*
