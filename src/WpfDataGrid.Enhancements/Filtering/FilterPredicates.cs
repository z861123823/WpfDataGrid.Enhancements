using System;
using System.Collections.Generic;
using System.Globalization;

namespace WpfDataGrid.Enhancements.Filtering;

/// <summary>文本匹配模式。</summary>
public enum TextMatchMode
{
    /// <summary>包含匹配（默认）。</summary>
    Contains,

    /// <summary>前缀匹配。</summary>
    StartsWith,

    /// <summary>后缀匹配。</summary>
    EndsWith
}

/// <summary>
/// 文本模糊匹配谓词：包含 / 前缀 / 后缀，支持忽略大小写。
/// 空搜索文本视为"匹配全部"。
/// </summary>
public sealed class TextContainsPredicate : IFilterPredicate
{
    public TextContainsPredicate(string searchText, bool ignoreCase = true, TextMatchMode mode = TextMatchMode.Contains)
    {
        SearchText = searchText ?? string.Empty;
        IgnoreCase = ignoreCase;
        Mode = mode;
    }

    /// <summary>搜索文本。</summary>
    public string SearchText { get; }

    /// <summary>是否忽略大小写（默认 true）。</summary>
    public bool IgnoreCase { get; }

    /// <summary>匹配模式。</summary>
    public TextMatchMode Mode { get; }

    /// <inheritdoc />
    public bool Match(object? value)
    {
        if (string.IsNullOrEmpty(SearchText)) return true;
        if (value == null) return false;

        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (text.Length == 0) return false;

        var comparison = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        switch (Mode)
        {
            case TextMatchMode.StartsWith:
                return text.StartsWith(SearchText, comparison);
            case TextMatchMode.EndsWith:
                return text.EndsWith(SearchText, comparison);
            case TextMatchMode.Contains:
            default:
                return text.IndexOf(SearchText, comparison) >= 0;
        }
    }
}

/// <summary>数值比较模式。</summary>
public enum NumericCompareMode
{
    /// <summary>等于指定值。</summary>
    Equals,

    /// <summary>闭区间 [Min, Max]（默认）。</summary>
    Between,

    /// <summary>大于指定值。</summary>
    GreaterThan,

    /// <summary>小于指定值。</summary>
    LessThan
}

/// <summary>
/// 数值范围谓词：等于 / 区间 / 大于 / 小于。支持 int / double / decimal 等可转换数值。
/// </summary>
public sealed class NumericRangePredicate : IFilterPredicate
{
    public NumericRangePredicate(double? min, double? max, NumericCompareMode mode = NumericCompareMode.Between)
    {
        Min = mode == NumericCompareMode.Equals ? min : min;
        Max = mode == NumericCompareMode.Equals ? min : max;
        Mode = mode;
    }

    /// <summary>区间下界 / 单值目标（Equals、GreaterThan、LessThan 模式下使用）。</summary>
    public double? Min { get; }

    /// <summary>区间上界（仅 Between 模式使用）。</summary>
    public double? Max { get; }

    /// <summary>比较模式。</summary>
    public NumericCompareMode Mode { get; }

    /// <inheritdoc />
    public bool Match(object? value)
    {
        if (value == null) return false;

        double number;
        try
        {
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return false;
        }

        switch (Mode)
        {
            case NumericCompareMode.Equals:
                return Min.HasValue && Math.Abs(number - Min.Value) < 1e-9;
            case NumericCompareMode.GreaterThan:
                return Min.HasValue && number > Min.Value;
            case NumericCompareMode.LessThan:
                return Min.HasValue && number < Min.Value;
            case NumericCompareMode.Between:
            default:
                if (Min.HasValue && number < Min.Value) return false;
                if (Max.HasValue && number > Max.Value) return false;
                return true;
        }
    }
}

/// <summary>
/// 日期范围谓词：起止日期（含边界）。调用方通常把 end 取为当天结束时间以保证"某日"语义。
/// </summary>
public sealed class DateRangePredicate : IFilterPredicate
{
    public DateRangePredicate(DateTime? start, DateTime? end)
    {
        Start = start;
        End = end;
    }

    /// <summary>起始日期（含）。</summary>
    public DateTime? Start { get; }

    /// <summary>结束日期（含）。</summary>
    public DateTime? End { get; }

    /// <inheritdoc />
    public bool Match(object? value)
    {
        if (value == null) return false;

        DateTime date;
        try
        {
            date = Convert.ToDateTime(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return false;
        }

        if (Start.HasValue && date < Start.Value) return false;
        if (End.HasValue && date > End.Value) return false;
        return true;
    }
}

/// <summary>
/// 枚举 / 布尔多选谓词：勾选多个值取并集（任一匹配即保留）。
/// 以值字符串（Ordinal）比较，兼容 enum 与 bool。
/// </summary>
public sealed class EnumMultiSelectPredicate : IFilterPredicate
{
    private readonly HashSet<string> _selected;
    private readonly HashSet<object> _selectedObjects;

    public EnumMultiSelectPredicate(IEnumerable<object> selectedValues)
    {
        if (selectedValues == null) throw new ArgumentNullException(nameof(selectedValues));

        _selected = new HashSet<string>(StringComparer.Ordinal);
        _selectedObjects = new HashSet<object>();
        foreach (var value in selectedValues)
        {
            if (value == null) continue;
            _selected.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            _selectedObjects.Add(value);
        }
    }

    /// <summary>已勾选值的字符串集合（只读视图）。</summary>
    public IReadOnlyCollection<string> SelectedValues => _selected;

    /// <inheritdoc />
    public bool Match(object? value)
    {
        if (value == null || _selected.Count == 0) return false;
        if (_selectedObjects.Contains(value)) return true;

        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return _selected.Contains(text);
    }
}
