using System;
using System.Collections.Generic;

namespace WpfDataGrid.Enhancements.Filtering;

/// <summary>同列多个谓词的组合方式。</summary>
public enum PredicateCombination
{
    /// <summary>全部满足（默认）。</summary>
    And,

    /// <summary>任一满足。</summary>
    Or
}

/// <summary>
/// 单列过滤条件集合：一个列可叠加多个谓词（文本包含、数值区间、枚举多选等），
/// 谓词之间按 <see cref="Combination"/> 采用 AND / OR 语义组合。
/// </summary>
public sealed class ColumnFilter
{
    public ColumnFilter(string columnName)
    {
        ColumnName = columnName ?? throw new ArgumentNullException(nameof(columnName));
    }

    /// <summary>目标列名（与 DataGridColumn 绑定路径对应）。</summary>
    public string ColumnName { get; }

    /// <summary>该列已添加的过滤谓词。</summary>
    public List<IFilterPredicate> Predicates { get; } = new();

    /// <summary>谓词组合方式（默认 AND）。</summary>
    public PredicateCombination Combination { get; set; } = PredicateCombination.And;

    /// <summary>是否没有任何谓词。</summary>
    public bool IsEmpty => Predicates.Count == 0;

    public void AddPredicate(IFilterPredicate predicate)
    {
        Predicates.Add(predicate ?? throw new ArgumentNullException(nameof(predicate)));
    }

    public void ClearPredicates()
    {
        Predicates.Clear();
    }

    /// <summary>判断单元格值是否满足本列全部条件。</summary>
    public bool Matches(object? value)
    {
        if (Predicates.Count == 0) return true;

        if (Combination == PredicateCombination.And)
        {
            foreach (var predicate in Predicates)
            {
                if (!predicate.Match(value)) return false;
            }
            return true;
        }

        foreach (var predicate in Predicates)
        {
            if (predicate.Match(value)) return true;
        }
        return false;
    }
}
