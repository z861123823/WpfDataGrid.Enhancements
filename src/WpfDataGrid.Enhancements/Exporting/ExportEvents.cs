using System;

namespace WpfDataGrid.Enhancements.Exporting;

/// <summary>
/// 导出即将开始事件参数（<see cref="DataGridExportBehavior.Exporting"/>）。
/// 订阅方可查看目标路径与即将导出的行数，并通过 <see cref="Cancel"/> 取消导出。
/// </summary>
public sealed class ExportingEventArgs : EventArgs
{
    public ExportingEventArgs(string filePath, int rowCount)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        RowCount = rowCount;
    }

    /// <summary>目标文件路径。</summary>
    public string FilePath { get; }

    /// <summary>即将导出的数据行数（当前视图 / 选中行 / 全量）。</summary>
    public int RowCount { get; }

    /// <summary>设为 true 可取消本次导出（不创建文件，Provider 不会被调用）。</summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// 导出完成事件参数（<see cref="DataGridExportBehavior.Exported"/>）。
/// </summary>
public sealed class ExportedEventArgs : EventArgs
{
    public ExportedEventArgs(string filePath, int rowCount)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        RowCount = rowCount;
    }

    /// <summary>目标文件路径。</summary>
    public string FilePath { get; }

    /// <summary>已导出的数据行数。</summary>
    public int RowCount { get; }
}
