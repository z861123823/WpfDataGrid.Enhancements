using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WpfDataGrid.Enhancements;
using Xunit;

namespace WpfDataGrid.Enhancements.Tests;

public class AutoFitTests
{
    [Fact]
    public void AutoFit_AttachedDefaults_AreSampleMode()
    {
        StaHelper.Run(() =>
        {
            var grid = new DataGrid();

            Assert.False(DataGridAutoFit.GetAutoFitColumns(grid));
            Assert.Equal(DataGridAutoFit.AutoFitMode.Sample, DataGridAutoFit.GetAutoFitMode(grid));
            Assert.Equal(100, DataGridAutoFit.GetSampleSize(grid));
            Assert.True(double.IsNaN(DataGridAutoFit.GetMinColumnWidth(grid)));
            Assert.True(double.IsNaN(DataGridAutoFit.GetMaxColumnWidth(grid)));
            Assert.False(DataGridAutoFit.GetStarLastColumn(grid));
        });
    }

    [Fact]
    public void Apply_WidensColumn_BeyondFixedWidth_WhenContentIsWider()
    {
        StaHelper.Run(() =>
        {
            var grid = new DataGrid { FontSize = 13 };
            var column = new DataGridTextColumn
            {
                Header = "Name",
                Binding = new Binding("Name"),
                Width = 20,
            };
            grid.Columns.Add(column);
            grid.ItemsSource = new List<RowItem>
            {
                new RowItem { Name = "这是一个非常非常非常长的产品名称 Product AAA" },
            };

            DataGridAutoFit.SetAutoFitColumns(grid, true);
            DataGridAutoFit.Apply(grid);

            Assert.True(column.Width.IsAbsolute);
            Assert.True(column.Width.Value > 20, $"期望列宽大于固定下限 20，实际 {column.Width.Value}");
        });
    }

    [Fact]
    public void Apply_KeepsFixedWidthAsLowerBound_WhenContentIsNarrow()
    {
        StaHelper.Run(() =>
        {
            var grid = new DataGrid { FontSize = 13 };
            var column = new DataGridTextColumn
            {
                Header = "单",
                Binding = new Binding("Name"),
                Width = 150,
            };
            grid.Columns.Add(column);
            grid.ItemsSource = new List<RowItem> { new RowItem { Name = "短" } };

            DataGridAutoFit.SetAutoFitColumns(grid, true);
            DataGridAutoFit.Apply(grid);

            Assert.True(column.Width.Value >= 150, $"期望保留固定下限 150，实际 {column.Width.Value}");
        });
    }

    [Fact]
    public void Apply_MinMaxConstraints_AreRespected()
    {
        StaHelper.Run(() =>
        {
            var grid = new DataGrid { FontSize = 13 };
            var column = new DataGridTextColumn
            {
                Header = "Name",
                Binding = new Binding("Name"),
            };
            grid.Columns.Add(column);
            grid.ItemsSource = new List<RowItem> { new RowItem { Name = "very long content padding more text" } };

            DataGridAutoFit.SetAutoFitColumns(grid, true);
            DataGridAutoFit.SetMinColumnWidth(grid, 300);
            DataGridAutoFit.SetMaxColumnWidth(grid, 120);
            DataGridAutoFit.Apply(grid);

            Assert.True(
                column.Width.Value >= 120 - 0.01 && column.Width.Value <= 300 + 0.01,
                $"期望宽度落在 [120, 300] 内，实际 {column.Width.Value}");
        });
    }

    [Fact]
    public void Apply_SampleMode_OnlyScansPrefix_AndFullIncludesLaterRows()
    {
        StaHelper.Run(() =>
        {
            var items = new List<RowItem>();
            for (int i = 0; i < 1000; i++)
            {
                items.Add(new RowItem { Name = "短" });
            }

            items[10] = new RowItem { Name = "这是一个超级超级超级超级长的文本，Sample 模式不应该采样到这一行" };

            var grid = new DataGrid { FontSize = 13 };
            var column = new DataGridTextColumn { Header = "N", Binding = new Binding("Name") };
            grid.Columns.Add(column);
            grid.ItemsSource = items;

            DataGridAutoFit.SetAutoFitColumns(grid, true);
            DataGridAutoFit.SetAutoFitMode(grid, DataGridAutoFit.AutoFitMode.Sample);
            DataGridAutoFit.SetSampleSize(grid, 10);
            DataGridAutoFit.Apply(grid);
            double sampleWidth = column.Width.Value;

            DataGridAutoFit.SetAutoFitMode(grid, DataGridAutoFit.AutoFitMode.Full);
            DataGridAutoFit.Apply(grid);

            Assert.True(column.Width.Value > sampleWidth, "Full 模式应包含超长行导致列更宽");
        });
    }

    [Fact]
    public void Apply_StarLastColumn_SetsLastColumnToStar()
    {
        StaHelper.Run(() =>
        {
            var grid = new DataGrid { FontSize = 13 };
            grid.Columns.Add(new DataGridTextColumn { Header = "A", Binding = new Binding("Name") });
            grid.Columns.Add(new DataGridTextColumn { Header = "B", Binding = new Binding("Amount") });
            grid.ItemsSource = new List<RowItem> { new RowItem { Name = "x", Amount = 1m } };

            DataGridAutoFit.SetAutoFitColumns(grid, true);
            DataGridAutoFit.SetStarLastColumn(grid, true);
            DataGridAutoFit.Apply(grid);

            Assert.True(grid.Columns[1].Width.IsStar);
            Assert.True(grid.Columns[0].Width.IsAbsolute);
        });
    }

    [Fact]
    public void Apply_EmptyGrid_DoesNotThrow()
    {
        StaHelper.Run(() =>
        {
            var grid = new DataGrid { FontSize = 13 };
            grid.Columns.Add(new DataGridTextColumn { Header = "A", Binding = new Binding("Name") });

            DataGridAutoFit.SetAutoFitColumns(grid, true);
            DataGridAutoFit.Apply(grid);

            // 无 ItemsSource 时 Apply 直接返回，不抛异常且列宽保持默认 Auto（不强制像素宽）
            Assert.True(grid.Columns[0].Width.IsAuto);
        });
    }
}
