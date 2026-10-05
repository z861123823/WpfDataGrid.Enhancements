using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Sylvan.Data.Excel;
using WpfDataGrid.Enhancements.Exporting;

namespace WpfDataGrid.Enhancements.Pro.Exporting;

/// <summary>
/// xlsx 导出实现（Pro）：原生 xlsx（免 COM），基于 Sylvan.Data.Excel（MIT）流式写入。
/// 导出美化：表头样式（加粗 + 背景色 + 边框）、按内容自动列宽（默认 8~60）、
/// 文本列左对齐 / 数值列右对齐 / 日期列格式（Sylvan 内建 numFmt）、冻结表头行。
/// 实现方式：Sylvan 流式写数据到临时文件，随后仅对 xl/styles.xml 与工作表 xml 做
/// 样式后处理（工作表流式改造），再流式复制到目标流——大文件内存占用不随数据量增长。
/// </summary>
public sealed class XlsxExportProvider : IExportProvider
{
    /// <summary>默认工作表名称。</summary>
    public const string DefaultWorksheetName = "Sheet1";

    /// <summary>自动列宽下限（字符单位）。</summary>
    public const int DefaultMinColumnWidth = 8;

    /// <summary>自动列宽上限（字符单位）。</summary>
    public const int DefaultMaxColumnWidth = 60;

    public string FileExtension => ".xlsx";

    /// <summary>导出工作表名称（覆盖 TableData.WorksheetName，为空时使用默认值）。</summary>
    public string? WorksheetName { get; set; }

    /// <summary>是否写入表头样式（加粗 + 背景色 + 边框），默认 true。</summary>
    public bool StyleHeader { get; set; } = true;

    /// <summary>是否按内容自动计算列宽（宽度范围 <see cref="MinColumnWidth"/> ~ <see cref="MaxColumnWidth"/>），默认 true。</summary>
    public bool AutoColumnWidth { get; set; } = true;

    /// <summary>是否冻结表头行（便于浏览大数据），默认 true。</summary>
    public bool FreezeHeaderRow { get; set; } = true;

    /// <summary>自动列宽下限（字符单位），默认 8。</summary>
    public int MinColumnWidth { get; set; } = DefaultMinColumnWidth;

    /// <summary>自动列宽上限（字符单位），默认 60。</summary>
    public int MaxColumnWidth { get; set; } = DefaultMaxColumnWidth;

    public void Export(TableData tableData, Stream destination)
    {
        if (tableData is null) throw new ArgumentNullException(nameof(tableData));
        if (destination is null) throw new ArgumentNullException(nameof(destination));

        if (MinColumnWidth < 1 || MaxColumnWidth < MinColumnWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(MinColumnWidth), "自动列宽范围无效：需满足 1 <= MinColumnWidth <= MaxColumnWidth。");
        }

        var sheetName = string.IsNullOrWhiteSpace(WorksheetName)
            ? (string.IsNullOrWhiteSpace(tableData.WorksheetName) ? DefaultWorksheetName : tableData.WorksheetName!)
            : WorksheetName!;

        // 行数据只物化一次：既用于构造 DataTable，也用于后处理的列类型 / 列宽估算。
        var rows = tableData.Rows as IReadOnlyList<object?[]> ?? tableData.Rows.ToList();

        // 临时文件：Sylvan 流式写入 + zip 后处理，避免目标流必须是可读写定位流的限制。
        var tempPath = Path.Combine(Path.GetTempPath(), $"xlsx_{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = File.Create(tempPath))
            using (var table = BuildDataTable(tableData.Columns, rows))
            using (var reader = new DataTableReader(table))
            using (var writer = ExcelDataWriter.Create(
                       fs,
                       ExcelWorkbookType.ExcelXml,
                       new ExcelDataWriterOptions { OwnsStream = false }))
            {
                writer.Write(reader, sheetName);
            }

            XlsxFormatting.Apply(
                tempPath,
                tableData.Columns,
                rows,
                StyleHeader,
                AutoColumnWidth,
                FreezeHeaderRow,
                MinColumnWidth,
                MaxColumnWidth);

            using (var src = File.OpenRead(tempPath))
            {
                src.CopyTo(destination);
            }
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private static DataTable BuildDataTable(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
    {
        var table = new DataTable();
        foreach (var column in columns)
        {
            table.Columns.Add(column, typeof(object));
        }

        foreach (var row in rows)
        {
            var values = new object[row.Length];
            for (var i = 0; i < row.Length; i++)
            {
                values[i] = row[i] ?? DBNull.Value;
            }

            table.Rows.Add(values);
        }

        return table;
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
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
}
