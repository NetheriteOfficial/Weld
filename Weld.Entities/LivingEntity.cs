using Weld.Entities.Net;

namespace Weld.Entities;

public abstract class LivingEntity : NetworkedEntity
{
    public float Health { get; protected set; } = 100f;
    public float MaxHealth { get; protected set; } = 100f;
    public bool IsDead => Health <= 0f;
    public int HurtTicks { get; private set; }

    public event Action<LivingEntity, float, BaseEntity?>? Damaged;
    public event Action<LivingEntity, BaseEntity?>? Died;

    public void Damage(float amount, BaseEntity? source = null)
    {
        if (!IsAuthority || IsDead || amount <= 0) return;
        Health = MathF.Max(0f, Health - amount);
        HurtTicks = 10;
        MarkDirty();
        OnDamaged(amount, source);
        Damaged?.Invoke(this, amount, source);
        if (IsDead)
        {
            OnDeath(source);
            Died?.Invoke(this, source);
        }
    }

    public void Heal(float amount)
    {
        if (!IsAuthority || IsDead) return;
        Health = MathF.Min(MaxHealth, Health + amount);
        MarkDirty();
    }

    public void Kill(BaseEntity? source = null) => Damage(Health + 1f, source);

    public void Respawn(float health)
    {
        Health = MathF.Min(health, MaxHealth);
        MarkDirty();
    }

    protected virtual void OnDamaged(float amount, BaseEntity? source) { }
    protected virtual void OnDeath(BaseEntity? source) { }

    public override void Tick()
    {
        base.Tick();
        if (HurtTicks > 0) HurtTicks--;
    }

    public override void WriteState(NetWriter w)
    {
        base.WriteState(w);
        w.Write(Health).Write((byte)Math.Min(HurtTicks, 255));
    }

    public override void ReadState(NetReader r, ulong serverTick)
    {
        base.ReadState(r, serverTick);
        Health = r.ReadFloat();
        HurtTicks = r.ReadByte();
    }
}
