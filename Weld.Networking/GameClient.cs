using Riptide;
using Riptide.Utils;
using Weld.Entities;
using Weld.Entities.Net;
using Weld.World;

namespace Weld.Networking;

public sealed class GameClient : IDisposable
{
    private readonly Client _client;
    private uint _playerNetId;
    private uint _inputSequence;
    private bool _disposed;

    public GameClient(GameWorld world, string playerName, IWorldLog? log = null)
    {
        World = world;
        PlayerName = playerName;
        Log = log ?? world.Log;
        _client = new Client();
        _client.Connected += OnConnected;
        _client.ConnectionFailed += OnConnectionFailed;
        _client.Disconnected += OnDisconnected;
        _client.MessageReceived += OnMessageReceived;
    }

    public GameWorld World { get; }
    public string PlayerName { get; }
    public IWorldLog Log { get; }
    public bool IsConnected => _client.IsConnected;
    public bool IsConnecting => _client.IsConnecting;
    public ushort ClientId => _client.Id;
    public ulong LastServerTick { get; private set; }
    public short RttMilliseconds => _client.RTT;
    public PlayerInput PendingInput;

    public event Action? Welcomed;
    public event Action<string>? ConnectionLost;

    public void Connect(string address)
    {
        RiptideLogger.Initialize(m => Log.Debug("[Riptide] " + m), m => Log.Info("[Riptide] " + m), m => Log.Warn("[Riptide] " + m), m => Log.Error("[Riptide] " + m), false);
        Log.Info($"Connecting to {address}");
        _client.Connect(address, 5, 0, null, false);
    }

    public void Disconnect()
    {
        if (_client.IsConnected || _client.IsConnecting) _client.Disconnect();
    }

    public void Update()
    {
        _client.Update();
        if (!_client.IsConnected) return;
        if (World.LocalPlayer == null && _playerNetId != 0 && World.Entities.FindNetworked(_playerNetId) is PlayerEntity p)
        {
            World.LocalPlayer = p;
            Log.Info($"Local player is {p.Name} (net {p.NetworkId})");
        }
        if (World.LocalPlayer == null) return;
        PendingInput.Sequence = ++_inputSequence;
        World.LocalPlayer.SetInput(PendingInput);
        var m = Message.Create(MessageSendMode.Unreliable, (ushort)NetMessageId.Input);
        m.AddBytes(Replication.EncodeInput(PendingInput));
        _client.Send(m);
        PendingInput.Jump = false;
        PendingInput.Use = false;
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        Log.Info($"Connected as client {_client.Id}");
        World.LocalClientId = _client.Id;
        var m = Message.Create(MessageSendMode.Reliable, (ushort)NetMessageId.ClientInfo);
        m.AddBytes(Replication.EncodeClientInfo(PlayerName));
        _client.Send(m);
    }

    private void OnConnectionFailed(object? sender, ConnectionFailedEventArgs e)
    {
        Log.Error($"Connection failed: {e.Reason}");
        ConnectionLost?.Invoke(e.Reason.ToString());
    }

    private void OnDisconnected(object? sender, DisconnectedEventArgs e)
    {
        Log.Warn($"Disconnected: {e.Reason}");
        World.LocalPlayer = null;
        _playerNetId = 0;
        foreach (var n in World.Entities.Networked.ToArray()) n.Remove();
        ConnectionLost?.Invoke(e.Reason.ToString());
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        var data = e.Message.GetBytes();
        switch ((NetMessageId)e.MessageId)
        {
            case NetMessageId.Welcome:
                var (clientId, tick, playerNetId) = Replication.DecodeWelcome(data);
                World.LocalClientId = clientId;
                _playerNetId = playerNetId;
                LastServerTick = tick;
                Welcomed?.Invoke();
                break;
            case NetMessageId.Spawn:
                Replication.DecodeSpawns(data, World);
                break;
            case NetMessageId.Despawn:
                Replication.DecodeDespawns(data, World);
                break;
            case NetMessageId.State:
                Replication.DecodeStates(data, World);
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }
}
