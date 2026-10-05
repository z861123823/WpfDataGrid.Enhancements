namespace WpfDataGrid.Enhancements.Filtering;

/// <summary>
/// 过滤谓词接口：描述"单元格值是否满足某个过滤条件"。
/// 开源版内置：文本包含、数值等于/区间、枚举多选；Pro 版扩展正则等高级谓词。
/// </summary>
public interface IFilterPredicate
{
    /// <summary>判断单元格值是否满足本谓词。</summary>
    /// <param name="value">单元格原始值；为 null 时由实现决定是否匹配。</param>
    /// <returns>true 表示匹配（保留该行）。</returns>
    bool Match(object? value);
}
