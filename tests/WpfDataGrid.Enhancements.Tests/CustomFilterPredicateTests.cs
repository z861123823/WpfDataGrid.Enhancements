using System;
using WpfDataGrid.Enhancements.Filtering;
using Xunit;

namespace WpfDataGrid.Enhancements.Tests;

/// <summary>
/// 自定义过滤谓词示例：按字符串长度过滤。
/// 实现 <see cref="IFilterPredicate"/> 后即可接入 <see cref="FilterModel"/>。
/// （完整示例见 docs/guide/extensibility.md 第一步）
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

public class CustomFilterPredicateTests
{
    [Theory]
    [InlineData("Alpha", 3, true)]
    [InlineData("A", 3, false)]
    [InlineData(null, 3, false)]
    public void LengthPredicate_MatchesByStringLength(string? value, int minLength, bool expected)
    {
        Assert.Equal(expected, new LengthPredicate(minLength).Match(value));
    }

    [Fact]
    public void CustomPredicate_IntegratedIntoFilterModel_AndSemantics()
    {
        var model = new FilterModel();
        var column = model.GetOrAdd("Name");
        column.AddPredicate(new LengthPredicate(4));
        model.Invalidate();

        Assert.True(model.IsMatch(new RowItem { Name = "Alpha" }));
        Assert.False(model.IsMatch(new RowItem { Name = "Bob" }));
    }

    [Fact]
    public void CustomPredicate_OrCombination_MatchesAny()
    {
        var model = new FilterModel();
        var column = model.GetOrAdd("Name");
        column.Combination = PredicateCombination.Or;
        column.AddPredicate(new LengthPredicate(10));          // 长条件：两个样例都不满足
        column.AddPredicate(new TextContainsPredicate("bob")); // 包含 bob 即通过
        model.Invalidate();

        Assert.True(model.IsMatch(new RowItem { Name = "bob" }));
        Assert.False(model.IsMatch(new RowItem { Name = "alice" }));
    }
}
