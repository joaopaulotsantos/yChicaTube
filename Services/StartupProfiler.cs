using System.Diagnostics;

namespace yChicaTube.Services;

public static class StartupProfiler
{
    private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();
    private static readonly List<(string Step, long ElapsedMs)> Milestones = new();

    public static void Milestone(string stepName)
    {
        var elapsed = Stopwatch.ElapsedMilliseconds;
        lock (Milestones)
        {
            Milestones.Add((stepName, elapsed));
        }
        Debug.WriteLine($"[StartupPerf] {elapsed,5} ms -> {stepName}");
    }

    public static string GetReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== STARTUP PERFORMANCE REPORT ===");
        long prev = 0;
        lock (Milestones)
        {
            foreach (var (step, elapsed) in Milestones)
            {
                var delta = elapsed - prev;
                sb.AppendLine($"  {elapsed,5} ms (+{delta,4} ms) : {step}");
                prev = elapsed;
            }
        }
        sb.AppendLine($"Total Startup Time to Home Ready: {Stopwatch.ElapsedMilliseconds} ms");
        return sb.ToString();
    }
}
