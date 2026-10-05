using System;
using System.Collections.Generic;
using WpfDataGrid.Enhancements.Infrastructure;

namespace WpfDataGrid.Enhancements.Filtering;

/// <summary>
/// 过滤状态模型：列名 → <see cref="ColumnFilter"/> 条件集合，支持多列组合过滤。
/// 与 DataGrid 的 ICollectionView.Filter 协作，实现列间 AND 语义的组合筛选。
/// 模型内容变化时触发 <see cref="Changed"/>，由关联行为刷新视图。
/// </summary>
public sealed class FilterModel
{
    private readonly Dictionary<string, ColumnFilter> _columnFilters = new(StringComparer.Ordinal);

    /// <summary>模型内容变化时触发（新增 / 清除列条件等）。</summary>
    public event EventHandler? Changed;

    /// <summary>当前全部列过滤条件（只读视图）。</summary>
    public IReadOnlyDictionary<string, ColumnFilter> ColumnFilters => _columnFilters;

    /// <summary>是否没有任何过滤条件。</summary>
    public bool IsEmpty => _columnFilters.Count == 0;

    /// <summary>获取或创建指定列的过滤条件集合。新增列本身不触发 <see cref="Changed"/>，由调用方显式 <see cref="Invalidate"/>。</summary>
    public ColumnFilter GetOrAdd(string columnName)
    {
        if (!_columnFilters.TryGetValue(columnName, out var filter))
        {
            filter = new ColumnFilter(columnName);
            _columnFilters.Add(columnName, filter);
        }

        return filter;
    }

    /// <summary>一键清除全部过滤条件。</summary>
    public void Clear()
    {
        if (_columnFilters.Count == 0) return;
        _columnFilters.Clear();
        RaiseChanged();
    }

    /// <summary>手动通知模型已变化（谓词增删后调用）。</summary>
    public void Invalidate() => RaiseChanged();

    /// <summary>
    /// 判断数据行是否满足全部列过滤条件（列间 AND 语义；列内按各 <see cref="ColumnFilter.Combination"/>）。
    /// </summary>
    /// <param name="item">DataGrid 数据行对象。</param>
    public bool IsMatch(object item)
    {
        if (item == null) return true;

        foreach (var pair in _columnFilters)
        {
            var columnFilter = pair.Value;
            if (columnFilter.IsEmpty) continue;
            if (!columnFilter.Matches(PropertyAccessor.GetValue(item, pair.Key))) return false;
        }

        return true;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
