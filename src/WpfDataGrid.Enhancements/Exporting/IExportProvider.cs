using System.IO;

namespace WpfDataGrid.Enhancements.Exporting;

/// <summary>
/// 导出策略接口：负责把 <see cref="TableData"/> 写入目标流。
/// 开源版内置 CSV；Pro 版提供 xlsx / PDF 实现。
/// </summary>
public interface IExportProvider
{
    /// <summary>目标文件扩展名（含点号，如 ".csv"、".xlsx"）。</summary>
    string FileExtension { get; }

    /// <summary>将表格数据写入目标流。</summary>
    void Export(TableData tableData, Stream destination);
}
