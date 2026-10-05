using System.IO;
using System.Linq;
using System.Text;
using WpfDataGrid.Enhancements.Exporting;
using Xunit;

namespace WpfDataGrid.Enhancements.Tests;

/// <summary>CSV 导出内容与转义规则单元测试（UTF-8 BOM、RFC 4180 转义）。</summary>
public class CsvExportProviderTests
{
    private static TableData SampleTable()
    {
        return new TableData(
            new[] { "Name", "Age" },
            new object?[][]
            {
                new object?[] { "Alice", 30 },
                new object?[] { "Bob, Jr.", 21 },
                new object?[] { "Say \"Hi\"", 5 },
                new object?[] { "Line1\nLine2", 7 },
                new object?[] { "尾逗号,", 8 }
            });
    }

    [Fact]
    public void Provider_ExposesCsvExtension()
    {
        var provider = new CsvExportProvider();
        Assert.Equal(".csv", provider.FileExtension);
    }

    [Fact]
    public void Export_WritesUtf8Bom()
    {
        var provider = new CsvExportProvider();
        using var stream = new MemoryStream();

        provider.Export(SampleTable(), stream);

        var bytes = stream.ToArray();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
    }

    [Fact]
    public void Export_WritesHeaderAndRows()
    {
        var provider = new CsvExportProvider();
        using var stream = new MemoryStream();

        provider.Export(SampleTable(), stream);

        var text = Encoding.UTF8.GetString(stream.ToArray()).TrimStart('\uFEFF');
        var lines = text.TrimEnd('\r', '\n').Split('\n');
        Assert.Equal("Name,Age", lines[0].TrimEnd('\r'));
        Assert.Equal("Alice,30", lines[1].TrimEnd('\r'));
    }

    [Fact]
    public void Export_EscapesFieldWithComma()
    {
        var provider = new CsvExportProvider();
        using var stream = new MemoryStream();

        provider.Export(SampleTable(), stream);

        var text = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("\"Bob, Jr.\",21", text);
    }

    [Fact]
    public void Export_EscapesEmbeddedQuotesByDoubling()
    {
        var provider = new CsvExportProvider();
        using var stream = new MemoryStream();

        provider.Export(SampleTable(), stream);

        var text = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("\"Say \"\"Hi\"\"\",5", text);
    }

    [Fact]
    public void Export_EscapesEmbeddedNewline()
    {
        var provider = new CsvExportProvider();
        using var stream = new MemoryStream();

        provider.Export(SampleTable(), stream);

        var text = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("\"Line1\nLine2\",7", text);
    }

    [Fact]
    public void Export_NoBom_WhenDisabled()
    {
        var provider = new CsvExportProvider { WriteBom = false };
        using var stream = new MemoryStream();

        provider.Export(SampleTable(), stream);

        var bytes = stream.ToArray();
        Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
    }

    [Fact]
    public void Export_ColumnCountMatchesRowCellCount()
    {
        var provider = new CsvExportProvider();
        using var stream = new MemoryStream();

        var table = new TableData(new[] { "A", "B", "C" }, new object?[][] { new object?[] { 1, 2, 3 } });
        provider.Export(table, stream);

        var text = Encoding.UTF8.GetString(stream.ToArray());
        var dataLine = text.TrimEnd('\r', '\n').Split('\n').Last();
        Assert.Equal("1,2,3", dataLine);
    }
}
