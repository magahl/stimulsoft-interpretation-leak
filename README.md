# Stimulsoft: Interpretation mode leaks interned strings on every chart value

In `StiCalculationMode.Interpretation`, every chart series value or argument that is given as an expression creates a new, uniquely named `StiText`. `StiBase.Name` interns that name with `string.Intern`, so each evaluation adds a string to the runtime intern pool that is never released. Memory grows with every render until the process is restarted.

Reproduced with **Stimulsoft.Reports.Engine 2026.4.2** (latest) and 2026.3.4 on .NET 10, Linux. The code path is platform independent.

## Run

```bash
dotnet run --project src/Repro -c Release -- Interpretation 10 5000
dotnet run --project src/Repro -c Release -- Compilation 10 5000
```

Arguments: calculation mode, number of renders, number of chart points. Each render builds a report with one column chart whose series uses `Value = "{Points.Value}"` and `Argument = "{Points.Argument}"`, renders it, disposes it, then forces a full GC and prints the live heap.

## Result

Interpretation: the live heap grows by ~0.55 MB per render (5,000 points × 2 expressions × ~56 bytes per interned name) and never comes back.

```
Stimulsoft 2026.4.2.0, .NET 10.0.13, Interpretation, 10 renders x 5000 chart points
render   1: live heap after full GC +    9.0 MB
render   2: live heap after full GC +    9.6 MB
render   3: live heap after full GC +   10.1 MB
...
render  10: live heap after full GC +   13.9 MB

Same leak without a chart, calling StiParser.ParseTextValue(StiReport, string) directly:
100,000 calls: live heap after full GC +4.3 MB, never released
```

Compilation: flat after warm-up.

```
Stimulsoft 2026.4.2.0, .NET 10.0.13, Compilation, 10 renders x 5000 chart points
render   1: live heap after full GC +   17.9 MB
render   2: live heap after full GC +   18.3 MB
...
render  10: live heap after full GC +   18.4 MB
```

In a production API rendering a template with 20 charts in Interpretation mode, a heap dump after 14 hours held **16.9 million** interned strings of the form `Temp` + 8–9 hex characters (775 MB), plus 129 MB of intern table, out of a 1.1 GB live heap.

## Cause

`StiParser.ParseTextValue(StiReport, string)` creates a throwaway component with a random name for every call:

```csharp
public static string ParseTextValue(StiReport report, string expression)
{
    return report.ToString(ParseTextValue(expression, new StiText
    {
        Name = "Temp" + Guid.NewGuid().ToString().Substring(0, 10).Replace("-", ""),
        Page = report.Pages[0]
    }));
}
```

and `StiBase.Name` interns every value assigned to it:

```csharp
public virtual string Name
{
    get => name;
    set => name = string.Intern(value);
}
```

Interned strings are never collected, so each call leaks one string permanently.

In Interpretation mode the chart engine calls this once per data point for each expression-based series property, for example `StiSeries.InvokeGetValue`:

```csharp
if (sender.Report.CalculationMode == StiCalculationMode.Interpretation)
{
    e.Value = StiParser.ParseTextValue(sender.Report, Value);
}
```

The same pattern is used for `Argument`, `ListOfValues`, `ListOfArguments`, `Title`, `ToolTip`, `ListOfToolTips`, `Tag`, `ListOfTags`, `ListOfHyperlinks` and the range, candlestick, bubble, pie and heatmap series properties (33 call sites in `Stimulsoft.Report.Chart`).

## Suggested fix

Any of:

- Use a constant name for the temporary component in `ParseTextValue(StiReport, string)`, e.g. `"Temp"`. The name only needs to exist, not be unique.
- Reuse one temporary `StiText` per report instead of creating one per call.
- Stop interning in `StiBase.Name`, or intern only names coming from a loaded template.

## Workaround

Set `CalculationMode = StiCalculationMode.Compilation` on affected reports. The chart then evaluates its expressions through compiled code and never calls `ParseTextValue(StiReport, string)`.
