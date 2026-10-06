using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Data;
using WpfDataGrid.Enhancements.Exporting;
using WpfDataGrid.Enhancements.Filtering;
#if PRO
using WpfDataGrid.Enhancements.Pro.Exporting;
using WpfDataGrid.Enhancements.Pro.Performance;
#endif

namespace WpfDataGrid.Enhancements.PerfBench;

public sealed class OrderRecord
{
    public int Id { get; set; }
    public string Customer { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string Status { get; set; } = string.Empty;
}

public static class Program
{
    private const int RowCount = 100_000;

    public static void Main()
    {
        Console.WriteLine($"env|OS|{Environment.OSVersion}");
        Console.WriteLine($"env|ProcessorCount|{Environment.ProcessorCount}");
        Console.WriteLine($"env|Is64BitOS|{Environment.Is64BitOperatingSystem}");
        Console.WriteLine($"env|DotNet|{Environment.Version}");
        Console.WriteLine($"env|Time|{DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        // ---------- 数据准备 ----------
        var sw = Stopwatch.StartNew();
        var orders = GenerateOrders(RowCount);
        sw.Stop();
        Console.WriteLine($"data|Generate|{sw.ElapsedMilliseconds}|rows={orders.Count}");

        // ---------- 1. AsyncVirtualizingCollection ----------
#if PRO
        RunVirtualization(orders);
#endif

        // ---------- 2. 过滤响应 ----------
        RunFilterBench(orders);

        // ---------- 3. ICollectionView 对比 ----------
        RunCollectionViewBench(orders);

        // ---------- 4. CSV 导出 ----------
        RunCsvExport(orders);

        // ---------- 5. Xlsx 导出 ----------
#if PRO
        RunXlsxExport(orders);
#endif

        Console.WriteLine("DONE");
    }

    private static List<OrderRecord> GenerateOrders(int count)
    {
        var rnd = new Random(42);
        var list = new List<OrderRecord>(count);
        var names = new[] { "张三", "李四", "王五", "赵六", "陈七", "刘八", "Alice", "Bob", "Carol", "David", "Emma", "Frank", "Grace", "Henry", "Iris" };
        var statuses = new[] { "已支付", "待发货", "已发货", "已完成", "已取消", "退款中" };
        for (var i = 0; i < count; i++)
        {
            list.Add(new OrderRecord
            {
                Id = i + 1,
                Customer = names[rnd.Next(names.Length)] + "_" + (i % 9973),
                Amount = Math.Round((decimal)(rnd.NextDouble() * 10000) + 0.01m, 2),
                Date = new DateTime(2023, 1, 1).AddDays(rnd.Next(1100)),
                Status = statuses[rnd.Next(statuses.Length)]
            });
        }

        return list;
    }

#if PRO
    private static void RunVirtualization(List<OrderRecord> orders)
    {
        Console.WriteLine("--- async-virtualizing ---");

        // LoadAll（同步全量加载）
        var colAll = new AsyncVirtualizingCollection<OrderRecord>(
            (offset, count) => orders.GetRange(offset, count), pageSize: 1000, totalCount: orders.Count);
        var sw = Stopwatch.StartNew();
        colAll.LoadAll();
        sw.Stop();
        Console.WriteLine($"async|LoadAll100k|{sw.ElapsedMilliseconds}|loaded={colAll.LoadedCount}");

        // 逐页 LoadPage（1000/页 × 100 页，异步调度总耗时）
        var colPage = new AsyncVirtualizingCollection<OrderRecord>(
            (offset, count) => orders.GetRange(offset, count), pageSize: 1000, totalCount: orders.Count);
        var pageCount = (orders.Count + colPage.PageSize - 1) / colPage.PageSize;
        sw.Restart();
        for (var p = 0; p < pageCount; p++) colPage.LoadPage(p);
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (colPage.LoadedCount < orders.Count && DateTime.UtcNow < deadline) Thread.Sleep(5);
        sw.Stop();
        Console.WriteLine($"async|LoadPages100|{sw.ElapsedMilliseconds}|pages={pageCount}|loaded={colPage.LoadedCount}|pageSize={colPage.PageSize}");

        // 缓存命中：全量加载后随机访问 10 万次索引器
        var rnd = new Random(7);
        long dummy = 0;
        sw.Restart();
        for (var i = 0; i < 100_000; i++)
        {
            var idx = rnd.Next(orders.Count);
            dummy += colAll[idx]?.Id ?? 0;
        }
        sw.Stop();
        Console.WriteLine($"async|CacheHit100k|{sw.ElapsedMilliseconds}|dummy={dummy}");
    }
#endif

    private static void RunFilterBench(List<OrderRecord> orders)
    {
        Console.WriteLine("--- filter-model ---");
        var model = new FilterModel();
        model.GetOrAdd(nameof(OrderRecord.Customer)).AddPredicate(new TextContainsPredicate("张", ignoreCase: true));
        model.GetOrAdd(nameof(OrderRecord.Status)).AddPredicate(new TextContainsPredicate("已完成", ignoreCase: true));
        model.Invalidate();

        var sw = Stopwatch.StartNew();
        var matched = 0;
        foreach (var row in orders)
        {
            if (model.IsMatch(row)) matched++;
        }
        sw.Stop();
        Console.WriteLine($"filter|FilterModel10kRows|{sw.ElapsedMilliseconds}|matched={matched}");
    }

    private static void RunCollectionViewBench(List<OrderRecord> orders)
    {
        Console.WriteLine("--- collection-view ---");
        var source = new ObservableCollection<OrderRecord>(orders);
        var view = new ListCollectionView(source);
        view.Filter = o => o is OrderRecord r
            && r.Customer.Contains("张", StringComparison.OrdinalIgnoreCase)
            && r.Status.Contains("已完成", StringComparison.OrdinalIgnoreCase);

        var sw = Stopwatch.StartNew();
        view.Refresh();
        var count = 0;
        foreach (var item in view) count++;
        sw.Stop();
        Console.WriteLine($"filter|ICollectionView100k|{sw.ElapsedMilliseconds}|viewCount={count}");
    }

    private static void RunCsvExport(List<OrderRecord> orders)
    {
        Console.WriteLine("--- csv-export ---");
        var table = BuildTableData(orders);
        var outPath = Path.Combine(Path.GetTempPath(), "perfbench_orders.csv");
        var sw = Stopwatch.StartNew();
        using (var fs = File.Create(outPath))
        {
            new CsvExportProvider().Export(table, fs);
        }
        sw.Stop();
        var size = new FileInfo(outPath).Length;
        Console.WriteLine($"export|csv|{sw.ElapsedMilliseconds}|{size}");
    }

#if PRO
    private static void RunXlsxExport(List<OrderRecord> orders)
    {
        Console.WriteLine("--- xlsx-export ---");
        var table = BuildTableData(orders);
        var outPath = Path.Combine(Path.GetTempPath(), "perfbench_orders.xlsx");
        var sw = Stopwatch.StartNew();
        using (var fs = File.Create(outPath))
        {
            new XlsxExportProvider().Export(table, fs);
        }
        sw.Stop();
        var size = new FileInfo(outPath).Length;
        Console.WriteLine($"export|xlsx|{sw.ElapsedMilliseconds}|{size}");
    }
#endif

    private static TableData BuildTableData(List<OrderRecord> orders)
    {
        var columns = new[] { nameof(OrderRecord.Id), nameof(OrderRecord.Customer), nameof(OrderRecord.Amount), nameof(OrderRecord.Date), nameof(OrderRecord.Status) };
        var rows = new List<object?[]>(orders.Count);
        foreach (var o in orders)
        {
            rows.Add(new object?[] { o.Id, o.Customer, o.Amount, o.Date, o.Status });
        }

        return new TableData(columns, rows) { WorksheetName = "Orders" };
    }
}
