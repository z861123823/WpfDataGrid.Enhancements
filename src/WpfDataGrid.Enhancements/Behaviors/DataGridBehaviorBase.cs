using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace WpfDataGrid.Enhancements.Behaviors;

/// <summary>
/// Behavior 基座：封装附加/分离生命周期与内部服务查找。
/// 具体行为（过滤、导出等）继承本基座以复用生命周期管理。
/// </summary>
public abstract class DataGridBehaviorBase : Behavior<DataGrid>
{
    protected override void OnAttached()
    {
        // 派生类在此初始化内部服务（过滤器、导出 Provider 等）。
        base.OnAttached();
    }

    protected override void OnDetaching()
    {
        // 派生类在此释放内部服务、取消事件订阅。
        base.OnDetaching();
    }
}
