using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Xaml.Behaviors;
using WpfDataGrid.Enhancements.Filtering;
using Xunit;

namespace WpfDataGrid.Enhancements.Tests;

/// <summary>
/// 自定义过滤面板工厂测试：验证 <see cref="DataGridFilterBehavior.FilterPanelFactory"/>
/// 可注入自定义 <see cref="IFilterPanel"/> 面板（B 方案扩展点）。
/// </summary>
public class FilterPanelFactoryTests
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

    private sealed class TestPanel : UserControl, IFilterPanel
    {
        public int FocusCalls { get; private set; }

        public int CloseCalls { get; private set; }

        public void FocusInput() => FocusCalls++;

        public void Close() => CloseCalls++;
    }

    private sealed class NonUiPanel : IFilterPanel
    {
        public void FocusInput()
        {
        }

        public void Close()
        {
        }
    }

    [Fact]
    public void FilterPanelFactory_Inject_CustomPanel_UsedForHeaders()
    {
        StaHelper.Run(() =>
        {
            var dataGrid = CreateGrid();
            var behavior = new DataGridFilterBehavior();
            var createdPanels = new List<IFilterPanel>();

            behavior.FilterPanelFactory = (owner, columnName, propertyType, closeAction) =>
            {
                Assert.NotNull(owner);
                Assert.Equal("Name", columnName);
                var panel = new TestPanel();
                createdPanels.Add(panel);
                return panel;
            };

            Interaction.GetBehaviors(dataGrid).Add(behavior);

            Assert.Single(createdPanels);
            Assert.All(createdPanels, p => Assert.IsType<TestPanel>(p));
        });
    }

    [Fact]
    public void FilterPanelFactory_CustomPanel_FocusAndCloseWork()
    {
        StaHelper.Run(() =>
        {
            var dataGrid = CreateGrid();
            var behavior = new DataGridFilterBehavior();
            var panel = new TestPanel();
            behavior.FilterPanelFactory = (_, _, _, _) => panel;

            Interaction.GetBehaviors(dataGrid).Add(behavior);

            panel.FocusInput();
            panel.Close();
            Assert.Equal(1, panel.FocusCalls);
            Assert.Equal(1, panel.CloseCalls);
        });
    }

    [Fact]
    public void FilterPanelFactory_NonUiElement_ThrowsInvalidOperation()
    {
        StaHelper.Run(() =>
        {
            var dataGrid = CreateGrid();
            var behavior = new DataGridFilterBehavior();
            behavior.FilterPanelFactory = (_, _, _, _) => new NonUiPanel();

            Assert.Throws<InvalidOperationException>(() => Interaction.GetBehaviors(dataGrid).Add(behavior));
        });
    }
}
