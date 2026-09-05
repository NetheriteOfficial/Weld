using System.Numerics;
using Silk.NET.Input;
using Weld.Entities;

namespace Weld.Carisle.Game;

internal sealed class LocalPlayerController
{
    private readonly IInputContext _input;
    private Vector2 _lastMouse;
    private bool _jumpPressed;
    private bool _usePressed;

    public LocalPlayerController(IInputContext input)
    {
        _input = input;
        foreach (var mouse in input.Mice) mouse.MouseMove += OnMouseMove;
        foreach (var kb in input.Keyboards) kb.KeyDown += OnKeyDown;
    }

    public float Yaw { get; set; } = MathF.PI / 2f;
    public float Pitch { get; set; }
    public float Sensitivity { get; set; } = 0.0022f;
    public bool MouseLook { get; set; } = true;
    public bool Captured { get; private set; }

    private void OnMouseMove(IMouse mouse, Vector2 pos)
    {
        var delta = pos - _lastMouse;
        _lastMouse = pos;
        if (!Captured || !MouseLook) return;
        Yaw -= delta.X * Sensitivity;
        Pitch = Math.Clamp(Pitch - delta.Y * Sensitivity, -1.5f, 1.5f);
    }

    private void OnKeyDown(IKeyboard kb, Key key, int code)
    {
        if (key == Key.Space) _jumpPressed = true;
        if (key == Key.E) _usePressed = true;
        if (key == Key.Tab) SetCaptured(!Captured);
    }

    public void SetCaptured(bool captured)
    {
        Captured = captured;
        foreach (var mouse in _input.Mice) mouse.Cursor.CursorMode = captured ? CursorMode.Raw : CursorMode.Normal;
    }

    public PlayerInput Sample()
    {
        var kb = _input.Keyboards.Count > 0 ? _input.Keyboards[0] : null;
        var move = Vector2.Zero;
        var run = false;
        if (kb != null && Captured)
        {
            if (kb.IsKeyPressed(Key.W)) move.Y += 1;
            if (kb.IsKeyPressed(Key.S)) move.Y -= 1;
            if (kb.IsKeyPressed(Key.D)) move.X += 1;
            if (kb.IsKeyPressed(Key.A)) move.X -= 1;
            run = kb.IsKeyPressed(Key.ShiftLeft);
        }
        if (move.LengthSquared() > 1) move = Vector2.Normalize(move);
        var input = new PlayerInput
        {
            Move = move,
            Yaw = Yaw,
            Pitch = Pitch,
            Run = run,
            Jump = _jumpPressed && Captured,
            Use = _usePressed && Captured,
        };
        _jumpPressed = false;
        _usePressed = false;
        return input;
    }
}
