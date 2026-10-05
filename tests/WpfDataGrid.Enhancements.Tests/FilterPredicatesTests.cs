using System;
using System.Collections.Generic;
using WpfDataGrid.Enhancements.Filtering;
using Xunit;

namespace WpfDataGrid.Enhancements.Tests;

/// <summary>过滤谓词各类型单元测试：文本 / 数值 / 日期 / 枚举多选。</summary>
public class FilterPredicatesTests
{
    #region 文本模糊匹配

    [Theory]
    [InlineData("Alice", "Ali", true)]
    [InlineData("Alice", "lic", true)]
    [InlineData("Alice", "ce", true)]
    [InlineData("Alice", "bob", false)]
    public void TextContains_MatchesSubstring(string value, string search, bool expected)
    {
        var predicate = new TextContainsPredicate(search);
        Assert.Equal(expected, predicate.Match(value));
    }

    [Fact]
    public void TextContains_IgnoreCaseByDefault()
    {
        var predicate = new TextContainsPredicate("ALICE");
        Assert.True(predicate.Match("alice smith"));
    }

    [Fact]
    public void TextContains_RespectsIgnoreCaseFlag()
    {
        var predicate = new TextContainsPredicate("ALICE", ignoreCase: false);
        Assert.False(predicate.Match("alice smith"));
    }

    [Fact]
    public void TextContains_EmptySearch_MatchesAll()
    {
        var predicate = new TextContainsPredicate("");
        Assert.True(predicate.Match(null));
        Assert.True(predicate.Match("anything"));
    }

    [Theory]
    [InlineData(TextMatchMode.StartsWith, "Alice", "Ali", true)]
    [InlineData(TextMatchMode.StartsWith, "Alice", "ce", false)]
    [InlineData(TextMatchMode.EndsWith, "Alice", "ce", true)]
    [InlineData(TextMatchMode.EndsWith, "Alice", "Ali", false)]
    public void TextContains_SupportsPrefixAndSuffix(TextMatchMode mode, string value, string search, bool expected)
    {
        var predicate = new TextContainsPredicate(search, mode: mode);
        Assert.Equal(expected, predicate.Match(value));
    }

    [Fact]
    public void TextContains_NullValue_ReturnsFalse()
    {
        var predicate = new TextContainsPredicate("x");
        Assert.False(predicate.Match(null));
    }

    #endregion

    #region 数值范围

    [Theory]
    [InlineData(10.0, true)]
    [InlineData(15.0, true)]
    [InlineData(20.0, true)]
    [InlineData(9.0, false)]
    [InlineData(21.0, false)]
    public void NumericRange_Between_Inclusive(double value, bool expected)
    {
        var predicate = new NumericRangePredicate(10, 20);
        Assert.Equal(expected, predicate.Match(value));
    }

    [Fact]
    public void NumericRange_Equals()
    {
        var predicate = new NumericRangePredicate(42, null, NumericCompareMode.Equals);
        Assert.True(predicate.Match(42));
        Assert.False(predicate.Match(43));
    }

    [Fact]
    public void NumericRange_GreaterThan()
    {
        var predicate = new NumericRangePredicate(100, null, NumericCompareMode.GreaterThan);
        Assert.True(predicate.Match(101));
        Assert.False(predicate.Match(100));
    }

    [Fact]
    public void NumericRange_LessThan()
    {
        var predicate = new NumericRangePredicate(100, null, NumericCompareMode.LessThan);
        Assert.True(predicate.Match(99));
        Assert.False(predicate.Match(100));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public void NumericRange_NonNumeric_ReturnsFalse(object? value)
    {
        var predicate = new NumericRangePredicate(0, 100);
        Assert.False(predicate.Match(value));
    }

    #endregion

    #region 日期范围

    [Fact]
    public void DateRange_Between_Inclusive()
    {
        var predicate = new DateRangePredicate(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 12, 31));

        Assert.True(predicate.Match(new DateTime(2024, 1, 1)));
        Assert.True(predicate.Match(new DateTime(2024, 6, 15)));
        Assert.True(predicate.Match(new DateTime(2024, 12, 31)));
        Assert.False(predicate.Match(new DateTime(2023, 12, 31)));
        Assert.False(predicate.Match(new DateTime(2025, 1, 1)));
    }

    [Fact]
    public void DateRange_OnlyStart()
    {
        var predicate = new DateRangePredicate(new DateTime(2024, 6, 1), null);
        Assert.True(predicate.Match(new DateTime(2024, 6, 1)));
        Assert.False(predicate.Match(new DateTime(2024, 5, 31)));
    }

    [Fact]
    public void DateRange_OnlyEnd()
    {
        var predicate = new DateRangePredicate(null, new DateTime(2024, 6, 1));
        Assert.True(predicate.Match(new DateTime(2024, 6, 1)));
        Assert.False(predicate.Match(new DateTime(2024, 6, 2)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-date")]
    public void DateRange_InvalidValue_ReturnsFalse(object? value)
    {
        var predicate = new DateRangePredicate(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));
        Assert.False(predicate.Match(value));
    }

    #endregion

    #region 枚举 / 布尔多选

    private enum Status { New, InProgress, Done }

    [Fact]
    public void EnumMultiSelect_AnySelectedMatches()
    {
        var predicate = new EnumMultiSelectPredicate(new object[] { Status.New, Status.Done });
        Assert.True(predicate.Match(Status.New));
        Assert.True(predicate.Match(Status.Done));
        Assert.False(predicate.Match(Status.InProgress));
    }

    [Fact]
    public void EnumMultiSelect_SupportsBoolean()
    {
        var predicate = new EnumMultiSelectPredicate(new object[] { true });
        Assert.True(predicate.Match(true));
        Assert.False(predicate.Match(false));
    }

    [Fact]
    public void EnumMultiSelect_EmptySelection_ReturnsFalse()
    {
        var predicate = new EnumMultiSelectPredicate(new List<object>());
        Assert.False(predicate.Match(Status.New));
        Assert.False(predicate.Match(null));
    }

    #endregion
}
