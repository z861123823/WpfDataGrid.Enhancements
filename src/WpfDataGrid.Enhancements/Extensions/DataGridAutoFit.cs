using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace WpfDataGrid.Enhancements;

/// <summary>
/// DataGrid 列宽自动识别（AutoFit）附加属性入口。
/// 开启 AutoFitColumns 后，按表头文本与单元格内容文本（FormattedText 测量）自动设置各列宽度。
/// 与现有主题 / 显式 DataGridCell ControlTemplate / 过滤面板 / 虚拟化共存，不修改任何样式。
/// 触发时机：Loaded、ItemsSource 变化、配置属性变化时调度一次 Apply；Apply 内部按阈值收敛，
/// 列宽未变化时不写 Width，避免布局死循环。不再监听 LayoutUpdated，防止无限重算。
/// </summary>
public static class DataGridAutoFit
{
    /// <summary>采样模式：只测量前 N 行；全量模式：遍历所有行。</summary>
    public enum AutoFitMode
    {
        /// <summary>采样前 SampleSize 行（大数据集默认，避免卡顿）。</summary>
        Sample,

        /// <summary>遍历全部行（适用于小数据集，最精确）。</summary>
        Full,
    }

    /// <summary>表头左右 Padding + 列分隔线留白（DataGridColumnHeader 主题 Padding=8,8，另加 1px 网格线）。</summary>
    private const double HeaderPadding = 17;

    /// <summary>单元格左右 Padding（DataGridCell 主题 Padding=8,6）。</summary>
    private const double CellPadding = 16;

    /// <summary>Full 模式允许全量遍历的最大行数（超过则自动回落 Sample，避免大数据集卡顿）。</summary>
    private const int FullModeRowLimit = 5000;

    /// <summary>设置列宽的最小变化阈值（px），低于该值不写 Width，避免无意义布局重算。</summary>
    private const double WidthChangeThreshold = 0.5;

    private static readonly Dictionary<string, PropertyInfo?> _propertyCache = new();

    /// <summary>启用列宽自动识别。</summary>
    public static readonly DependencyProperty AutoFitColumnsProperty =
        DependencyProperty.RegisterAttached(
            "AutoFitColumns",
            typeof(bool),
            typeof(DataGridAutoFit),
            new PropertyMetadata(false, OnAutoFitColumnsChanged));

    /// <summary>测量模式：Sample（默认，采样前 N 行）/ Full（全量遍历）。</summary>
    public static readonly DependencyProperty AutoFitModeProperty =
        DependencyProperty.RegisterAttached(
            "AutoFitMode",
            typeof(AutoFitMode),
            typeof(DataGridAutoFit),
            new PropertyMetadata(AutoFitMode.Sample));

    /// <summary>采样行数（Sample 模式生效，默认 100；10 万行场景必须走采样）。</summary>
    public static readonly DependencyProperty SampleSizeProperty =
        DependencyProperty.RegisterAttached(
            "SampleSize",
            typeof(int),
            typeof(DataGridAutoFit),
            new PropertyMetadata(100, OnConfigChanged));

    /// <summary>最小列宽约束（NaN 表示不限制）。</summary>
    public static readonly DependencyProperty MinColumnWidthProperty =
        DependencyProperty.RegisterAttached(
            "MinColumnWidth",
            typeof(double),
            typeof(DataGridAutoFit),
            new PropertyMetadata(double.NaN, OnConfigChanged));

    /// <summary>最大列宽约束（NaN 表示不限制）。</summary>
    public static readonly DependencyProperty MaxColumnWidthProperty =
        DependencyProperty.RegisterAttached(
            "MaxColumnWidth",
            typeof(double),
            typeof(DataGridAutoFit),
            new PropertyMetadata(double.NaN, OnConfigChanged));

    /// <summary>最后一列使用 * 填充剩余空间（其余列按内容定宽）。</summary>
    public static readonly DependencyProperty StarLastColumnProperty =
        DependencyProperty.RegisterAttached(
            "StarLastColumn",
            typeof(bool),
            typeof(DataGridAutoFit),
            new PropertyMetadata(false, OnConfigChanged));

    public static void SetAutoFitColumns(DependencyObject element, bool value) => element.SetValue(AutoFitColumnsProperty, value);
    public static bool GetAutoFitColumns(DependencyObject element) => (bool)element.GetValue(AutoFitColumnsProperty);

    public static void SetAutoFitMode(DependencyObject element, AutoFitMode value) => element.SetValue(AutoFitModeProperty, value);
    public static AutoFitMode GetAutoFitMode(DependencyObject element) => (AutoFitMode)element.GetValue(AutoFitModeProperty);

    public static void SetSampleSize(DependencyObject element, int value) => element.SetValue(SampleSizeProperty, value);
    public static int GetSampleSize(DependencyObject element) => (int)element.GetValue(SampleSizeProperty);

    public static void SetMinColumnWidth(DependencyObject element, double value) => element.SetValue(MinColumnWidthProperty, value);
    public static double GetMinColumnWidth(DependencyObject element) => (double)element.GetValue(MinColumnWidthProperty);

    public static void SetMaxColumnWidth(DependencyObject element, double value) => element.SetValue(MaxColumnWidthProperty, value);
    public static double GetMaxColumnWidth(DependencyObject element) => (double)element.GetValue(MaxColumnWidthProperty);

    public static void SetStarLastColumn(DependencyObject element, bool value) => element.SetValue(StarLastColumnProperty, value);
    public static bool GetStarLastColumn(DependencyObject element) => (bool)element.GetValue(StarLastColumnProperty);

    private static void OnAutoFitColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (!(d is DataGrid grid)) return;
        if ((bool)e.NewValue)
        {
            Hook(grid);
        }
        else
        {
            Unhook(grid);
        }
    }

    private static void OnConfigChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (!(d is DataGrid grid)) return;
        if (GetAutoFitColumns(grid))
        {
            Apply(grid);
        }
    }

    private static void Hook(DataGrid grid)
    {
        grid.Loaded += OnGridLoaded;
        grid.Unloaded += OnGridUnloaded;
        if (grid.IsLoaded)
        {
            OnGridLoaded(grid, new RoutedEventArgs());
        }
    }

    private static void Unhook(DataGrid grid)
    {
        grid.Loaded -= OnGridLoaded;
        grid.Unloaded -= OnGridUnloaded;
        RemoveItemsSourceHook(grid);
    }

    private static void OnGridLoaded(object? sender, RoutedEventArgs e)
    {
        var grid = (DataGrid)sender!;
        AddItemsSourceHook(grid);
        Apply(grid);
    }

    private static void OnGridUnloaded(object? sender, RoutedEventArgs e)
    {
        var grid = (DataGrid)sender!;
        RemoveItemsSourceHook(grid);
    }

    private static void AddItemsSourceHook(DataGrid grid)
    {
        var descriptor = DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ItemsControl));
        descriptor?.AddValueChanged(grid, OnItemsSourceChanged);
    }

    private static void RemoveItemsSourceHook(DataGrid grid)
    {
        var descriptor = DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ItemsControl));
        descriptor?.RemoveValueChanged(grid, OnItemsSourceChanged);
    }

    private static void OnItemsSourceChanged(object? sender, EventArgs e) => Apply((DataGrid)sender!);

    /// <summary>立即执行列宽测量并应用到所有列（公开，便于手动触发与测试）。</summary>
    public static void Apply(DataGrid grid)
    {
        if (!GetAutoFitColumns(grid)) return;
        if (grid.Columns.Count == 0) return;

        double pixelsPerDip = 1.0;
        try
        {
            pixelsPerDip = VisualTreeHelper.GetDpi(grid).PixelsPerDip;
        }
        catch (InvalidOperationException)
        {
            // 未挂接可视树时使用默认值，测量结果误差可忽略
        }

        double fontSize = grid.FontSize > 0 ? grid.FontSize : 13.0;
        var typeface = new Typeface(grid.FontFamily ?? new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var headerTypeface = new Typeface(grid.FontFamily ?? new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        // 根因2：不触碰 grid.Items 容器集合（大数据集下同步枚举会阻塞 UI 线程），
        // 改为从 ItemsSource 取数据；null 时直接返回，保持默认列宽。
        if (!(grid.ItemsSource is IEnumerable source)) return;

        int total = source is ICollection collection ? collection.Count : -1;
        bool full = GetAutoFitMode(grid) == AutoFitMode.Full && total > 0 && total <= FullModeRowLimit;
        int sample = Math.Max(GetSampleSize(grid), 1);
        if (!full && total >= 0)
        {
            sample = Math.Min(sample, total);
        }

        double minWidth = GetMinColumnWidth(grid);
        double maxWidth = GetMaxColumnWidth(grid);
        bool hasMin = !double.IsNaN(minWidth) && minWidth > 0;
        bool hasMax = !double.IsNaN(maxWidth) && maxWidth > 0;

        for (int i = 0; i < grid.Columns.Count; i++)
        {
            var column = grid.Columns[i];
            double headerTextWidth = MeasureText(column.Header?.ToString(), headerTypeface, fontSize, pixelsPerDip);
            double maxContentWidth = 0;

            int examined = 0;
            foreach (object item in source)
            {
                if (!full && examined >= sample) break;
                string text = GetCellText(column, item);
                if (!string.IsNullOrEmpty(text))
                {
                    double w = MeasureText(text, typeface, fontSize, pixelsPerDip);
                    if (w > maxContentWidth) maxContentWidth = w;
                }

                examined++;
            }

            double desired = maxContentWidth + CellPadding;
            double headerWidth = headerTextWidth + HeaderPadding;
            if (headerWidth > desired) desired = headerWidth;

            // 已有固定像素宽度作为下限保留（AutoFit 只增不减）
            if (column.Width.IsAbsolute && column.Width.Value > desired)
            {
                desired = column.Width.Value;
            }

            if (hasMin && desired < minWidth) desired = minWidth;
            if (hasMax && desired > maxWidth) desired = maxWidth;

            bool isLast = i == grid.Columns.Count - 1;
            if (isLast && GetStarLastColumn(grid))
            {
                // 根因1收敛：Star 值未变化时不重复写 Width，避免再次触发布局重算
                if (!(column.Width.IsStar && Math.Abs(column.Width.Value - 1.0) < 0.0001))
                {
                    column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                }
            }
            else
            {
                // 根因1收敛：新旧宽度差值小于阈值时跳过，避免无意义布局更新
                if (!(column.Width.IsAbsolute && Math.Abs(column.Width.Value - desired) < WidthChangeThreshold))
                {
                    column.Width = new DataGridLength(desired);
                }
            }
        }
    }

    private static double MeasureText(string? text, Typeface typeface, double fontSize, double pixelsPerDip)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (text.Length > 512) text = text.Substring(0, 512);

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            pixelsPerDip);

        return formatted.WidthIncludingTrailingWhitespace;
    }

    private static string GetCellText(DataGridColumn column, object item)
    {
        if (item == null || !(column is DataGridTextColumn textColumn))
        {
            return string.Empty;
        }

        var binding = textColumn.Binding as Binding;
        if (binding == null || string.IsNullOrEmpty(binding.Path?.Path))
        {
            return string.Empty;
        }

        string path = binding.Path.Path;
        object? value = GetProperty(item.GetType(), path)?.GetValue(item);
        if (value == null) return string.Empty;

        if (!string.IsNullOrEmpty(binding.StringFormat))
        {
            try
            {
                return string.Format(CultureInfo.CurrentCulture, binding.StringFormat, value) ?? string.Empty;
            }
            catch (FormatException)
            {
                return value.ToString() ?? string.Empty;
            }
        }

        return value.ToString() ?? string.Empty;
    }

    private static PropertyInfo? GetProperty(Type type, string path)
    {
        var key = (type.FullName ?? type.Name) + "|" + path;
        if (_propertyCache.TryGetValue(key, out PropertyInfo? cached))
        {
            return cached;
        }

        PropertyInfo? property = type.GetProperty(path);
        _propertyCache[key] = property;
        return property;
    }
}
