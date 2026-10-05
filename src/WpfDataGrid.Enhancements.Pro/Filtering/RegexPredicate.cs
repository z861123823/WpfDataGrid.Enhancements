using System;
using System.Text.RegularExpressions;
using WpfDataGrid.Enhancements.Filtering;

namespace WpfDataGrid.Enhancements.Pro.Filtering;

/// <summary>
/// 正则过滤谓词（Pro）：按正则表达式匹配单元格文本。
/// </summary>
public sealed class RegexPredicate : IFilterPredicate
{
    private readonly Regex _regex;

    public RegexPredicate(string pattern)
    {
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        _regex = new Regex(pattern, RegexOptions.CultureInvariant);
    }

    /// <summary>正则表达式模式。</summary>
    public string Pattern { get; }

    /// <inheritdoc />
    public bool Match(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        return _regex.IsMatch(text);
    }
}
