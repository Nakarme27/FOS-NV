using Godot;
using System.Collections.Generic;

namespace OpenNV.Runtime.Presentation.Gamepad;

/// <summary>
/// On-screen pad for touch devices, laid out like the Xbox 360 retail binding:
/// left stick moves, right stick looks, A activates, B opens the Pip-Boy, X reloads,
/// Y jumps, RT fires, RB grabs, Back saves and Start opens the pause menu.
///
/// The two triggers and LB carry no retail mechanic in this runtime — the source game
/// maps them to V.A.T.S. and the first/third-person toggle, neither of which the port
/// implements — so they hold the two actions the port does have and the keyboard cannot
/// reach on a phone: LT is sprint, LB is cancel.
///
/// Buttons are delivered as the project's own flat_* actions through Input.ParseInputEvent,
/// so every gameplay path keeps reading Input exactly as it does for a keyboard. The
/// sticks are exposed as vectors instead, because a digital action cannot express half
/// deflection and Input.GetVector only reports the eight action directions.
/// </summary>
internal sealed partial class NativeVirtualGamepad : Control
{
    private const float DeadZone = 0.18f;
    private const float KnobFraction = 0.38f;
    private const float ButtonFraction = 0.060f;
    private const float TriggerFraction = 0.072f;
    private const float MenuFraction = 0.040f;
    private const float ButtonGapFraction = 0.128f;
    private const float LabelFraction = 0.030f;

    private static readonly Color Outline = new(1.0f, 1.0f, 1.0f, 0.30f);
    private static readonly Color Fill = new(0.0f, 0.0f, 0.0f, 0.26f);
    private static readonly Color Held = new(0.42f, 0.95f, 0.48f, 0.42f);
    private static readonly Color Label = new(1.0f, 1.0f, 1.0f, 0.82f);

    private enum Zone { LeftStick, RightStick, A, B, X, Y, TriggerRight, TriggerLeft, BumperRight, BumperLeft, Start, Back }

    private static readonly (Zone Zone, string Action)[] ActionButtons =
    [
        (Zone.A, "flat_activate"),
        (Zone.B, "flat_pip_boy"),
        (Zone.X, "flat_reload"),
        (Zone.Y, "flat_jump"),
        (Zone.TriggerRight, "flat_fire"),
        (Zone.TriggerLeft, "flat_sprint"),
        (Zone.BumperRight, "flat_grab"),
        (Zone.BumperLeft, "flat_cancel"),
        (Zone.Back, "flat_save"),
    ];

    private readonly Dictionary<int, Zone> _touches = new();
    private readonly HashSet<Zone> _held = [];

    private Vector2 _left = Vector2.Zero;
    private Vector2 _right = Vector2.Zero;

    private float _height;
    private float _stickRadius;
    private float _knobRadius;
    private float _buttonRadius;
    private float _triggerRadius;
    private float _menuRadius;
    private float _buttonGap;
    private int _labelSize;
    private Vector2 _leftCenter;
    private Vector2 _rightCenter;
    private Vector2 _faceCenter;
    private Vector2 _triggerLeft;
    private Vector2 _triggerRight;
    private Vector2 _bumperLeft;
    private Vector2 _bumperRight;
    private Vector2 _start;
    private Vector2 _back;

    /// <summary>The pad currently in the tree, or null outside gameplay.</summary>
    internal static NativeVirtualGamepad? Current { get; private set; }

    /// <summary>Left stick as a movement vector: Y positive is forward.</summary>
    internal Vector2 Movement => new(_left.X, -_left.Y);

    /// <summary>Right stick as a look vector: Y negative is up.</summary>
    internal Vector2 Look => _right;

    internal event System.Action? PauseRequested;

    public override void _Ready()
    {
        Current = this;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        // A node built in C# carries no Godot script, so the virtual callbacks are not
        // wired up on their own: _Input only fires once input processing is enabled.
        SetProcessInput(true);
        // The runtime pauses the tree for every modal interaction (name entry, containers,
        // the pause menu). An inheriting node stops receiving input with it, which would
        // leave the player with no controls exactly when a menu is open.
        ProcessMode = ProcessModeEnum.Always;
        RebuildLayout();
    }

    public override void _ExitTree()
    {
        if (Current == this) Current = null;
        foreach (var zone in new List<Zone>(_held)) Release(zone);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) RebuildLayout();
    }

    private void RebuildLayout()
    {
        var size = GetViewportRect().Size;
        if (size.X < 2.0f || size.Y < 2.0f) return;
        _height = size.Y;
        _stickRadius = _height * 0.155f;
        _knobRadius = _stickRadius * KnobFraction;
        _buttonRadius = _height * ButtonFraction;
        _triggerRadius = _height * TriggerFraction;
        _menuRadius = _height * MenuFraction;
        _buttonGap = _height * ButtonGapFraction;
        _labelSize = Mathf.Max(10, (int)(_height * LabelFraction));
        _leftCenter = new Vector2(_height * 0.28f, _height * 0.74f);
        _rightCenter = new Vector2(size.X - _height * 0.95f, _height * 0.74f);
        _faceCenter = new Vector2(size.X - _height * 0.26f, _height * 0.46f);
        _triggerLeft = new Vector2(_height * 0.44f, _height * 0.10f);
        _triggerRight = new Vector2(size.X - _height * 0.18f, _height * 0.16f);
        _bumperLeft = new Vector2(_height * 0.20f, _height * 0.16f);
        _bumperRight = new Vector2(size.X - _height * 0.46f, _height * 0.10f);
        _back = new Vector2(size.X * 0.44f, _height * 0.09f);
        _start = new Vector2(size.X * 0.56f, _height * 0.09f);
        QueueRedraw();
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsVisibleInTree()) return;
        switch (@event)
        {
            case InputEventScreenTouch { Pressed: true } touch:
                if (ZoneAt(touch.Position) is not { } pressed) return;
                _touches[touch.Index] = pressed;
                Begin(pressed, touch.Position);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventScreenTouch touch:
                if (!_touches.Remove(touch.Index, out var released)) return;
                Release(released);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventScreenDrag drag:
                if (!_touches.TryGetValue(drag.Index, out var dragged)) return;
                Drag(dragged, drag.Position);
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private Zone? ZoneAt(Vector2 position)
    {
        if (position.DistanceTo(_leftCenter) <= _stickRadius) return Zone.LeftStick;
        if (position.DistanceTo(_rightCenter) <= _stickRadius) return Zone.RightStick;
        if (position.DistanceTo(ButtonCenter(Zone.Y)) <= _buttonRadius) return Zone.Y;
        if (position.DistanceTo(ButtonCenter(Zone.A)) <= _buttonRadius) return Zone.A;
        if (position.DistanceTo(ButtonCenter(Zone.X)) <= _buttonRadius) return Zone.X;
        if (position.DistanceTo(ButtonCenter(Zone.B)) <= _buttonRadius) return Zone.B;
        if (position.DistanceTo(_triggerRight) <= _triggerRadius) return Zone.TriggerRight;
        if (position.DistanceTo(_triggerLeft) <= _triggerRadius) return Zone.TriggerLeft;
        if (position.DistanceTo(_bumperRight) <= _triggerRadius) return Zone.BumperRight;
        if (position.DistanceTo(_bumperLeft) <= _triggerRadius) return Zone.BumperLeft;
        if (position.DistanceTo(_start) <= _menuRadius) return Zone.Start;
        if (position.DistanceTo(_back) <= _menuRadius) return Zone.Back;
        return null;
    }

    private Vector2 ButtonCenter(Zone zone) => zone switch
    {
        Zone.Y => _faceCenter + new Vector2(0.0f, -_buttonGap),
        Zone.A => _faceCenter + new Vector2(0.0f, _buttonGap),
        Zone.X => _faceCenter + new Vector2(-_buttonGap, 0.0f),
        Zone.B => _faceCenter + new Vector2(_buttonGap, 0.0f),
        _ => _faceCenter,
    };

    private void Begin(Zone zone, Vector2 position)
    {
        if (zone is Zone.LeftStick or Zone.RightStick) Drag(zone, position);
        else if (zone == Zone.Start) PauseRequested?.Invoke();
        else if (_held.Add(zone)) SetAction(zone, true);
        QueueRedraw();
    }

    private void Drag(Zone zone, Vector2 position)
    {
        if (zone == Zone.LeftStick) _left = Deflection(position, _leftCenter);
        else if (zone == Zone.RightStick) _right = Deflection(position, _rightCenter);
        else return;
        QueueRedraw();
    }

    private Vector2 Deflection(Vector2 position, Vector2 center)
    {
        var offset = position - center;
        var length = offset.Length();
        if (length < 1.0f) return Vector2.Zero;
        var normalized = offset / length;
        var magnitude = Mathf.Min(length / _stickRadius, 1.0f);
        // Rescale past the dead zone so the first pixel of travel is already meaningful.
        magnitude = magnitude <= DeadZone ? 0.0f : (magnitude - DeadZone) / (1.0f - DeadZone);
        return normalized * magnitude;
    }

    private void Release(Zone zone)
    {
        switch (zone)
        {
            case Zone.LeftStick: _left = Vector2.Zero; break;
            case Zone.RightStick: _right = Vector2.Zero; break;
            case Zone.Start: break;
            default:
                if (_held.Remove(zone)) SetAction(zone, false);
                break;
        }
        QueueRedraw();
    }

    private void SetAction(Zone zone, bool pressed)
    {
        foreach (var (target, action) in ActionButtons)
        {
            if (target != zone) continue;
            Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed });
            GD.Print($"OPENNV_VIRTUAL_PAD button={action} pressed={pressed}");
            return;
        }
    }

    public override void _Draw()
    {
        if (_height < 1.0f) return;
        var font = ThemeDB.FallbackFont;
        DrawStick(_leftCenter, _left);
        DrawStick(_rightCenter, _right);
        DrawFace(Zone.Y, "Y", font);
        DrawFace(Zone.A, "A", font);
        DrawFace(Zone.X, "X", font);
        DrawFace(Zone.B, "B", font);
        DrawRound(_bumperLeft, _triggerRadius, _held.Contains(Zone.BumperLeft));
        DrawRound(_bumperRight, _triggerRadius, _held.Contains(Zone.BumperRight));
        DrawRound(_triggerLeft, _triggerRadius, _held.Contains(Zone.TriggerLeft));
        DrawRound(_triggerRight, _triggerRadius, _held.Contains(Zone.TriggerRight));
        DrawRound(_back, _menuRadius, _held.Contains(Zone.Back));
        DrawRound(_start, _menuRadius, false);
        DrawLabel(_bumperLeft, "LB", font);
        DrawLabel(_bumperRight, "RB", font);
        DrawLabel(_triggerLeft, "LT", font);
        DrawLabel(_triggerRight, "RT", font);
        DrawLabel(_back, "▣", font);
        DrawLabel(_start, "▶", font);
    }

    private void DrawStick(Vector2 center, Vector2 value)
    {
        DrawCircle(center, _stickRadius, Fill);
        DrawArc(center, _stickRadius, 0.0f, Mathf.Tau, 48, Outline, 3.0f, true);
        var knob = center + value * (_stickRadius - _knobRadius);
        DrawCircle(knob, _knobRadius, value == Vector2.Zero ? Fill : Held);
        DrawArc(knob, _knobRadius, 0.0f, Mathf.Tau, 32, Outline, 2.0f, true);
    }

    private void DrawFace(Zone zone, string text, Font font)
    {
        DrawRound(ButtonCenter(zone), _buttonRadius, _held.Contains(zone));
        DrawLabel(ButtonCenter(zone), text, font);
    }

    private void DrawRound(Vector2 center, float radius, bool held)
    {
        DrawCircle(center, radius, held ? Held : Fill);
        DrawArc(center, radius, 0.0f, Mathf.Tau, 32, Outline, 2.5f, true);
    }

    private void DrawLabel(Vector2 center, string text, Font font)
    {
        var size = font.GetStringSize(text, fontSize: _labelSize);
        DrawString(font, center - new Vector2(size.X * 0.5f, -size.Y * 0.32f), text,
            fontSize: _labelSize, modulate: Label);
    }
}
