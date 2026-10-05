using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace WpfDataGrid.Enhancements.Pro.Exporting;

/// <summary>
/// xlsx 样式后处理：对 Sylvan.Data.Excel 流式输出的 OOXML 包注入表头样式、自动列宽、
/// 单元格对齐与冻结窗格控制。
/// 仅修改 <c>xl/styles.xml</c> 与工作表 xml；工作表采用 XmlReader/XmlWriter 流式改造，
/// 单行内存占用（每行处理完即释放），不破坏 Sylvan 流式写入的低内存特性。
/// </summary>
internal static class XlsxFormatting
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string StylesEntryName = "xl/styles.xml";
    private const string SheetEntryPrefix = "xl/worksheets/sheet";

    private enum ColumnKind
    {
        Text,
        Numeric,
        Date
    }

    /// <summary>
    /// 对已由 Sylvan 写入完成的 xlsx 文件执行样式后处理。
    /// </summary>
    public static void Apply(
        string xlsxPath,
        IReadOnlyList<string> columns,
        IReadOnlyList<object?[]> rows,
        bool styleHeader,
        bool autoColumnWidth,
        bool freezeHeaderRow,
        int minColumnWidth,
        int maxColumnWidth)
    {
        var columnKinds = AnalyzeColumns(columns, rows);
        var columnWidths = ComputeColumnWidths(columns, rows, columnKinds, minColumnWidth, maxColumnWidth);

        string sheetEntryName;
        string tempSheetPath = Path.Combine(Path.GetDirectoryName(xlsxPath)!, Path.GetFileName(xlsxPath) + ".sheet.tmp.xml");

        using (var zip = ZipFile.Open(xlsxPath, ZipArchiveMode.Update))
        {
            var sheetEntry = zip.Entries.FirstOrDefault(e =>
                e.FullName.StartsWith(SheetEntryPrefix, StringComparison.OrdinalIgnoreCase) &&
                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            if (sheetEntry is null)
            {
                return;
            }

            sheetEntryName = sheetEntry.FullName;

            // 1. 样式表：追加加粗字体、背景填充、边框与组合样式，返回新样式索引。
            int headerXf = -1;
            int textLeftXf = -1;
            int numberRightXf = -1;
            string stylesXml;
            var stylesEntry = zip.GetEntry(StylesEntryName);
            if (stylesEntry is null)
            {
                return;
            }

            using (var sr = new StreamReader(stylesEntry.Open()))
            {
                stylesXml = sr.ReadToEnd();
            }

            var stylesDoc = XDocument.Parse(stylesXml);
            NormalizeEmptyStyles(stylesDoc);
            int fontIdx = AppendBoldFont(stylesDoc);
            int fillIdx = AppendHeaderFill(stylesDoc);
            int borderIdx = AppendThinBorder(stylesDoc);
            if (fontIdx >= 0 && fillIdx >= 0 && borderIdx >= 0)
            {
                headerXf = AppendHeaderXf(stylesDoc, fontIdx, fillIdx, borderIdx);
            }

            textLeftXf = AppendAlignmentXf(stylesDoc, "left");
            numberRightXf = AppendAlignmentXf(stylesDoc, "right");

            // 2. 工作表：流式改造（表头样式 / 对齐 / 列宽 / 冻结）。
            using (var entryStream = sheetEntry.Open())
            using (var reader = XmlReader.Create(entryStream))
            using (var writer = XmlWriter.Create(tempSheetPath, new XmlWriterSettings { Indent = false, Encoding = new UTF8Encoding(false) }))
            {
                TransformSheet(reader, writer, columns.Count, columnKinds, columnWidths,
                    styleHeader, autoColumnWidth, freezeHeaderRow, headerXf, textLeftXf, numberRightXf);
            }

            // 3. 写回：先删后建，替换 styles.xml 与工作表。
            stylesEntry.Delete();
            sheetEntry.Delete();

            var newStylesEntry = zip.CreateEntry(StylesEntryName);
            using (var sw = new StreamWriter(newStylesEntry.Open(), new UTF8Encoding(false)))
            {
                sw.Write(stylesDoc.ToString(SaveOptions.DisableFormatting));
            }

            var newSheetEntry = zip.CreateEntry(sheetEntryName);
            using (var sheetOut = newSheetEntry.Open())
            using (var sheetIn = new FileStream(tempSheetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                sheetIn.CopyTo(sheetOut);
            }
        }

        try
        {
            File.Delete(tempSheetPath);
        }
        catch (IOException)
        {
            // 临时文件清理失败不影响导出结果。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }

    /// <summary>流式复制工作表：手写节点复制（不用 WriteNode，避免其位置语义与循环 Read 冲突）。</summary>
    private static void TransformSheet(
        XmlReader reader,
        XmlWriter writer,
        int columnCount,
        IReadOnlyList<ColumnKind> columnKinds,
        IReadOnlyList<double> columnWidths,
        bool styleHeader,
        bool autoColumnWidth,
        bool freezeHeaderRow,
        int headerXf,
        int textLeftXf,
        int numberRightXf)
    {
        reader.MoveToContent();
        var isFirstRow = true;
        CopyElement(reader, writer, ref isFirstRow, columnKinds, columnWidths,
            styleHeader, autoColumnWidth, freezeHeaderRow, headerXf, textLeftXf, numberRightXf);
    }

    /// <summary>递归复制当前元素（reader 位于该元素 StartElement）。遇到目标节点注入 / 删除。</summary>
    private static void CopyElement(
        XmlReader reader,
        XmlWriter writer,
        ref bool isFirstRow,
        IReadOnlyList<ColumnKind> columnKinds,
        IReadOnlyList<double> columnWidths,
        bool styleHeader,
        bool autoColumnWidth,
        bool freezeHeaderRow,
        int headerXf,
        int textLeftXf,
        int numberRightXf)
    {
        writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
        if (reader.HasAttributes)
        {
            while (reader.MoveToNextAttribute())
            {
                if (reader.Prefix == "xmlns" || reader.LocalName == "xmlns")
                {
                    continue;
                }

                writer.WriteAttributeString(reader.Prefix, reader.LocalName, reader.NamespaceURI, reader.Value);
            }

            reader.MoveToElement();
        }

        if (reader.IsEmptyElement)
        {
            writer.WriteEndElement();
            reader.Read();
            return;
        }

        reader.Read(); // 进入第一个子节点。
        while (true)
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.EndElement:
                    writer.WriteEndElement();
                    reader.Read();
                    return;

                case XmlNodeType.Element:
                    var localName = reader.LocalName;
                    var ns = reader.NamespaceURI;

                    if (localName == "cols" && ns == M.NamespaceName && autoColumnWidth)
                    {
                        WriteComputedCols(writer, columnWidths);
                        reader.Skip();
                        continue;
                    }

                    if (localName == "row" && ns == M.NamespaceName)
                    {
                        using (var subtree = reader.ReadSubtree())
                        {
                            var row = XElement.Load(subtree);
                            ApplyRow(row, isFirstRow, columnKinds, styleHeader, headerXf, textLeftXf, numberRightXf);
                            row.WriteTo(writer);
                        }

                        isFirstRow = false;
                        reader.Read(); // 跳过 row 的 EndElement。
                        continue;
                    }

                    if (localName == "pane" && ns == M.NamespaceName && !freezeHeaderRow)
                    {
                        reader.Skip();
                        continue;
                    }

                    CopyElement(reader, writer, ref isFirstRow, columnKinds, columnWidths,
                        styleHeader, autoColumnWidth, freezeHeaderRow, headerXf, textLeftXf, numberRightXf);
                    continue;

                default:
                    writer.WriteString(reader.Value);
                    reader.Read();
                    break;
            }
        }
    }

    private static void ApplyRow(
        XElement row,
        bool isHeader,
        IReadOnlyList<ColumnKind> columnKinds,
        bool styleHeader,
        int headerXf,
        int textLeftXf,
        int numberRightXf)
    {
        var cells = row.Elements(M + "c").ToList();

        if (isHeader)
        {
            if (styleHeader && headerXf >= 0)
            {
                foreach (var c in cells)
                {
                    c.SetAttributeValue("s", headerXf.ToString(CultureInfo.InvariantCulture));
                }
            }

            return;
        }

        for (var i = 0; i < cells.Count; i++)
        {
            // 日期列保留 Sylvan 内建 numFmt 样式（s 已存在），不做覆盖。
            if (cells[i].Attribute("s") is not null)
            {
                continue;
            }

            if (i >= columnKinds.Count)
            {
                continue;
            }

            var kind = columnKinds[i];
            if (kind == ColumnKind.Numeric && numberRightXf >= 0)
            {
                cells[i].SetAttributeValue("s", numberRightXf.ToString(CultureInfo.InvariantCulture));
            }
            else if (kind == ColumnKind.Text && textLeftXf >= 0)
            {
                cells[i].SetAttributeValue("s", textLeftXf.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private static void WriteComputedCols(XmlWriter writer, IReadOnlyList<double> widths)
    {
        writer.WriteStartElement("cols", M.NamespaceName);
        for (var i = 0; i < widths.Count; i++)
        {
            writer.WriteStartElement("col", M.NamespaceName);
            writer.WriteAttributeString("min", (i + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("max", (i + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("width", widths[i].ToString("0.##", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("customWidth", "1");
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    // ---- 类型分析 ----

    private static ColumnKind[] AnalyzeColumns(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
    {
        var kinds = new ColumnKind[columns.Count];
        var sawValue = new bool[columns.Count];
        var allNumeric = new bool[columns.Count];

        for (var i = 0; i < columns.Count; i++)
        {
            kinds[i] = ColumnKind.Text;
            allNumeric[i] = true;
        }

        foreach (var row in rows)
        {
            for (var i = 0; i < columns.Count && i < row.Length; i++)
            {
                var v = row[i];
                if (v is null || v == DBNull.Value)
                {
                    continue;
                }

                if (v is DateTime || v is DateTimeOffset)
                {
                    kinds[i] = ColumnKind.Date;
                }

                sawValue[i] = true;
                if (!IsNumeric(v))
                {
                    allNumeric[i] = false;
                }
            }
        }

        for (var i = 0; i < columns.Count; i++)
        {
            if (kinds[i] == ColumnKind.Date)
            {
                continue;
            }

            if (sawValue[i] && allNumeric[i])
            {
                kinds[i] = ColumnKind.Numeric;
            }
            else
            {
                kinds[i] = ColumnKind.Text;
            }
        }

        return kinds;
    }

    private static bool IsNumeric(object v)
    {
        if (v is sbyte || v is byte || v is short || v is ushort || v is int || v is uint || v is long || v is ulong)
        {
            return true;
        }

        return v is float || v is double || v is decimal;
    }

    // ---- 列宽估算 ----

    private static double[] ComputeColumnWidths(
        IReadOnlyList<string> columns,
        IReadOnlyList<object?[]> rows,
        IReadOnlyList<ColumnKind> kinds,
        int minWidth,
        int maxWidth)
    {
        var widths = new int[columns.Count];

        // 表头文本。
        for (var i = 0; i < columns.Count; i++)
        {
            widths[i] = DisplayWidth(columns[i]);
        }

        // 数据内容（取最大宽度）。
        foreach (var row in rows)
        {
            for (var i = 0; i < columns.Count && i < row.Length; i++)
            {
                var kind = i < kinds.Count ? kinds[i] : ColumnKind.Text;
                var w = kind == ColumnKind.Date ? 19 : DisplayWidth(row[i]);
                if (w > widths[i])
                {
                    widths[i] = w;
                }
            }
        }

        // 夹取到 [minWidth, maxWidth]。
        var result = new double[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            var clamped = Math.Max(minWidth, Math.Min(maxWidth, widths[i]));
            result[i] = clamped;
        }

        return result;
    }

    /// <summary>估算显示宽度：中文 / 全角按 2 个字符，其余按 1 个字符。</summary>
    private static int DisplayWidth(object? value)
    {
        if (value is null || value == DBNull.Value)
        {
            return 0;
        }

        var s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        var w = 0;
        foreach (var ch in s)
        {
            w += ch > 0xFF ? 2 : 1;
        }

        return w;
    }

    // ---- 样式表追加 ----

    /// <summary>
    /// 规范化 Sylvan 输出的空 <c>fill</c> / <c>border</c> 元素，
    /// 使其符合严格解析器（如 openpyxl）要求的完整子结构。
    /// </summary>
    private static void NormalizeEmptyStyles(XDocument styles)
    {
        foreach (var fill in styles.Descendants(M + "fill")
                     .Where(f => !f.Elements(M + "patternFill").Any())
                     .ToList())
        {
            fill.Add(new XElement(M + "patternFill", new XAttribute("patternType", "none")));
        }

        foreach (var border in styles.Descendants(M + "border")
                     .Where(b => !b.Elements().Any())
                     .ToList())
        {
            border.Add(
                new XElement(M + "left"),
                new XElement(M + "right"),
                new XElement(M + "top"),
                new XElement(M + "bottom"),
                new XElement(M + "diagonal"));
        }
    }

    private static int AppendBoldFont(XDocument styles)
    {
        var fonts = styles.Root?.Element(M + "fonts");
        if (fonts is null)
        {
            return -1;
        }

        fonts.Add(new XElement(M + "font",
            new XElement(M + "b"),
            new XElement(M + "name", new XAttribute("val", "Calibri"))));
        return fonts.Elements(M + "font").Count() - 1;
    }

    private static int AppendHeaderFill(XDocument styles)
    {
        var fills = styles.Root?.Element(M + "fills");
        if (fills is null)
        {
            return -1;
        }

        fills.Add(new XElement(M + "fill",
            new XElement(M + "patternFill",
                new XAttribute("patternType", "solid"),
                new XElement(M + "fgColor", new XAttribute("rgb", "FFD9E1F2")),
                new XElement(M + "bgColor", new XAttribute("indexed", "64")))));
        return fills.Elements(M + "fill").Count() - 1;
    }

    private static int AppendThinBorder(XDocument styles)
    {
        var borders = styles.Root?.Element(M + "borders");
        if (borders is null)
        {
            return -1;
        }

        borders.Add(new XElement(M + "border",
            new XElement(M + "left", new XAttribute("style", "thin"), new XElement(M + "color", new XAttribute("auto", "1"))),
            new XElement(M + "right", new XAttribute("style", "thin"), new XElement(M + "color", new XAttribute("auto", "1"))),
            new XElement(M + "top", new XAttribute("style", "thin"), new XElement(M + "color", new XAttribute("auto", "1"))),
            new XElement(M + "bottom", new XAttribute("style", "thin"), new XElement(M + "color", new XAttribute("auto", "1"))),
            new XElement(M + "diagonal")));
        return borders.Elements(M + "border").Count() - 1;
    }

    private static int AppendHeaderXf(XDocument styles, int fontIdx, int fillIdx, int borderIdx)
    {
        var cellXfs = styles.Root?.Element(M + "cellXfs");
        if (cellXfs is null)
        {
            return -1;
        }

        cellXfs.Add(new XElement(M + "xf",
            new XAttribute("numFmtId", "0"),
            new XAttribute("xfId", "0"),
            new XAttribute("fontId", fontIdx.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("fillId", fillIdx.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("borderId", borderIdx.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("applyFont", "1"),
            new XAttribute("applyFill", "1"),
            new XAttribute("applyBorder", "1"),
            new XAttribute("applyAlignment", "1"),
            new XElement(M + "alignment",
                new XAttribute("horizontal", "left"),
                new XAttribute("vertical", "center"))));
        return cellXfs.Elements(M + "xf").Count() - 1;
    }

    private static int AppendAlignmentXf(XDocument styles, string horizontal)
    {
        var cellXfs = styles.Root?.Element(M + "cellXfs");
        if (cellXfs is null)
        {
            return -1;
        }

        cellXfs.Add(new XElement(M + "xf",
            new XAttribute("numFmtId", "0"),
            new XAttribute("xfId", "0"),
            new XAttribute("applyAlignment", "1"),
            new XElement(M + "alignment", new XAttribute("horizontal", horizontal))));
        return cellXfs.Elements(M + "xf").Count() - 1;
    }
}
