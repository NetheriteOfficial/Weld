using Weld.Entities;

namespace Weld.Entities.Net;

public enum NetMessageId : ushort
{
    Welcome = 1,
    Spawn = 2,
    Despawn = 3,
    State = 4,
    Input = 5,
    ClientInfo = 6,
}

public static class Replication
{
    public const int DefaultChunkBytes = 1000;

    public static byte[] EncodeWelcome(ushort clientId, ulong serverTick, uint playerNetId)
    {
        var w = new NetWriter(16);
        w.Write(clientId).Write(serverTick).Write(playerNetId);
        return w.ToArray();
    }

    public static (ushort ClientId, ulong ServerTick, uint PlayerNetId) DecodeWelcome(byte[] data)
    {
        var r = new NetReader(data);
        return (r.ReadUShort(), r.ReadULong(), r.ReadUInt());
    }

    public static List<byte[]> EncodeSpawns(IEnumerable<NetworkedEntity> entities, EntityTypeRegistry types, int chunkBytes = DefaultChunkBytes)
    {
        var chunks = new List<byte[]>();
        var w = new NetWriter(chunkBytes + 64);
        var payload = new NetWriter(256);
        var count = 0;
        w.Write((byte)0);
        foreach (var e in entities)
        {
            if (!types.TryGetId(e.GetType(), out var typeId)) continue;
            payload.Reset();
            e.WriteSpawn(payload);
            if (count > 0 && w.Length + payload.Length + 10 > chunkBytes)
            {
                chunks.Add(Finish(w, count));
                w.Reset();
                w.Write((byte)0);
                count = 0;
            }
            w.Write(typeId).Write(e.NetworkId).Write(e.OwnerClientId).WriteBytes(payload.Span);
            count++;
            if (count == 255)
            {
                chunks.Add(Finish(w, count));
                w.Reset();
                w.Write((byte)0);
                count = 0;
            }
        }
        if (count > 0) chunks.Add(Finish(w, count));
        return chunks;
    }

    private static byte[] Finish(NetWriter w, int count)
    {
        var bytes = w.ToArray();
        bytes[0] = (byte)count;
        return bytes;
    }

    public static List<NetworkedEntity> DecodeSpawns(byte[] data, GameWorld world)
    {
        var r = new NetReader(data);
        var count = r.ReadByte();
        var spawned = new List<NetworkedEntity>(count);
        for (var i = 0; i < count; i++)
        {
            var typeId = r.ReadUShort();
            var netId = r.ReadUInt();
            var owner = r.ReadUShort();
            var payload = r.ReadBytes();
            if (world.Entities.FindNetworked(netId) != null) continue;
            var entity = world.Types.Create(typeId) as NetworkedEntity;
            if (entity == null) continue;
            entity.ReadSpawn(new NetReader(payload));
            world.Entities.Add(entity, netId, owner);
            spawned.Add(entity);
        }
        return spawned;
    }

    public static byte[] EncodeDespawns(IReadOnlyList<uint> netIds)
    {
        var w = new NetWriter(4 + netIds.Count * 4);
        w.Write((ushort)netIds.Count);
        foreach (var id in netIds) w.Write(id);
        return w.ToArray();
    }

    public static int DecodeDespawns(byte[] data, GameWorld world)
    {
        var r = new NetReader(data);
        var count = r.ReadUShort();
        var removed = 0;
        for (var i = 0; i < count; i++)
        {
            var e = world.Entities.FindNetworked(r.ReadUInt());
            if (e == null) continue;
            e.Remove();
            removed++;
        }
        return removed;
    }

    public static List<byte[]> EncodeStates(ulong serverTick, IEnumerable<NetworkedEntity> entities, int chunkBytes = DefaultChunkBytes)
    {
        var chunks = new List<byte[]>();
        var w = new NetWriter(chunkBytes + 64);
        var payload = new NetWriter(128);
        var count = 0;
        w.Write(serverTick).Write((byte)0);
        foreach (var e in entities)
        {
            payload.Reset();
            e.WriteState(payload);
            if (count > 0 && w.Length + payload.Length + 8 > chunkBytes)
            {
                chunks.Add(FinishState(w, count));
                w.Reset();
                w.Write(serverTick).Write((byte)0);
                count = 0;
            }
            w.Write(e.NetworkId).WriteBytes(payload.Span);
            count++;
            if (count == 255)
            {
                chunks.Add(FinishState(w, count));
                w.Reset();
                w.Write(serverTick).Write((byte)0);
                count = 0;
            }
        }
        if (count > 0) chunks.Add(FinishState(w, count));
        return chunks;
    }

    private static byte[] FinishState(NetWriter w, int count)
    {
        var bytes = w.ToArray();
        bytes[8] = (byte)count;
        return bytes;
    }

    public static int DecodeStates(byte[] data, GameWorld world)
    {
        var r = new NetReader(data);
        var tick = r.ReadULong();
        var count = r.ReadByte();
        var applied = 0;
        for (var i = 0; i < count; i++)
        {
            var netId = r.ReadUInt();
            var payload = r.ReadBytes();
            var e = world.Entities.FindNetworked(netId);
            if (e == null) continue;
            e.ReadState(new NetReader(payload), tick);
            applied++;
        }
        return applied;
    }

    public static byte[] EncodeInput(in PlayerInput input)
    {
        var w = new NetWriter(32);
        input.Write(w);
        return w.ToArray();
    }

    public static PlayerInput DecodeInput(byte[] data) => PlayerInput.Read(new NetReader(data));

    public static byte[] EncodeClientInfo(string name)
    {
        var w = new NetWriter(64);
        w.Write(name);
        return w.ToArray();
    }

    public static string DecodeClientInfo(byte[] data) => new NetReader(data).ReadString();

    public static IEnumerable<NetworkedEntity> DueForReplication(GameWorld world)
    {
        var tick = world.Entities.Tick;
        foreach (var e in world.Entities.Networked)
        {
            if (!e.ReplicateState || !e.Dirty) continue;
            if (tick - e.LastReplicatedTick < (ulong)Math.Max(1, e.ReplicationIntervalTicks) && e.LastReplicatedTick != 0) continue;
            yield return e;
        }
    }
}
