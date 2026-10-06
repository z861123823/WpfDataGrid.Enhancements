using System;
using System.Threading;
using Xunit.Sdk;

namespace WpfDataGrid.Enhancements.Tests;

/// <summary>在 STA 线程中执行 WPF 相关测试体（xunit 默认线程非 STA，WPF 控件需 STA）。</summary>
internal static class StaHelper
{
    public static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            throw new XunitException("STA 线程内异常：" + failure);
        }
    }
}

/// <summary>测试用简单数据行（属性供 PropertyAccessor 反射取值）。</summary>
public sealed class RowItem
{
    public string? Name { get; set; }

    public decimal Amount { get; set; }
}
