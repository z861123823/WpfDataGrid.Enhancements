using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Xaml.Behaviors;
using WpfDataGrid.Enhancements.Exporting;
using Xunit;

namespace WpfDataGrid.Enhancements.Tests;

/// <summary>
/// 自定义导出器示例：JSON 序列化。
/// 实现 <see cref="IExportProvider"/> 后加入行为的 <see cref="DataGridExportBehavior.Providers"/>，
/// 导出时按文件扩展名自动路由。
/// （完整示例见 docs/guide/extensibility.md 第二步）
/// </summary>
public sealed class JsonExportProvider : IExportProvider
{
    public string FileExtension => ".json";

    public void Export(TableData tableData, Stream destination)
    {
        var payload = new
        {
            columns = tableData.Columns,
            rows = tableData.Rows.Select(r => r.Select(v => v?.ToString()).ToArray()).ToArray(),
        };
        JsonSerializer.Serialize(destination, payload);
    }
}

public class CustomExportProviderTests
{
    [Fact]
    public void JsonProvider_WritesColumnsAndRows()
    {
        var table = new TableData(
            new[] { "Name", "Amount" },
            new object?[][]
            {
                new object?[] { "Alpha", 12m },
                new object?[] { "Beta", 7.5m },
            });

        using var stream = new MemoryStream();
        new JsonExportProvider().Export(table, stream);
        stream.Position = 0;
        var json = new StreamReader(stream).ReadToEnd();

        Assert.Contains("\"Name\"", json);
        Assert.Contains("Alpha", json);
        Assert.Contains("Beta", json);
    }

    [Fact]
    public void CustomProvider_RegisteredInBehavior_IsRoutedByExtension()
    {
        StaHelper.Run(() =>
        {
            var dataGrid = new DataGrid();
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding("Name") });
            dataGrid.Columns.Add(new DataGridTextColumn { Header = "Amount", Binding = new Binding("Amount") });
            dataGrid.ItemsSource = new[]
            {
                new RowItem { Name = "Alpha", Amount = 12m },
                new RowItem { Name = "Beta", Amount = 7.5m },
            };

            var behavior = new DataGridExportBehavior();
            behavior.Providers.Add(new JsonExportProvider());
            Interaction.GetBehaviors(dataGrid).Add(behavior);

            var path = Path.Combine(Path.GetTempPath(), $"export_test_{Guid.NewGuid():N}.json");
            try
            {
                behavior.Export(ExportScope.AllData, path);

                var json = File.ReadAllText(path);
                Assert.Contains("\"Name\"", json);
                Assert.Contains("Alpha", json);
                Assert.Contains("Beta", json);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        });
    }
}
