using System;
using System.Collections.Generic;

namespace WpfDataGrid.Enhancements.Exporting;

/// <summary>
/// 导出用表格数据描述：列定义 + 行数据。
/// 由导出行为从 DataGrid 视图中收集，交由 <see cref="IExportProvider"/> 写出。
/// </summary>
public sealed class TableData
{
    public TableData(IReadOnlyList<string> columns, IEnumerable<object?[]> rows)
    {
        Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
    }

    /// <summary>列名（与 DataGridColumn.Header 对应）。</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>行数据，每行元素顺序与 <see cref="Columns"/> 对齐。</summary>
    public IEnumerable<object?[]> Rows { get; }

    /// <summary>
    /// 导出工作表名称（xlsx / PDF 标题等使用）；为空时由导出器使用默认值。
    /// </summary>
    public string? WorksheetName { get; set; }
}
