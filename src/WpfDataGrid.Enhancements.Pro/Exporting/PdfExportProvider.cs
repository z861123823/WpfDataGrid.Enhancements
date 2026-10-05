using System;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using WpfDataGrid.Enhancements.Exporting;

namespace WpfDataGrid.Enhancements.Pro.Exporting;

/// <summary>
/// PDF 导出实现（Pro）：表格化渲染，面向打印 / 归档。基于 PdfSharp（MIT）。
/// 支持：A4 纵向 / 横向、表头行、数据分页、列宽均分自适应、页眉标题与页码。
/// </summary>
public sealed class PdfExportProvider : IExportProvider
{
    /// <summary>PDF 页面方向：纵向 / 横向。</summary>
    public enum PageOrientation
    {
        Portrait,
        Landscape
    }

    /// <summary>A4 页面尺寸（单位：磅）。</summary>
    public static class PageSize
    {
        public const double A4Width = 595.0;
        public const double A4Height = 842.0;
    }

    private const double Margin = 28.0;
    private const double HeaderHeight = 22.0;
    private const double RowHeight = 17.0;
    private const double FooterHeight = 20.0;

    public string FileExtension => ".pdf";

    /// <summary>导出方向（默认纵向）。</summary>
    public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;

    /// <summary>PDF 文档标题（页眉显示）。</summary>
    public string? Title { get; set; }

    public void Export(TableData tableData, Stream destination)
    {
        if (tableData is null) throw new ArgumentNullException(nameof(tableData));
        if (destination is null) throw new ArgumentNullException(nameof(destination));

        using (var document = new PdfDocument())
        {
            var pageWidth = Orientation == PageOrientation.Landscape ? PageSize.A4Height : PageSize.A4Width;
            var pageHeight = Orientation == PageOrientation.Landscape ? PageSize.A4Width : PageSize.A4Height;

            var page = document.AddPage();
            page.Width = XUnit.FromPoint(pageWidth);
            page.Height = XUnit.FromPoint(pageHeight);

            var contentTop = Margin + HeaderHeight;
            var contentBottom = pageHeight - Margin - FooterHeight;
            var contentWidth = pageWidth - 2 * Margin;

            var columns = tableData.Columns;
            var columnCount = Math.Max(1, columns.Count);
            var columnWidth = contentWidth / columnCount;

            var gfx = XGraphics.FromPdfPage(page);
            var headerFont = new XFont("Arial", 9, XFontStyleEx.Bold);
            var cellFont = new XFont("Arial", 8);
            var titleFont = new XFont("Arial", 11, XFontStyleEx.Bold);
            var brush = new XSolidBrush(XColor.FromArgb(40, 40, 40));
            var accentColor = XColor.FromArgb(45, 108, 223);
            var headerFill = XColor.FromArgb(235, 240, 250);
            var lineColor = XColor.FromArgb(200, 200, 200);

            var pageNumber = 1;
            var y = contentTop;

            DrawTitle(gfx, titleFont, brush, pageWidth, Margin, Title ?? GetDefaultTitle(tableData));

            // 表头行
            DrawHeaderRow(gfx, headerFont, accentColor, headerFill, brush, columns, Margin, contentWidth, columnWidth, ref y);

            foreach (var row in tableData.Rows)
            {
                if (y + RowHeight > contentBottom)
                {
                    // 分页
                    DrawFooter(gfx, cellFont, brush, pageWidth, pageHeight, Margin, pageNumber);
                    pageNumber++;
                    page = document.AddPage();
                    page.Width = XUnit.FromPoint(pageWidth);
                    page.Height = XUnit.FromPoint(pageHeight);
                    gfx = XGraphics.FromPdfPage(page);
                    y = contentTop;
                    DrawTitle(gfx, titleFont, brush, pageWidth, Margin, Title ?? GetDefaultTitle(tableData));
                    DrawHeaderRow(gfx, headerFont, accentColor, headerFill, brush, columns, Margin, contentWidth, columnWidth, ref y);
                }

                DrawDataRow(gfx, cellFont, brush, lineColor, row, Margin, contentWidth, columnWidth, y, RowHeight);
                y += RowHeight;
            }

            DrawFooter(gfx, cellFont, brush, pageWidth, pageHeight, Margin, pageNumber);

            document.Save(destination, false);
        }
    }

    private static string GetDefaultTitle(TableData tableData)
    {
        var name = string.IsNullOrWhiteSpace(tableData.WorksheetName) ? "DataGrid Export" : tableData.WorksheetName!;
        return name;
    }

    private static void DrawTitle(XGraphics gfx, XFont font, XBrush brush, double pageWidth, double margin, string title)
    {
        gfx.DrawString(title, font, brush, new XPoint(margin, margin + 12));
        gfx.DrawLine(new XPen(XColor.FromArgb(45, 108, 223), 1.2),
            new XPoint(margin, margin + 17),
            new XPoint(pageWidth - margin, margin + 17));
    }

    private static void DrawHeaderRow(
        XGraphics gfx,
        XFont font,
        XColor accentColor,
        XColor headerFill,
        XBrush textBrush,
        System.Collections.Generic.IReadOnlyList<string> columns,
        double margin,
        double contentWidth,
        double columnWidth,
        ref double y)
    {
        gfx.DrawRectangle(new XSolidBrush(headerFill), margin, y, contentWidth, HeaderHeight);
        for (var c = 0; c < columns.Count; c++)
        {
            gfx.DrawString(
                columns[c],
                font,
                textBrush,
                new XRect(margin + c * columnWidth + 3, y, columnWidth - 4, HeaderHeight),
                XStringFormats.TopLeft);
        }

        gfx.DrawLine(new XPen(accentColor, 1.2),
            new XPoint(margin, y + HeaderHeight),
            new XPoint(margin + contentWidth, y + HeaderHeight));
        y += HeaderHeight;
    }

    private static void DrawDataRow(
        XGraphics gfx,
        XFont font,
        XBrush brush,
        XColor lineColor,
        object?[] row,
        double margin,
        double contentWidth,
        double columnWidth,
        double y,
        double rowHeight)
    {
        for (var c = 0; c < row.Length; c++)
        {
            var text = FormatCell(row[c]);
            gfx.DrawString(
                text,
                font,
                brush,
                new XRect(margin + c * columnWidth + 3, y, columnWidth - 4, rowHeight),
                XStringFormats.TopLeft);
        }

        gfx.DrawLine(new XPen(lineColor, 0.6),
            new XPoint(margin, y + rowHeight),
            new XPoint(margin + contentWidth, y + rowHeight));
    }

    private static void DrawFooter(
        XGraphics gfx,
        XFont font,
        XBrush brush,
        double pageWidth,
        double pageHeight,
        double margin,
        int pageNumber)
    {
        gfx.DrawString(
            $"第 {pageNumber} 页",
            font,
            brush,
            new XPoint(pageWidth - margin - 40, pageHeight - margin - 4));
    }

    private static string FormatCell(object? value)
    {
        if (value is null) return string.Empty;
        if (value is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm:ss");
        return value.ToString() ?? string.Empty;
    }
}
