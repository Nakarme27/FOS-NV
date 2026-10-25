using Godot;
using System.Collections.Generic;

namespace OpenNV.Runtime.Presentation.Gamepad;

/// <summary>Multitouch adapter for the native player's actions and modal menu navigation.</summary>
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
        (Zone.TriggerLeft, "flat_aim"),
        (Zone.BumperRight, "flat_unavailable_vats"),
        (Zone.BumperLeft, "flat_view"),
        (Zone.Back, "flat_unavailable_wait"),
    ];

    private readonly Dictionary<int, Zone> _touches = new();
    private readonly HashSet<Zone> _held = [];

    private Vector2 _left = Vector2.Zero;
    private Vector2 _right = Vector2.Zero;

    internal System.Func<bool>? GameplayInput { get; set; }
    private bool Gameplay => GameplayInput?.Invoke() == true;
    private bool _wasGameplay;
    private bool _previousMouseEmulation;
    private int? _pointerFinger;
    private Vector2 _pointerPosition;
    internal event System.Action? InputCanceled;
    private string? _menuDirection;
    private double _menuRepeat;
    private readonly Queue<(Zone Zone, bool Pressed)> _edges = new();
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

    private Vector2 _start;


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
        _wasGameplay = Gameplay;
        _previousMouseEmulation = Input.EmulateMouseFromTouch;
        Input.EmulateMouseFromTouch = false;
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
        ResetInput();
        Input.EmulateMouseFromTouch = _previousMouseEmulation;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) { ResetInput(); RebuildLayout(); }
        if (what is (int)NotificationApplicationFocusOut or (int)NotificationApplicationPaused) ResetInput();
    }

    private void RebuildLayout()
    {
        var size = GetViewportRect().Size;
        if (size.X < 2.0f || size.Y < 2.0f) return;
        _height = Mathf.Min(size.Y, size.X / 1.8f);
        _stickRadius = _height * 0.155f;
        _knobRadius = _stickRadius * KnobFraction;
        _buttonRadius = _height * ButtonFraction;
        _triggerRadius = _height * TriggerFraction;
        _menuRadius = _height * MenuFraction;
        _buttonGap = _height * ButtonGapFraction;
        _labelSize = Mathf.Max(10, (int)(_height * LabelFraction));
        _leftCenter = new Vector2(_height * 0.28f, size.Y - _height * 0.26f);
        _rightCenter = new Vector2(size.X - _height * 0.95f, size.Y - _height * 0.26f);
        _faceCenter = new Vector2(size.X - _height * 0.26f, size.Y - _height * 0.54f);
        _triggerLeft = new Vector2(_height * 0.44f, _height * 0.10f);
        _triggerRight = new Vector2(size.X - _height * 0.18f, _height * 0.16f);
        _bumperLeft = new Vector2(_height * 0.20f, _height * 0.16f);


        _start = new Vector2(size.X * 0.56f, _height * 0.09f);
        QueueRedraw();
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsVisibleInTree()) return;
        if (Gameplay && @event is InputEventMouse { Device: -1 })
        { GetViewport().SetInputAsHandled(); return; }
        switch (@event)
        {
            case InputEventScreenTouch { Canceled: true }:
                ResetInput(); GetViewport().SetInputAsHandled(); break;
            case InputEventScreenTouch { Pressed: true } touch:
                if (ZoneAt(touch.Position) is not { } pressed)
                {
                    if (!Gameplay && _pointerFinger is null)
                    {
                        _pointerFinger = touch.Index; _pointerPosition = touch.Position;
                        PointerButton(true);
                    }
                    GetViewport().SetInputAsHandled(); return;
                }
                if ((pressed is Zone.LeftStick or Zone.RightStick) && _touches.ContainsValue(pressed))
                { GetViewport().SetInputAsHandled(); return; }
                _touches[touch.Index] = pressed;
                Begin(pressed, touch.Position);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventScreenTouch touch:
                if (_pointerFinger == touch.Index)
                { PointerButton(false); _pointerFinger = null; }
                if (!_touches.Remove(touch.Index, out var released)) return;
                if (!_touches.ContainsValue(released)) Release(released);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventScreenDrag drag:
                if (_pointerFinger == drag.Index)
                {
                    using var motion = new InputEventMouseMotion
                    {
                        Position = drag.Position,
                        GlobalPosition = drag.Position,
                        Relative = drag.Position - _pointerPosition,
                        ButtonMask = MouseButtonMask.Left,
                    };
                    _pointerPosition = drag.Position;
                    Input.ParseInputEvent(motion); GetViewport().SetInputAsHandled(); return;
                }
                if (!_touches.TryGetValue(drag.Index, out var dragged)) return;
                Drag(dragged, drag.Position);
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private Zone? ZoneAt(Vector2 position)
    {
        if (!Gameplay)
        {
            if (position.DistanceTo(_leftCenter) <= _stickRadius) return Zone.LeftStick;
            if (position.DistanceTo(ButtonCenter(Zone.A)) <= _buttonRadius) return Zone.A;
            if (position.DistanceTo(ButtonCenter(Zone.B)) <= _buttonRadius) return Zone.B;
            if (position.DistanceTo(_start) <= _menuRadius) return Zone.Start;
            return null;
        }
        if (position.DistanceTo(_leftCenter) <= _stickRadius) return Zone.LeftStick;
        if (position.DistanceTo(_rightCenter) <= _stickRadius) return Zone.RightStick;
        if (position.DistanceTo(ButtonCenter(Zone.Y)) <= _buttonRadius) return Zone.Y;
        if (position.DistanceTo(ButtonCenter(Zone.A)) <= _buttonRadius) return Zone.A;
        if (position.DistanceTo(ButtonCenter(Zone.X)) <= _buttonRadius) return Zone.X;
        if (position.DistanceTo(ButtonCenter(Zone.B)) <= _buttonRadius) return Zone.B;
        if (position.DistanceTo(_triggerRight) <= _triggerRadius) return Zone.TriggerRight;
        if (position.DistanceTo(_triggerLeft) <= _triggerRadius) return Zone.TriggerLeft;

        if (position.DistanceTo(_bumperLeft) <= _triggerRadius) return Zone.BumperLeft;
        if (position.DistanceTo(_start) <= _menuRadius) return Zone.Start;

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
        else if (zone == Zone.Start) _edges.Enqueue((zone, true));
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

    private void SetAction(Zone zone, bool pressed) => _edges.Enqueue((zone, pressed));

    private static void EmitAction(Zone zone, bool pressed)
    {
        foreach (var (target, action) in ActionButtons)
        {
            if (target != zone || action.StartsWith("flat_unavailable_", System.StringComparison.Ordinal)) continue;
            using var input = new InputEventAction { Action = action, Pressed = pressed };
            Input.ParseInputEvent(input);
            return;
        }
    }

    private void PointerButton(bool pressed)
    {
        using var input = new InputEventMouseButton
        {
            Position = _pointerPosition,
            GlobalPosition = _pointerPosition,
            ButtonIndex = MouseButton.Left,
            Pressed = pressed,
        };
        Input.ParseInputEvent(input);
    }

    internal void ResetInput()
    {
        InputCanceled?.Invoke();
        if (_pointerFinger is not null) { _pointerPosition = new(-1, -1); PointerButton(false); }
        _pointerFinger = null;
        _edges.Clear();
        foreach (var zone in _held) EmitAction(zone, false);
        _held.Clear(); _touches.Clear();
        _left = _right = Vector2.Zero;
        _menuDirection = null; _menuRepeat = 0;
        QueueRedraw();
    }

    private static void MenuKey(Key key)
    {
        using var down = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true };
        using var up = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false };
        Input.ParseInputEvent(down); Input.ParseInputEvent(up);
    }

    public override void _Process(double delta)
    {
        if (_wasGameplay != Gameplay)
        {
            ResetInput(); _wasGameplay = Gameplay; QueueRedraw();
        }
        while (_edges.TryDequeue(out var edge))
        {
            if (!Gameplay)
            {
                if (edge.Pressed) MenuKey(edge.Zone == Zone.A ? Key.Enter : Key.Escape);
            }
            else if (edge.Zone == Zone.Start)
            {
                if (edge.Pressed) PauseRequested?.Invoke();
            }
            else EmitAction(edge.Zone, edge.Pressed);
        }
        if (Gameplay) return;
        var direction = _left.Length() < .5f ? null : Mathf.Abs(_left.X) > Mathf.Abs(_left.Y)
            ? (_left.X < 0 ? "Left" : "Right") : (_left.Y < 0 ? "Up" : "Down");
        _menuRepeat -= delta;
        if (direction is not null && (direction != _menuDirection || _menuRepeat <= 0))
        {
            MenuKey(System.Enum.Parse<Key>(direction));
            _menuRepeat = direction == _menuDirection ? .12 : .35;
        }
        _menuDirection = direction;
    }

    public override void _Draw()
    {
        if (_height < 1.0f) return;
        var font = ThemeDB.FallbackFont;
        DrawStick(_leftCenter, _left);
        if (Gameplay) DrawStick(_rightCenter, _right);
        if (Gameplay) DrawFace(Zone.Y, "Y", font);
        DrawFace(Zone.A, "A", font);
        if (Gameplay) DrawFace(Zone.X, "X", font);
        DrawFace(Zone.B, "B", font);
        if (Gameplay)
        {
            DrawRound(_bumperLeft, _triggerRadius, _held.Contains(Zone.BumperLeft));
            DrawRound(_triggerLeft, _triggerRadius, _held.Contains(Zone.TriggerLeft));
            DrawRound(_triggerRight, _triggerRadius, _held.Contains(Zone.TriggerRight));
            DrawLabel(_bumperLeft, "LB", font);
            DrawLabel(_triggerLeft, "LT", font);
            DrawLabel(_triggerRight, "RT", font);
        }
        DrawRound(_start, _menuRadius, false);
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
