using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WpfDataGrid.Enhancements.Infrastructure;

namespace WpfDataGrid.Enhancements.Exporting;

/// <summary>
/// CSV 导出实现（开源版内置）：UTF-8 with BOM，遵循 RFC 4180 转义规则。
/// 字段含逗号 / 引号 / 换行时用双引号包裹，内部引号双写转义。
/// </summary>
public sealed class CsvExportProvider : IExportProvider
{
    /// <summary>是否写入 UTF-8 BOM（默认 true，便于 Excel 正确识别中文）。</summary>
    public bool WriteBom { get; set; } = true;

    public string FileExtension => ".csv";

    public void Export(TableData tableData, Stream destination)
    {
        if (tableData == null) throw new ArgumentNullException(nameof(tableData));
        if (destination == null) throw new ArgumentNullException(nameof(destination));

        var encoding = new UTF8Encoding(WriteBom);
        using (var writer = new StreamWriter(destination, encoding, 4096, leaveOpen: true))
        {
            WriteRow(writer, tableData.Columns);
            foreach (var row in tableData.Rows)
            {
                WriteRow(writer, row);
            }
            writer.Flush();
        }
    }

    private static void WriteRow(StreamWriter writer, IEnumerable<object?> values)
    {
        bool first = true;
        foreach (var value in values)
        {
            if (!first) writer.Write(',');
            first = false;
            WriteEscaped(writer, PropertyAccessor.ToDisplayString(value));
        }

        writer.WriteLine();
    }

    private static void WriteEscaped(StreamWriter writer, string text)
    {
        if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
        {
            writer.Write(text);
            return;
        }

        writer.Write('"');
        writer.Write(text.Replace("\"", "\"\""));
        writer.Write('"');
    }
}
