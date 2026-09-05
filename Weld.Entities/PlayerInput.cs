using System.Numerics;
using Weld.Entities.Net;

namespace Weld.Entities;

public struct PlayerInput
{
    public uint Sequence;
    public Vector2 Move;
    public float Yaw;
    public float Pitch;
    public bool Run;
    public bool Jump;
    public bool Use;

    public bool HasMovement => Move.LengthSquared() > 1e-6f || Jump;

    public void Write(NetWriter w)
    {
        w.Write(Sequence).Write(Move).Write(Yaw).Write(Pitch);
        byte flags = 0;
        if (Run) flags |= 1;
        if (Jump) flags |= 2;
        if (Use) flags |= 4;
        w.Write(flags);
    }

    public static PlayerInput Read(NetReader r)
    {
        var input = new PlayerInput
        {
            Sequence = r.ReadUInt(),
            Move = r.ReadVector2(),
            Yaw = r.ReadFloat(),
            Pitch = r.ReadFloat(),
        };
        var flags = r.ReadByte();
        input.Run = (flags & 1) != 0;
        input.Jump = (flags & 2) != 0;
        input.Use = (flags & 4) != 0;
        return input;
    }
}
