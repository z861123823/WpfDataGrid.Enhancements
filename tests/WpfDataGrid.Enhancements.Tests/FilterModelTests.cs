using System;
using WpfDataGrid.Enhancements.Filtering;
using Xunit;

namespace WpfDataGrid.Enhancements.Tests;

/// <summary>FilterModel 过滤组合逻辑单元测试：多列 AND、列内 OR、清除与事件。</summary>
public class FilterModelTests
{
    private sealed class TestRow
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public string Region { get; set; } = string.Empty;
    }

    [Fact]
    public void NewFilterModel_IsEmpty()
    {
        var model = new FilterModel();
        Assert.True(model.IsEmpty);
    }

    [Fact]
    public void GetOrAdd_CreatesColumnFilter()
    {
        var model = new FilterModel();
        var filter = model.GetOrAdd("Name");

        Assert.False(model.IsEmpty);
        Assert.Equal("Name", filter.ColumnName);
    }

    [Fact]
    public void GetOrAdd_SameColumn_ReturnsSameFilter()
    {
        var model = new FilterModel();
        Assert.Same(model.GetOrAdd("Name"), model.GetOrAdd("Name"));
    }

    [Fact]
    public void IsMatch_MultipleColumns_AndSemantics()
    {
        var model = new FilterModel();
        model.GetOrAdd("Name").AddPredicate(new TextContainsPredicate("Ali"));
        model.GetOrAdd("Age").AddPredicate(new NumericRangePredicate(20, 30));

        Assert.True(model.IsMatch(new TestRow { Name = "Alice", Age = 25 }));
        Assert.False(model.IsMatch(new TestRow { Name = "Bob", Age = 25 }));
        Assert.False(model.IsMatch(new TestRow { Name = "Alice", Age = 40 }));
    }

    [Fact]
    public void IsMatch_ColumnInnerOr_AnyPredicateMatches()
    {
        var model = new FilterModel();
        var filter = model.GetOrAdd("Name");
        filter.Combination = PredicateCombination.Or;
        filter.AddPredicate(new TextContainsPredicate("a"));
        filter.AddPredicate(new TextContainsPredicate("b"));

        Assert.True(model.IsMatch(new TestRow { Name = "cat" }));
        Assert.True(model.IsMatch(new TestRow { Name = "boat" }));
        Assert.False(model.IsMatch(new TestRow { Name = "xyz" }));
    }

    [Fact]
    public void IsMatch_NoPredicates_ReturnsTrue()
    {
        var model = new FilterModel();
        Assert.True(model.IsMatch(new TestRow { Name = "Alice", Age = 25 }));
    }

    [Fact]
    public void Clear_EmptiesModel_AndRaisesChanged()
    {
        var model = new FilterModel();
        model.GetOrAdd("Name").AddPredicate(new TextContainsPredicate("x"));
        var changedCount = 0;
        model.Changed += (_, _) => changedCount++;

        model.Clear();

        Assert.True(model.IsEmpty);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void Invalidate_RaisesChanged()
    {
        var model = new FilterModel();
        var changedCount = 0;
        model.Changed += (_, _) => changedCount++;

        model.Invalidate();

        Assert.Equal(1, changedCount);
    }
}
