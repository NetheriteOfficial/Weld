using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Weld.Entities;
using Weld.Networking;
using Weld.World;

namespace Weld.Carisle.Game;

public sealed class ClientApp
{
    private readonly AppOptions _options;
    private readonly ConsoleWorldLog _log = new() { Minimum = WorldLogLevel.Info };
    private IWindow _window = null!;
    private GL _gl = null!;
    private IInputContext _input = null!;
    private Level _level = null!;
    private GameWorld _world = null!;
    private RenderPipeline _pipeline = null!;
    private LocalPlayerController _controller = null!;
    private PlayerCamera _camera = null!;
    private GameServer? _server;
    private GameClient? _client;
    private double _statsTimer;
    private bool _harsh;

    public ClientApp(AppOptions options)
    {
        _options = options;
        _harsh = options.Ps2Streaming;
    }

    public int Run()
    {
        var windowOptions = WindowOptions.Default with
        {
            Title = "Weld.Carisle — Carrington Island",
            Size = new Vector2D<int>((int)RenderPipeline.InternalWidth * 2, (int)RenderPipeline.InternalHeight * 2),
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 5)),
            VSync = true,
        };
        _window = Window.Create(windowOptions);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Closing += OnClosing;
        _window.Run();
        _window.Dispose();
        return 0;
    }

    private void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _input = _window.CreateInput();
        _level = GameBootstrap.LoadLevel(_options, _log);
        _world = GameBootstrap.CreateWorld(_level, _options.Role, _log);
        _pipeline = new RenderPipeline(_gl, _level) { Filter = UpscaleFilter.SharpBilinear, PreserveAspectRatio = true };
        _controller = new LocalPlayerController(_input);
        _camera = new PlayerCamera();

        if (_options.Role == NetRole.Host)
        {
            _server = new GameServer(_world, _log);
            _server.Start(_options.Port, _options.MaxClients);
            GameBootstrap.SpawnDemoProps(_world);
            _world.LocalPlayer = _world.SpawnPlayer(0, _options.PlayerName);
            _world.Entities.TickEnded += _ => _server.Update();
        }
        else
        {
            _client = new GameClient(_world, _options.PlayerName, _log);
            _client.Connect(_options.Address);
            _world.Entities.TickBegan += _ => _client.Update();
        }

        _world.Entities.TickBegan += _ =>
        {
            var input = _controller.Sample();
            if (_client != null)
            {
                var pending = _client.PendingInput;
                pending.Move = input.Move; pending.Yaw = input.Yaw; pending.Pitch = input.Pitch; pending.Run = input.Run;
                pending.Jump |= input.Jump; pending.Use |= input.Use;
                _client.PendingInput = pending;
            }
            else
            {
                _world.LocalPlayer?.SetInput(input);
            }
            if (input.Use && _world.IsServer && _world.LocalPlayer != null) ThrowProp(_world.LocalPlayer);
        };

        foreach (var kb in _input.Keyboards)
            kb.KeyDown += (_, key, _) =>
            {
                switch (key)
                {
                    case Key.Escape: _window.Close(); break;
                    case Key.F1: _pipeline.Wireframe = !_pipeline.Wireframe; break;
                    case Key.F2: _pipeline.Filter = (UpscaleFilter)(((int)_pipeline.Filter + 1) % 3); break;
                    case Key.F3: _pipeline.PreserveAspectRatio = !_pipeline.PreserveAspectRatio; break;
                    case Key.F4: _level.Options.FrustumCulling = !_level.Options.FrustumCulling; break;
                    case Key.F5:
                        _harsh = !_harsh;
                        _level.Options.CopyFrom(_harsh ? LevelOptions.Ps2Harsh() : LevelOptions.Modern());
                        break;
                    case Key.F6: _pipeline.DitherFades = !_pipeline.DitherFades; break;
                    case Key.F7: _camera.ThirdPersonDistance = _camera.ThirdPersonDistance > 0 ? 0 : 4f; _world.RenderLocalPlayer = _camera.ThirdPersonDistance > 0; break;
                    case Key.R: if (_world.IsServer && _world.LocalPlayer != null) _world.LocalPlayer.Teleport(_world.FindSpawnPoint()); break;
                }
            };

        _controller.SetCaptured(true);
        _log.Info("Controls: WASD move, mouse look, Space jump, Shift run, E throw prop (host), R respawn (host), Tab mouse capture, F1-F7 render/stream toggles, Esc quit");
    }

    private void ThrowProp(PlayerEntity player)
    {
        var prop = new PropEntity { Size = new Vector3(0.5f), Mass = 8f, Color = new Vector4(0.9f, 0.2f, 0.2f, 1f) };
        _world.Spawn(prop, player.EyePosition + player.Forward * 1.2f);
        prop.ApplyImpulse(player.Forward * 120f);
    }

    private void OnUpdate(double dt)
    {
        _world.Advance(dt);
    }

    private void OnRender(double dt)
    {
        var alpha = _world.Entities.Alpha;
        _camera.Update(_world.LocalPlayer, _controller.Yaw, _controller.Pitch, alpha, _world);
        var cam = _camera.ToStreamingCamera(RenderPipeline.InternalAspect);
        _level.Update(cam);
        var size = _window.FramebufferSize;
        _pipeline.RenderFrame(cam, size.X, size.Y, _world, alpha);

        _statsTimer += dt;
        if (_statsTimer >= 1.0)
        {
            _statsTimer = 0;
            var net = _server != null ? $"host {_server.ClientCount} client(s)" : _client != null ? (_client.IsConnected ? $"client rtt {_client.RttMilliseconds}ms" : "connecting…") : "offline";
            var surf = _world.LocalPlayer?.GroundSurface.ToString() ?? "-";
            _window.Title = $"Weld.Carisle — {net} | tick {_world.Entities.Tick} ents {_world.Entities.Count} on {surf} | {_level.Stats} | {_pipeline.DrawCallsLastFrame} draws | {1.0 / Math.Max(dt, 1e-6):F0} fps";
        }
    }

    private void OnClosing()
    {
        _client?.Dispose();
        _server?.Dispose();
        _pipeline.Dispose();
        _world.Dispose();
        _level.Dispose();
        _input.Dispose();
        _gl.Dispose();
    }
}
