using System;
using System.Collections.Generic;

namespace WpfDataGrid.Enhancements.Filtering;

/// <summary>
/// 过滤即将生效事件参数（<see cref="DataGridFilterBehavior.Filtering"/>）。
/// 订阅方可查看目标列与即将生效的谓词，并通过 <see cref="Cancel"/> 阻止过滤应用。
/// </summary>
public sealed class FilterChangingEventArgs : EventArgs
{
    public FilterChangingEventArgs(string columnName, IReadOnlyList<IFilterPredicate> predicates)
    {
        ColumnName = columnName ?? throw new ArgumentNullException(nameof(columnName));
        Predicates = predicates ?? throw new ArgumentNullException(nameof(predicates));
    }

    /// <summary>目标列绑定路径 / 列名。</summary>
    public string ColumnName { get; }

    /// <summary>即将生效的过滤谓词；空列表表示清除该列过滤。</summary>
    public IReadOnlyList<IFilterPredicate> Predicates { get; }

    /// <summary>设为 true 可取消本次过滤（视图保持不变）。</summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// 过滤已生效事件参数（<see cref="DataGridFilterBehavior.Filtered"/>）。
/// </summary>
public sealed class FilterChangedEventArgs : EventArgs
{
    public FilterChangedEventArgs(string columnName, IReadOnlyList<IFilterPredicate> predicates)
    {
        ColumnName = columnName ?? throw new ArgumentNullException(nameof(columnName));
        Predicates = predicates ?? throw new ArgumentNullException(nameof(predicates));
    }

    /// <summary>目标列绑定路径 / 列名。</summary>
    public string ColumnName { get; }

    /// <summary>已生效的过滤谓词；空列表表示该列过滤已被清除。</summary>
    public IReadOnlyList<IFilterPredicate> Predicates { get; }
}
