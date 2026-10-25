using System.Text.Json;
using Godot;
using OpenNV.Runtime;


namespace OpenNV.Runtime.Diagnostics.Performance;

internal partial class RuntimePerformanceObserver : Node
{
    private RuntimePerformanceConfiguration _configuration = null!;
    private string _configurationSchema = "";
    private string _configurationSha256 = "";
    private string? _reportPath;
    private double _elapsedSeconds;
    private long _sampleCount;
    private bool _reportWritten;
    private PerformanceMetricAccumulator _framesPerSecond;
    private PerformanceMetricAccumulator _processSeconds;
    private PerformanceMetricAccumulator _physicsProcessSeconds;
    private PerformanceMetricAccumulator _nodeCount;
    private PerformanceMetricAccumulator _orphanNodeCount;
    private PerformanceMetricAccumulator _staticMemoryBytes;
    private PerformanceMetricAccumulator _renderedObjectCount;
    private PerformanceMetricAccumulator _renderedPrimitiveCount;

    internal void Configure(
        RuntimePerformanceConfiguration configuration,
        string configurationSchema,
        string configurationSha256,
        string? reportPath)
    {
        _configuration = configuration;
        _configurationSchema = configurationSchema;
        _configurationSha256 = configurationSha256;
        _reportPath = reportPath;
        Name = "RuntimePerformanceObserver";
    }

    public override void _Process(double delta)
    {
        _elapsedSeconds += delta;
        if (_elapsedSeconds < _configuration.SampleIntervalSeconds)
            return;
        _elapsedSeconds -= _configuration.SampleIntervalSeconds *
            Math.Floor(_elapsedSeconds / _configuration.SampleIntervalSeconds);
        Sample();
        // A hitch shows up as a collapsed FPS with a healthy main-thread time, which means
        // the stall is on the GPU (pipeline compilation or a texture upload). Emit one line
        // per hitch so it can be correlated with the content being streamed at that moment.
        var fps = Godot.Performance.GetMonitor(Godot.Performance.Monitor.TimeFps);
        if (fps < HitchFps)
            GD.PushWarning($"OPENNV_PERF_HITCH fps={fps} process_ms={Godot.Performance.GetMonitor(Godot.Performance.Monitor.TimeProcess) * 1000.0:0.0} " +
                $"nodes={Godot.Performance.GetMonitor(Godot.Performance.Monitor.ObjectNodeCount)} " +
                $"orphans={Godot.Performance.GetMonitor(Godot.Performance.Monitor.ObjectOrphanNodeCount)} " +
                $"rendered={(int)Godot.Performance.GetMonitor(Godot.Performance.Monitor.RenderTotalObjectsInFrame)}");
        // Rewrite the report on every sample: a hang or a system kill never reaches
        // _ExitTree, and that is exactly the case this report is needed for.
        if (_reportPath is not null) WriteReport(_reportPath);
    }

    private const int HitchFps = 15;

    public override void _ExitTree()
    {
        if (_reportPath is null || _reportWritten)
            return;
        if (_sampleCount == 0)
            Sample();
        WriteReport(_reportPath);
        _reportWritten = true;
    }

    private void Sample()
    {
        _framesPerSecond.Add(Godot.Performance.GetMonitor(Godot.Performance.Monitor.TimeFps));
        _processSeconds.Add(Godot.Performance.GetMonitor(Godot.Performance.Monitor.TimeProcess));
        _physicsProcessSeconds.Add(
            Godot.Performance.GetMonitor(Godot.Performance.Monitor.TimePhysicsProcess));
        _nodeCount.Add(Godot.Performance.GetMonitor(Godot.Performance.Monitor.ObjectNodeCount));
        _orphanNodeCount.Add(
            Godot.Performance.GetMonitor(Godot.Performance.Monitor.ObjectOrphanNodeCount));
        _staticMemoryBytes.Add(Godot.Performance.GetMonitor(Godot.Performance.Monitor.MemoryStatic));
        _renderedObjectCount.Add(
            Godot.Performance.GetMonitor(Godot.Performance.Monitor.RenderTotalObjectsInFrame));
        _renderedPrimitiveCount.Add(
            Godot.Performance.GetMonitor(Godot.Performance.Monitor.RenderTotalPrimitivesInFrame));
        _sampleCount++;
    }

    private void WriteReport(string reportPath)
    {
        var report = new
        {
            schema = "opennv-runtime-performance/v1",
            status = "observed-no-thresholds",
            engine = Engine.GetVersionInfo()["string"].AsString(),
            displayServer = DisplayServer.GetName(),
            configuration = new
            {
                schema = _configurationSchema,
                sha256 = _configurationSha256,
                sampleIntervalSeconds = _configuration.SampleIntervalSeconds,
            },
            sampleCount = _sampleCount,
            metrics = new
            {
                framesPerSecond = _framesPerSecond.Report("frames-per-second"),
                processSeconds = _processSeconds.Report("seconds"),
                physicsProcessSeconds = _physicsProcessSeconds.Report("seconds"),
                nodeCount = _nodeCount.Report("nodes"),
                orphanNodeCount = _orphanNodeCount.Report("nodes"),
                staticMemoryBytes = _staticMemoryBytes.Report("bytes"),
                renderedObjectCount = _renderedObjectCount.Report("objects"),
                renderedPrimitiveCount = _renderedPrimitiveCount.Report("primitives"),
            },
        };
        var fullPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
        File.Move(temporaryPath, fullPath, true);
        GD.Print(
            $"OPENNV_PERF_REPORT path={fullPath} samples={_sampleCount} thresholds=none");
    }

    private struct PerformanceMetricAccumulator
    {
        private long _sampleCount;
        private double _minimum;
        private double _maximum;
        private double _sum;

        internal void Add(double value)
        {
            if (!double.IsFinite(value))
                return;
            if (_sampleCount == 0)
            {
                _minimum = value;
                _maximum = value;
            }
            else
            {
                _minimum = Math.Min(_minimum, value);
                _maximum = Math.Max(_maximum, value);
            }
            _sum += value;
            _sampleCount++;
        }

        internal object Report(string unit) => new
        {
            unit,
            sampleCount = _sampleCount,
            minimum = _sampleCount == 0 ? 0.0 : _minimum,
            maximum = _sampleCount == 0 ? 0.0 : _maximum,
            average = _sampleCount == 0 ? 0.0 : _sum / _sampleCount,
        };
    }
}
