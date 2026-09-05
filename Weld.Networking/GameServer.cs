using Riptide;
using Riptide.Utils;
using Weld.Entities;
using Weld.Entities.Net;
using Weld.World;

namespace Weld.Networking;

public sealed class GameServer : IDisposable
{
    private readonly Server _server;
    private readonly Dictionary<ushort, PlayerEntity> _players = new();
    private readonly Dictionary<ushort, string> _names = new();
    private readonly List<NetworkedEntity> _pendingSpawns = new();
    private readonly List<uint> _pendingDespawns = new();
    private readonly List<NetworkedEntity> _due = new();
    private bool _disposed;

    public GameServer(GameWorld world, IWorldLog? log = null)
    {
        World = world;
        Log = log ?? world.Log;
        _server = new Server();
        _server.ClientConnected += OnClientConnected;
        _server.ClientDisconnected += OnClientDisconnected;
        _server.MessageReceived += OnMessageReceived;
        world.Entities.Spawned += OnEntitySpawned;
        world.Entities.Removed += OnEntityRemoved;
    }

    public GameWorld World { get; }
    public IWorldLog Log { get; }
    public bool IsRunning => _server.IsRunning;
    public int ClientCount => _server.ClientCount;
    public ushort Port { get; private set; }
    public IReadOnlyDictionary<ushort, PlayerEntity> Players => _players;
    public int MessagesSentThisTick { get; private set; }

    public event Action<ushort, PlayerEntity>? PlayerJoined;
    public event Action<ushort, PlayerEntity>? PlayerLeft;

    public void Start(ushort port, ushort maxClients = 16)
    {
        RiptideLogger.Initialize(m => Log.Debug("[Riptide] " + m), m => Log.Info("[Riptide] " + m), m => Log.Warn("[Riptide] " + m), m => Log.Error("[Riptide] " + m), false);
        Port = port;
        _server.Start(port, maxClients, 0, false);
        Log.Info($"Server listening on UDP {port} (max {maxClients})");
    }

    public void Stop()
    {
        if (_server.IsRunning) _server.Stop();
    }

    public void Update()
    {
        MessagesSentThisTick = 0;
        _server.Update();
        FlushSpawns();
        FlushDespawns();
        ReplicateStates();
    }

    private void OnClientConnected(object? sender, ServerConnectedEventArgs e)
    {
        var id = e.Client.Id;
        Log.Info($"Client {id} connected from {e.Client}");
        foreach (var chunk in Replication.EncodeSpawns(World.Entities.Networked, World.Types))
            Send(MessageSendMode.Reliable, NetMessageId.Spawn, chunk, id);
        var player = World.SpawnPlayer(id, _names.GetValueOrDefault(id) ?? $"Player{id}");
        _players[id] = player;
        Send(MessageSendMode.Reliable, NetMessageId.Welcome, Replication.EncodeWelcome(id, World.Entities.Tick, player.NetworkId), id);
        PlayerJoined?.Invoke(id, player);
    }

    private void OnClientDisconnected(object? sender, ServerDisconnectedEventArgs e)
    {
        var id = e.Client.Id;
        Log.Info($"Client {id} disconnected ({e.Reason})");
        if (_players.Remove(id, out var player))
        {
            player.Remove();
            PlayerLeft?.Invoke(id, player);
        }
        _names.Remove(id);
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        var from = e.FromConnection.Id;
        switch ((NetMessageId)e.MessageId)
        {
            case NetMessageId.Input:
                if (_players.TryGetValue(from, out var player))
                    player.SetInput(Replication.DecodeInput(e.Message.GetBytes()));
                break;
            case NetMessageId.ClientInfo:
                var name = Replication.DecodeClientInfo(e.Message.GetBytes());
                _names[from] = name;
                if (_players.TryGetValue(from, out var p)) p.Name = name;
                break;
        }
    }

    private void OnEntitySpawned(BaseEntity entity)
    {
        if (entity is NetworkedEntity n) _pendingSpawns.Add(n);
    }

    private void OnEntityRemoved(BaseEntity entity)
    {
        if (entity is NetworkedEntity n)
        {
            _pendingSpawns.Remove(n);
            _pendingDespawns.Add(n.NetworkId);
        }
    }

    private void FlushSpawns()
    {
        if (_pendingSpawns.Count == 0 || _server.ClientCount == 0) { _pendingSpawns.Clear(); return; }
        foreach (var chunk in Replication.EncodeSpawns(_pendingSpawns, World.Types))
            Broadcast(MessageSendMode.Reliable, NetMessageId.Spawn, chunk);
        _pendingSpawns.Clear();
    }

    private void FlushDespawns()
    {
        if (_pendingDespawns.Count == 0) return;
        if (_server.ClientCount > 0)
            Broadcast(MessageSendMode.Reliable, NetMessageId.Despawn, Replication.EncodeDespawns(_pendingDespawns));
        _pendingDespawns.Clear();
    }

    private void ReplicateStates()
    {
        if (_server.ClientCount == 0) return;
        _due.Clear();
        _due.AddRange(Replication.DueForReplication(World));
        if (_due.Count == 0) return;
        var tick = World.Entities.Tick;
        foreach (var chunk in Replication.EncodeStates(tick, _due))
            Broadcast(MessageSendMode.Unreliable, NetMessageId.State, chunk);
        foreach (var e in _due)
        {
            e.LastReplicatedTick = tick;
            e.ClearDirty();
        }
    }

    private void Send(MessageSendMode mode, NetMessageId id, byte[] payload, ushort to)
    {
        var m = Message.Create(mode, (ushort)id);
        m.AddBytes(payload);
        _server.Send(m, to);
        MessagesSentThisTick++;
    }

    private void Broadcast(MessageSendMode mode, NetMessageId id, byte[] payload)
    {
        var m = Message.Create(mode, (ushort)id);
        m.AddBytes(payload);
        _server.SendToAll(m);
        MessagesSentThisTick++;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        World.Entities.Spawned -= OnEntitySpawned;
        World.Entities.Removed -= OnEntityRemoved;
        Stop();
    }
}
