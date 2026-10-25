using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

/// <summary>
/// OpenNV reads an owned Fallout installation through System.IO. On Android 11+
/// scoped storage blocks raw paths until the user grants the special "All files
/// access" permission. SAF tree URIs are intentionally not used here because the
/// runtime needs a real filesystem path for the complete Bethesda data tree.
/// </summary>
internal static class AndroidStorageAccess
{
    internal const string ReadExternalStoragePermission =
        "android.permission.READ_EXTERNAL_STORAGE";

    private const string ManageAppAllFilesAction =
        "android.settings.MANAGE_APP_ALL_FILES_ACCESS_PERMISSION";
    private const string ManageAllFilesAction =
        "android.settings.MANAGE_ALL_FILES_ACCESS_PERMISSION";

    internal static bool IsAndroid =>
        OS.GetName().Equals("Android", StringComparison.OrdinalIgnoreCase);

    internal static bool HasRequiredAccess()
    {
        if (!IsAndroid)
            return true;

        try
        {
            if (AndroidSdkInt() < 30)
                return OS.GetGrantedPermissions().Contains(ReadExternalStoragePermission);

            var environment = WrapJavaClass("android.os.Environment");
            return environment?.Call("isExternalStorageManager").AsBool() == true;
        }
        catch (Exception exception)
        {
            GD.PushWarning($"OPENNV_ANDROID_STORAGE_CHECK_FAIL {exception}");
            return false;
        }
    }

    internal static void RequestRequiredAccess()
    {
        if (!IsAndroid || HasRequiredAccess())
            return;

        try
        {
            if (AndroidSdkInt() < 30)
            {
                OS.RequestPermission(ReadExternalStoragePermission);
                return;
            }

            OpenAllFilesSettings(ManageAppAllFilesAction, includePackage: true);
        }
        catch (Exception primaryException)
        {
            GD.PushWarning($"OPENNV_ANDROID_STORAGE_SETTINGS_PRIMARY_FAIL {primaryException}");
            try
            {
                OpenAllFilesSettings(ManageAllFilesAction, includePackage: false);
            }
            catch (Exception fallbackException)
            {
                GD.PushError($"OPENNV_ANDROID_STORAGE_SETTINGS_FAIL {fallbackException}");
            }
        }
    }

    internal static string RequiredAccessMessage =>
        "Android is blocking access to your Fallout files. Enable ‘All files access’ " +
        "for FOS NV, return to the app, then choose the Fallout: New Vegas folder " +
        "again (the folder containing Data, or Data itself).";

    private static int AndroidSdkInt()
    {
        var version = WrapJavaClass("android.os.Build$VERSION")
            ?? throw new InvalidOperationException("Android Build.VERSION is unavailable.");
        return version.Get("SDK_INT").AsInt32();
    }

    private static GodotObject? WrapJavaClass(string className)
    {
        var wrapper = Engine.GetSingleton("JavaClassWrapper");
        return wrapper?.Call("wrap", className).AsGodotObject();
    }

    private static void OpenAllFilesSettings(string action, bool includePackage)
    {
        var runtime = Engine.GetSingleton("AndroidRuntime")
            ?? throw new InvalidOperationException("AndroidRuntime singleton is unavailable.");
        var activity = runtime.Call("getActivity").AsGodotObject()
            ?? throw new InvalidOperationException("Android Activity is unavailable.");
        var intentClass = WrapJavaClass("android.content.Intent")
            ?? throw new InvalidOperationException("android.content.Intent is unavailable.");
        var intent = intentClass.Call("Intent", action).AsGodotObject()
            ?? throw new InvalidOperationException("Could not create Android settings Intent.");

        if (includePackage)
        {
            var uriClass = WrapJavaClass("android.net.Uri")
                ?? throw new InvalidOperationException("android.net.Uri is unavailable.");
            var packageName = activity.Call("getPackageName").AsString();
            var packageUri = uriClass.Call("parse", $"package:{packageName}").AsGodotObject()
                ?? throw new InvalidOperationException("Could not create Android package URI.");
            intent.Call("setData", packageUri);
        }

        activity.Call("startActivity", intent);
    }
}
