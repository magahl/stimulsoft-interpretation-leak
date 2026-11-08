using System.Data;
using Stimulsoft.Base.Drawing;
using Stimulsoft.Report;
using Stimulsoft.Report.Chart;
using Stimulsoft.Report.Engine;

var mode = args.Length > 0 ? Enum.Parse<StiCalculationMode>(args[0], ignoreCase: true) : StiCalculationMode.Interpretation;
var renders = args.Length > 1 ? int.Parse(args[1]) : 20;
var points = args.Length > 2 ? int.Parse(args[2]) : 5000;

Console.WriteLine($"Stimulsoft {typeof(StiReport).Assembly.GetName().Version}, .NET {Environment.Version}, {mode}, {renders} renders x {points} chart points");

var baseline = LiveHeapBytes();
for (var render = 1; render <= renders; render++)
{
    using (var report = CreateChartReport(mode, points))
    {
        report.Render(false);
    }

    Console.WriteLine($"render {render,3}: live heap after full GC +{(LiveHeapBytes() - baseline) / 1048576.0,7:N1} MB");
}

Console.WriteLine();
Console.WriteLine("Same leak without a chart, calling StiParser.ParseTextValue(StiReport, string) directly:");
using (var report = new StiReport())
{
    var before = LiveHeapBytes();
    for (var i = 0; i < 100_000; i++)
    {
        StiParser.ParseTextValue(report, "{1 + 1}");
    }

    Console.WriteLine($"100,000 calls: live heap after full GC +{(LiveHeapBytes() - before) / 1048576.0:N1} MB, never released");
}

static StiReport CreateChartReport(StiCalculationMode mode, int points)
{
    var table = new DataTable("Points");
    table.Columns.Add("Argument", typeof(string));
    table.Columns.Add("Value", typeof(double));
    for (var i = 0; i < points; i++)
    {
        table.Rows.Add($"P{i}", i % 100);
    }

    var data = new DataSet("Data");
    data.Tables.Add(table);

    var report = new StiReport { CalculationMode = mode };
    report.RegData(data);
    report.Dictionary.Synchronize();

    var series = new StiClusteredColumnSeries();
    series.Value.Value = "{Points.Value}";
    series.Argument.Value = "{Points.Argument}";

    var chart = new StiChart
    {
        Name = "Chart1",
        ClientRectangle = new RectangleD(0, 0, 7, 4),
        DataSourceName = "Points"
    };
    chart.Series.Add(series);
    report.Pages[0].Components.Add(chart);

    return report;
}

static long LiveHeapBytes()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    return GC.GetTotalMemory(forceFullCollection: true);
}
