using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using WpfDataGrid.Enhancements.Exporting;
using WpfDataGrid.Enhancements.Pro.Exporting;
using Xunit;

namespace WpfDataGrid.Enhancements.Tests;

/// <summary>xlsx 导出文件结构单元测试（基于 Sylvan.Data.Excel 的真实 OOXML 输出）。</summary>
public class XlsxExportProviderTests
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static TableData SampleTable()
    {
        return new TableData(
            new[] { "订单号", "客户", "金额" },
            new object?[][]
            {
                new object?[] { 1001, "张三", 199.5m },
                new object?[] { 1002, "李四", 88m },
                new object?[] { 1003, "王五", 320m }
            })
        {
            WorksheetName = "订单明细"
        };
    }

    [Fact]
    public void Provider_ExposesXlsxExtension()
    {
        var provider = new XlsxExportProvider();
        Assert.Equal(".xlsx", provider.FileExtension);
    }

    [Fact]
    public void Export_ProducesValidXlsxPackage()
    {
        var provider = new XlsxExportProvider();
        using var stream = new MemoryStream();

        provider.Export(SampleTable(), stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entryNames = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("[Content_Types].xml", entryNames);
        Assert.Contains("xl/workbook.xml", entryNames);
        Assert.Contains("xl/worksheets/sheet1.xml", entryNames);
    }

    [Fact]
    public void Export_WorksheetName_AppearsInWorkbookXml()
    {
        var provider = new XlsxExportProvider();
        using var stream = new MemoryStream();

        provider.Export(SampleTable(), stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var workbookEntry = zip.GetEntry("xl/workbook.xml");
        Assert.NotNull(workbookEntry);

        using var reader = new StreamReader(workbookEntry!.Open());
        var xml = reader.ReadToEnd();

        Assert.Contains("订单明细", xml);
    }

    [Fact]
    public void Export_DefaultWorksheetName_WhenNotSpecified()
    {
        var provider = new XlsxExportProvider();
        var table = new TableData(new[] { "A" }, new object?[][] { new object?[] { 1 } });
        using var stream = new MemoryStream();

        provider.Export(table, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("xl/workbook.xml")!.Open());
        var xml = reader.ReadToEnd();

        Assert.Contains("Sheet1", xml);
    }

    [Fact]
    public void Export_ProviderWorksheetName_OverridesTableName()
    {
        var provider = new XlsxExportProvider { WorksheetName = "覆盖表名" };
        var table = new TableData(new[] { "A" }, new object?[][] { new object?[] { 1 } })
        {
            WorksheetName = "原始表名"
        };
        using var stream = new MemoryStream();

        provider.Export(table, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("xl/workbook.xml")!.Open());
        var xml = reader.ReadToEnd();

        Assert.Contains("覆盖表名", xml);
        Assert.DoesNotContain("原始表名", xml);
    }

    // ---- 导出美化：样式断言（解压 OOXML 检查样式表与工作表） ----

    private static XDocument ReadXmlEntry(ZipArchive zip, string entryName)
    {
        using var reader = new StreamReader(zip.GetEntry(entryName)!.Open());
        return XDocument.Parse(reader.ReadToEnd());
    }

    /// <summary>按样式索引读取 cellXfs 中的 xf。</summary>
    private static XElement CellXf(XDocument styles, string styleIndex)
    {
        var xfs = styles.Descendants(M + "cellXfs").First();
        return xfs.Elements(M + "xf").ElementAt(int.Parse(styleIndex, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Export_HeaderRow_BoldBackgroundBorder()
    {
        var provider = new XlsxExportProvider();
        using var stream = new MemoryStream();

        provider.Export(SampleTable(), stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var sheet = ReadXmlEntry(zip, "xl/worksheets/sheet1.xml");
        var styles = ReadXmlEntry(zip, "xl/styles.xml");

        var headerRow = sheet.Descendants(M + "row").First();
        var cells = headerRow.Elements(M + "c").ToList();
        Assert.NotEmpty(cells);

        foreach (var c in cells)
        {
            var s = c.Attribute("s")?.Value;
            Assert.NotNull(s);
            var xf = CellXf(styles, s!);

            // 加粗：fontId 指向含 <b/> 的 font。
            var fontId = int.Parse(xf.Attribute("fontId")!.Value, CultureInfo.InvariantCulture);
            var font = styles.Descendants(M + "font").ElementAt(fontId);
            Assert.NotNull(font.Element(M + "b"));

            // 背景：fillId 指向 solid patternFill。
            var fillId = int.Parse(xf.Attribute("fillId")!.Value, CultureInfo.InvariantCulture);
            var fill = styles.Descendants(M + "fill").ElementAt(fillId);
            var pattern = fill.Element(M + "patternFill");
            Assert.NotNull(pattern);
            Assert.Equal("solid", pattern!.Attribute("patternType")?.Value);

            // 边框：borderId 指向 thin 边框。
            var borderId = int.Parse(xf.Attribute("borderId")!.Value, CultureInfo.InvariantCulture);
            var border = styles.Descendants(M + "border").ElementAt(borderId);
            Assert.Equal("thin", border.Element(M + "left")?.Attribute("style")?.Value);
        }
    }

    [Fact]
    public void Export_DataCells_TextLeftNumberRight()
    {
        var provider = new XlsxExportProvider();
        using var stream = new MemoryStream();

        provider.Export(SampleTable(), stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var sheet = ReadXmlEntry(zip, "xl/worksheets/sheet1.xml");
        var styles = ReadXmlEntry(zip, "xl/styles.xml");

        // SampleTable: 订单号(数值) / 客户(文本) / 金额(数值)。
        var dataRow = sheet.Descendants(M + "row").ElementAt(1);
        var cells = dataRow.Elements(M + "c").ToList();
        Assert.Equal(3, cells.Count);

        var xfNumber = CellXf(styles, cells[0].Attribute("s")!.Value);
        Assert.Equal("right", xfNumber.Element(M + "alignment")?.Attribute("horizontal")?.Value);

        var xfText = CellXf(styles, cells[1].Attribute("s")!.Value);
        Assert.Equal("left", xfText.Element(M + "alignment")?.Attribute("horizontal")?.Value);

        var xfNumber2 = CellXf(styles, cells[2].Attribute("s")!.Value);
        Assert.Equal("right", xfNumber2.Element(M + "alignment")?.Attribute("horizontal")?.Value);
    }

    [Fact]
    public void Export_AutoColumnWidth_WritesColsWithinBounds()
    {
        var provider = new XlsxExportProvider();
        using var stream = new MemoryStream();

        provider.Export(SampleTable(), stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var sheet = ReadXmlEntry(zip, "xl/worksheets/sheet1.xml");

        var cols = sheet.Descendants(M + "cols").First();
        var colList = cols.Elements(M + "col").ToList();
        Assert.Equal(3, colList.Count);

        foreach (var col in colList)
        {
            var width = double.Parse(col.Attribute("width")!.Value, CultureInfo.InvariantCulture);
            Assert.InRange(width, XlsxExportProvider.DefaultMinColumnWidth, XlsxExportProvider.DefaultMaxColumnWidth);
            Assert.Equal("1", col.Attribute("customWidth")?.Value);
        }
    }

    [Fact]
    public void Export_AutoColumnWidth_ClampsToMax()
    {
        var provider = new XlsxExportProvider();
        var table = new TableData(
            new[] { "长文本" },
            new object?[][] { new object?[] { new string('长', 200) } });
        using var stream = new MemoryStream();

        provider.Export(table, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var sheet = ReadXmlEntry(zip, "xl/worksheets/sheet1.xml");

        var col = sheet.Descendants(M + "cols").First().Elements(M + "col").Single();
        var width = double.Parse(col.Attribute("width")!.Value, CultureInfo.InvariantCulture);
        Assert.Equal(XlsxExportProvider.DefaultMaxColumnWidth, width);
    }

    [Fact]
    public void Export_FreezeHeaderRow_DefaultTrueKeepsPane()
    {
        var provider = new XlsxExportProvider();
        using var stream = new MemoryStream();

        provider.Export(SampleTable(), stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var sheet = ReadXmlEntry(zip, "xl/worksheets/sheet1.xml");

        var pane = sheet.Descendants(M + "pane").FirstOrDefault();
        Assert.NotNull(pane);
        Assert.Equal("1", pane!.Attribute("ySplit")?.Value);
        Assert.Equal("frozen", pane.Attribute("state")?.Value);
    }

    [Fact]
    public void Export_FreezeHeaderRow_DisabledRemovesPane()
    {
        var provider = new XlsxExportProvider { FreezeHeaderRow = false };
        using var stream = new MemoryStream();

        provider.Export(SampleTable(), stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var sheet = ReadXmlEntry(zip, "xl/worksheets/sheet1.xml");

        Assert.Empty(sheet.Descendants(M + "pane"));
    }

    [Fact]
    public void Export_DateColumn_KeepsSylvanDateFormat()
    {
        var provider = new XlsxExportProvider();
        var table = new TableData(
            new[] { "下单日期" },
            new object?[][] { new object?[] { new DateTime(2026, 1, 1) } });
        using var stream = new MemoryStream();

        provider.Export(table, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var sheet = ReadXmlEntry(zip, "xl/worksheets/sheet1.xml");

        // 日期单元格保留 Sylvan 内建样式（s=1 为日期 numFmt）。
        var cell = sheet.Descendants(M + "c").ElementAt(1);
        var s = cell.Attribute("s")?.Value;
        Assert.NotNull(s);
        Assert.Equal("1", s);
    }
}
