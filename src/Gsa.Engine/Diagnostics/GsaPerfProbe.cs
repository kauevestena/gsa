using System.Diagnostics;
using System.Globalization;
using Godot;

namespace Gsa.Engine.Diagnostics;

/// <summary>Opt-in CLI frame-time and render-monitor probe (--perf-probe=&lt;frames&gt;) with zero overhead when inactive.</summary>
public sealed class GsaPerfProbe
{
    private readonly int _targetFrames;
    private readonly int _warmupFrames;
    private readonly string _sceneTag;
    private readonly Rid _viewportRid;
    private readonly List<double> _frameMs = [];
    private readonly List<double> _gpuMs = [];
    private readonly List<double> _renderCpuMs = [];
    private long _lastTimestamp;
    private int _frameCount;
    private bool _finished;

    private GsaPerfProbe(Node owner, string sceneTag, int targetFrames, int warmupFrames)
    {
        _sceneTag = sceneTag;
        _targetFrames = Math.Max(10, targetFrames);
        _warmupFrames = Math.Max(5, warmupFrames);
        _viewportRid = owner.GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(_viewportRid, true);
        _lastTimestamp = Stopwatch.GetTimestamp();
    }

    public static GsaPerfProbe? TryCreateFromArgs(Node owner, string sceneTag)
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--perf-probe=", StringComparison.Ordinal) &&
                int.TryParse(arg["--perf-probe=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames) &&
                frames > 0)
            {
                return new GsaPerfProbe(owner, sceneTag, frames, warmupFrames: Math.Min(20, Math.Max(5, frames / 4)));
            }
        }
        return null;
    }

    public void StepFrame(Node owner)
    {
        if (_finished)
            return;

        var now = Stopwatch.GetTimestamp();
        var dtMs = (now - _lastTimestamp) * 1000.0 / Stopwatch.Frequency;
        _lastTimestamp = now;

        _frameCount++;
        if (_frameCount <= _warmupFrames)
            return;

        _frameMs.Add(dtMs);
        _gpuMs.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewportRid));
        _renderCpuMs.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewportRid));

        if (_frameMs.Count < _targetFrames)
            return;

        _finished = true;
        var sortedFrame = _frameMs.OrderBy(x => x).ToArray();
        var meanMs = sortedFrame.Average();
        var p95Ms = sortedFrame[Math.Min(sortedFrame.Length - 1, (int)Math.Ceiling(0.95 * sortedFrame.Length) - 1)];
        var p99Ms = sortedFrame[Math.Min(sortedFrame.Length - 1, (int)Math.Ceiling(0.99 * sortedFrame.Length) - 1)];
        var meanGpuMs = _gpuMs.Average();
        var meanRenderCpuMs = _renderCpuMs.Average();
        var meanFps = meanMs > 1e-4 ? 1000.0 / meanMs : 0.0;

        var drawCalls = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        var objects = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        var primitives = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        var processMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        var physicsMs = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        var rssMb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);

        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"GSA_PERF_PROBE scene={_sceneTag} frames={sortedFrame.Length} mean_fps={meanFps:F2} " +
            $"mean_ms={meanMs:F2} p95_ms={p95Ms:F2} p99_ms={p99Ms:F2} " +
            $"gpu_ms={meanGpuMs:F2} render_cpu_ms={meanRenderCpuMs:F2} " +
            $"process_ms={processMs:F2} physics_ms={physicsMs:F2} " +
            $"draw_calls={drawCalls} objects={objects} primitives={primitives} rss_mb={rssMb:F1}"));

        owner.GetTree().Quit(0);
    }
}

