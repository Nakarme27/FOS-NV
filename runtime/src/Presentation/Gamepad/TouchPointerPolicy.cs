using Godot;

namespace OpenNV.Runtime.Presentation.Gamepad;

/// <summary>
/// Pointer capture policy. The runtime locks the pointer after every interaction so the
/// mouse keeps driving the camera, which is right on desktop. On a touch host there is no
/// pointer to lock, and locking it removes the position from every touch event: the soft
/// keyboard, the menus and the on-screen pad all stop responding. Touch hosts therefore
/// keep the pointer visible and let the pad own the camera.
/// </summary>
internal static class TouchPointerPolicy
{
    private static readonly bool TouchHost = OS.GetName() is "Android" or "iOS";

    internal static bool IsTouchHost => TouchHost;

    internal static void Capture()
    {
        if (!TouchHost) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    internal static void Release()
    {
        if (!TouchHost) Input.MouseMode = Input.MouseModeEnum.Visible;
    }
}
