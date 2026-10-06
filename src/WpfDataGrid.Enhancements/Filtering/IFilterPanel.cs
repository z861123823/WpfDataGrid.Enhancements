using System.Windows;

namespace WpfDataGrid.Enhancements.Filtering;

/// <summary>
/// 列头过滤面板契约。客户可通过 <see cref="DataGridFilterBehavior.FilterPanelFactory"/>
/// 注入自定义面板实现（须同时是 <see cref="UIElement"/> 才能放入弹层）。
/// </summary>
public interface IFilterPanel
{
    /// <summary>打开弹层后聚焦主输入控件；无输入控件时忽略。</summary>
    void FocusInput();

    /// <summary>关闭弹层。</summary>
    void Close();
}
