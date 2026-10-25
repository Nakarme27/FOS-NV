using Godot;

namespace OpenNV.Runtime.Diagnostics;

/// <summary>
/// Product diagnostics that remain available after the Android app closes.
/// Android writes to the public Download/FOS NV folder once storage access is
/// available. Lines produced before that permission is granted are buffered and
/// copied there on the first successful write.
/// </summary>
internal static class FosNvLog
{
    private const string AndroidDownloadRoot = "/storage/emulated/0/Download";
    private const string LogDirectoryName = "FOS NV";
    private const string LogFileName = "fosnv.log";
    private const long MaxLogBytes = 4L * 1024L * 1024L;
    private const int MaxPendingLines = 256;

    private static readonly object Gate = new();
    private static readonly Queue<string> PendingExternalLines = new();
    private static bool _godotHostInitialized;

    internal static string PreferredLogDirectory =>
        OS.GetName().Equals("Android", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(AndroidDownloadRoot, LogDirectoryName)
            : Path.Combine(ProjectSettings.GlobalizePath("user://"), LogDirectoryName);

    internal static string PreferredLogPath => Path.Combine(PreferredLogDirectory, LogFileName);

    internal static void Initialize()
    {
        lock (Gate)
        {
            if (_godotHostInitialized)
                return;
            _godotHostInitialized = true;
        }

        Info(
            "session_start",
            $"platform={OS.GetName()} log={PreferredLogPath} utc={DateTimeOffset.UtcNow:O}");
    }

    internal static void Info(string eventName, string message) =>
        Write("INFO", eventName, message);

    internal static void Error(string eventName, string message) =>
        Write("ERROR", eventName, message);

    internal static void Exception(string eventName, Exception exception, string? context = null)
    {
        var prefix = string.IsNullOrWhiteSpace(context) ? string.Empty : context + " | ";
        Write("ERROR", eventName, prefix + exception);
    }

    /// <summary>
    /// Records exactly what System.IO can see for a folder chosen by the launcher.
    /// This intentionally runs before NativeGameInstallation.Detect so a failed
    /// detector still leaves enough evidence to diagnose Android path/permission
    /// problems.
    /// </summary>
    internal static void ProbeSelectedGameFolder(string path)
    {
        Info("folder_selected", $"raw={path}");

        try
        {
            var full = Path.GetFullPath(path);
            var exists = Directory.Exists(full);
            if (!exists)
            {
                Info("folder_probe", $"full={full} exists=false");
                return;
            }

            var selectedIsData = Path.GetFileName(full)
                .Equals("Data", StringComparison.OrdinalIgnoreCase);
            var dataRoot = selectedIsData ? full : FindDirectory(full, "Data");
            var selectedMaster = ContainsFile(full, "FalloutNV.esm");
            var dataExists = dataRoot is not null && Directory.Exists(dataRoot);
            var dataMaster = dataRoot is not null && ContainsFile(dataRoot, "FalloutNV.esm");
            var masters = dataRoot is null
                ? EnumerateMasterNames(full)
                : EnumerateMasterNames(dataRoot);

            Info(
                "folder_probe",
                $"full={full} exists=true selectedIsData={selectedIsData} " +
                $"dataRoot={dataRoot ?? "<missing>"} dataExists={dataExists} " +
                $"FalloutNV.esm[selected]={selectedMaster} FalloutNV.esm[data]={dataMaster} " +
                $"masters=[{string.Join(", ", masters)}]");
        }
        catch (Exception exception)
        {
            Exception("folder_probe_fail", exception, $"raw={path}");
        }
    }

    private static string[] EnumerateMasterNames(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root)
                .Where(file => Path.GetExtension(file).Equals(".esm", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Take(32)
                .Cast<string>()
                .ToArray();
        }
        catch (Exception exception)
        {
            return [$"<enumeration failed: {exception.GetType().Name}: {exception.Message}>"];
        }
    }

    private static string? FindDirectory(string root, string name) =>
        Directory.EnumerateDirectories(root)
            .FirstOrDefault(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsFile(string root, string name) =>
        Directory.EnumerateFiles(root)
            .Any(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase));

    private static void Write(string level, string eventName, string message)
    {
        var sanitized = message.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "\n    ", StringComparison.Ordinal);
        var line = $"{DateTimeOffset.UtcNow:O} [{level}] {eventName} {sanitized}";

        // Assembly-backed command-line probes have no Godot native bindings.
        // Only an explicit Initialize call from the Godot host enables its sink.
        if (!System.Threading.Volatile.Read(ref _godotHostInitialized))
        {
            Console.WriteLine($"FOSNV_LOG {line}");
            lock (Gate) EnqueuePending(line);
            return;
        }
        GD.Print($"FOSNV_LOG {line}");

        lock (Gate)
        {
            if (TryWritePreferred(line))
                return;

            EnqueuePending(line);
            TryWriteFallback(line);
        }
    }

    private static bool TryWritePreferred(string line)
    {
        try
        {
            Directory.CreateDirectory(PreferredLogDirectory);
            RotateIfNeeded(PreferredLogPath);

            if (PendingExternalLines.Count != 0)
            {
                File.AppendAllLines(PreferredLogPath, PendingExternalLines);
                PendingExternalLines.Clear();
            }

            File.AppendAllText(PreferredLogPath, line + System.Environment.NewLine);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryWriteFallback(string line)
    {
        try
        {
            var fallback = ProjectSettings.GlobalizePath("user://logs/fosnv.log");
            var directory = Path.GetDirectoryName(fallback);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            RotateIfNeeded(fallback);
            File.AppendAllText(fallback, line + System.Environment.NewLine);
        }
        catch
        {
            // Logging must never become a reason for the product to fail.
        }
    }

    private static void EnqueuePending(string line)
    {
        while (PendingExternalLines.Count >= MaxPendingLines)
            PendingExternalLines.Dequeue();
        PendingExternalLines.Enqueue(line);
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxLogBytes)
            return;

        var previous = path + ".previous";
        if (File.Exists(previous))
            File.Delete(previous);
        File.Move(path, previous);
    }
}
