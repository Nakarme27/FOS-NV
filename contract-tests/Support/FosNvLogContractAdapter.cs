namespace OpenNV.Runtime.Diagnostics;

// Installation contracts run without a Godot process. Preserve diagnostic
// observations on the console instead of bringing the Android/file sink into
// this standalone test executable. Product logging has its own Android gate.
internal static class FosNvLog
{
    internal static void Info(string eventName, string message) =>
        Console.WriteLine($"{eventName}: {message}");

    internal static void Exception(string eventName, Exception exception, string? context = null) =>
        Console.Error.WriteLine($"{eventName}: {context} {exception}");
}
