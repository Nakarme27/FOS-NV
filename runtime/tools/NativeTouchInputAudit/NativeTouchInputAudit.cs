using Godot;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.Presentation.Gamepad;

namespace OpenNV.Runtime.Tools;

public partial class NativeTouchInputAudit : Node
{
    private int _confirm, _cancel, _mouseDown, _mouseUp;
    public override void _Input(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Keycode: Key.Enter }) _confirm++;
        if (input is InputEventKey { Pressed: true, Keycode: Key.Escape }) _cancel++;
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse)
        { if (mouse.Pressed) _mouseDown++; else _mouseUp++; }
    }

    public override void _Ready()
    {
        try
        {
            DesktopInputMap.Configure(RuntimeConfiguration.Load().Player.DesktopInput);
            var gameplay = true;
            var pad = new NativeVirtualGamepad { GameplayInput = () => gameplay };
            AddChild(pad);
            var size = GetViewport().GetVisibleRect().Size;
            var h = Mathf.Min(size.Y, size.X / 1.8f);
            var left = new Vector2(h * .28f, size.Y - h * .26f);
            var right = new Vector2(size.X - h * .95f, size.Y - h * .26f);
            var fire = new Vector2(size.X - h * .18f, h * .16f);
            Touch(pad, 0, left + new Vector2(h * .08f, 0), true);
            Touch(pad, 1, right + new Vector2(0, -h * .08f), true);
            Touch(pad, 2, fire, true);
            pad._Process(.016); Input.FlushBufferedEvents();
            Require(pad.Movement.X > 0 && pad.Movement.X < 1 && pad.Look.Y < 0, "analog sticks");
            Require(Input.IsActionPressed("flat_fire"), "RT reaches gameplay action");
            Touch(pad, 3, left, true);
            Touch(pad, 3, left, false);
            Require(pad.Movement.X > 0, "second finger cannot steal stick");
            Touch(pad, 2, fire, false); pad._Process(.016); Input.FlushBufferedEvents();
            Require(!Input.IsActionPressed("flat_fire") && pad.Movement.X > 0, "independent trigger release");
            var aim = new Vector2(h * .44f, h * .10f);
            Touch(pad, 2, aim, true); pad._Process(.016); Input.FlushBufferedEvents();
            Require(Input.IsActionPressed("flat_aim") && !Input.IsActionPressed("flat_sprint"), "LT aims");
            pad._Notification((int)NotificationApplicationFocusOut); Input.FlushBufferedEvents();
            Require(pad.Movement == Vector2.Zero && pad.Look == Vector2.Zero && !Input.IsActionPressed("flat_aim"), "focus loss releases all");
            Touch(pad, 0, fire, true); pad._Process(.016); Input.FlushBufferedEvents();
            gameplay = false; pad._Process(.016); Input.FlushBufferedEvents();
            Require(!Input.IsActionPressed("flat_fire"), "modal transition releases trigger");
            gameplay = true; pad._Process(.016); Input.FlushBufferedEvents();
            Require(pad.Movement == Vector2.Zero && !Input.IsActionPressed("flat_fire"), "closing menu cannot resume held input");
            Touch(pad, 0, fire, true); pad._Process(.016); Input.FlushBufferedEvents();
            using var cancel = new InputEventScreenTouch { Index = 0, Canceled = true };
            pad._Input(cancel); Input.FlushBufferedEvents();
            Require(!Input.IsActionPressed("flat_fire"), "canceled touch releases trigger");
            gameplay = false; pad._Process(.016); Input.FlushBufferedEvents();
            var face = new Vector2(size.X - h * .26f, size.Y - h * .54f);
            Touch(pad, 0, face + new Vector2(0, h * .128f), true);
            pad._Process(.016); Input.FlushBufferedEvents();
            Touch(pad, 0, face, false);
            Touch(pad, 1, face + new Vector2(h * .128f, 0), true);
            pad._Process(.016); Input.FlushBufferedEvents();
            Touch(pad, 1, face, false);
            Require(_confirm == 1 && _cancel == 1, "modal A confirms and B cancels once");
            var pointer = new Vector2(size.X / 2, h * .4f);
            Touch(pad, 0, pointer, true); Input.FlushBufferedEvents();
            Touch(pad, 1, pointer, true); Input.FlushBufferedEvents();
            Touch(pad, 1, pointer, false); Input.FlushBufferedEvents();
            Touch(pad, 0, pointer, false); Input.FlushBufferedEvents();
            Require(_mouseDown == 1 && _mouseUp == 1, "menu pointer has one finger owner");
            pad.QueueFree();
            GD.Print("OPENNV_TOUCH_INPUT_AUDIT_PASS");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString()); GetTree().Quit(1);
        }
    }

    private static void Touch(NativeVirtualGamepad pad, int index, Vector2 position, bool pressed)
    {
        using var input = new InputEventScreenTouch { Index = index, Position = position, Pressed = pressed };
        pad._Input(input);
    }

    private static void Require(bool condition, string contract)
    {
        if (!condition) throw new InvalidOperationException("Touch input failed: " + contract);
    }
}
