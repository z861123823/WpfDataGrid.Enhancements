using System;
using System.IO;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Xaml.Behaviors;
using WpfDataGrid.Enhancements.Exporting;
using WpfDataGrid.Enhancements.Filtering;
using Xunit;

namespace WpfDataGrid.Enhancements.Tests;

/// <summary>
/// 事件钩子测试：过滤 Filtering（可取消）/ Filtered，导出 Exporting（可取消）/ Exported。
/// </summary>
public class EventHookTests
{
    private static DataGrid CreateGrid()
    {
        var dataGrid = new DataGrid();
        dataGrid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding("Name") });
        dataGrid.ItemsSource = new[]
        {
            new RowItem { Name = "Alpha" },
            new RowItem { Name = "Beta" },
        };
        return dataGrid;
    }

    [Fact]
    public void Filtering_Cancel_PreventsApplication_AndSkipsFiltered()
    {
        StaHelper.Run(() =>
        {
            var dataGrid = CreateGrid();
            var behavior = new DataGridFilterBehavior();
            Interaction.GetBehaviors(dataGrid).Add(behavior);

            var filteredRaised = false;
            behavior.Filtered += (_, _) => filteredRaised = true;
            behavior.Filtering += (_, e) => e.Cancel = true;

            var applied = behavior.ApplyColumnFilter("Name", new[] { new LengthPredicate(4) });

            Assert.False(applied);
            Assert.False(behavior.FilterModel!.ColumnFilters.ContainsKey("Name"));
            Assert.False(filteredRaised);
        });
    }

    [Fact]
    public void Filtering_NotCancelled_AppliesPredicate_AndRaisesFiltered()
    {
        StaHelper.Run(() =>
        {
            var dataGrid = CreateGrid();
            var behavior = new DataGridFilterBehavior();
            Interaction.GetBehaviors(dataGrid).Add(behavior);

            string? raisedColumn = null;
            int raisedPredicateCount = -1;
            behavior.Filtered += (_, e) =>
            {
                raisedColumn = e.ColumnName;
                raisedPredicateCount = e.Predicates.Count;
            };

            var applied = behavior.ApplyColumnFilter("Name", new[] { new LengthPredicate(4) });

            Assert.True(applied);
            Assert.Single(behavior.FilterModel!.ColumnFilters["Name"].Predicates);
            Assert.Equal("Name", raisedColumn);
            Assert.Equal(1, raisedPredicateCount);
        });
    }

    [Fact]
    public void ClearColumnFilter_RaisesFiltered_WithEmptyPredicates()
    {
        StaHelper.Run(() =>
        {
            var dataGrid = CreateGrid();
            var behavior = new DataGridFilterBehavior();
            Interaction.GetBehaviors(dataGrid).Add(behavior);
            behavior.ApplyColumnFilter("Name", new[] { new LengthPredicate(4) });

            int raisedEmpty = -1;
            behavior.Filtered += (_, e) => raisedEmpty = e.Predicates.Count;

            Assert.True(behavior.ClearColumnFilter("Name"));
            Assert.Equal(0, raisedEmpty);
        });
    }

    [Fact]
    public void Exporting_Cancel_PreventsFileCreation_AndSkipsExported()
    {
        StaHelper.Run(() =>
        {
            var dataGrid = CreateGrid();
            var behavior = new DataGridExportBehavior();
            behavior.Providers.Add(new JsonExportProvider());
            Interaction.GetBehaviors(dataGrid).Add(behavior);

            var exportedRaised = false;
            behavior.Exported += (_, _) => exportedRaised = true;
            behavior.Exporting += (_, e) => e.Cancel = true;

            var path = Path.Combine(Path.GetTempPath(), $"export_cancel_{Guid.NewGuid():N}.json");
            try
            {
                behavior.Export(ExportScope.AllData, path);
                Assert.False(File.Exists(path));
                Assert.False(exportedRaised);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        });
    }

    [Fact]
    public void Exporting_Exported_RaisedWithRowCount_AndFileWritten()
    {
        StaHelper.Run(() =>
        {
            var dataGrid = CreateGrid();
            var behavior = new DataGridExportBehavior();
            behavior.Providers.Add(new JsonExportProvider());
            Interaction.GetBehaviors(dataGrid).Add(behavior);

            string? exportedPath = null;
            int exportedRows = -1;
            behavior.Exported += (_, e) =>
            {
                exportedPath = e.FilePath;
                exportedRows = e.RowCount;
            };

            var path = Path.Combine(Path.GetTempPath(), $"export_ok_{Guid.NewGuid():N}.json");
            try
            {
                behavior.Export(ExportScope.AllData, path);
                Assert.True(File.Exists(path));
                Assert.Equal(path, exportedPath);
                Assert.Equal(2, exportedRows);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        });
    }
}
