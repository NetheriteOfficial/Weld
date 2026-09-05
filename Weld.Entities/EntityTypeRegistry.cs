namespace Weld.Entities;

public sealed class EntityTypeRegistry
{
    private readonly Dictionary<ushort, Entry> _byId = new();
    private readonly Dictionary<Type, Entry> _byType = new();
    private readonly Dictionary<string, Entry> _byName = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct Entry(ushort Id, string Name, Type Type, Func<BaseEntity> Factory);

    public EntityTypeRegistry Register<T>(ushort id, string name, Func<T> factory) where T : BaseEntity
    {
        if (_byId.ContainsKey(id)) throw new InvalidOperationException($"entity type id {id} already registered");
        var e = new Entry(id, name, typeof(T), () => factory());
        _byId[id] = e;
        _byType[typeof(T)] = e;
        _byName[name] = e;
        return this;
    }

    public static EntityTypeRegistry CreateDefault() => new EntityTypeRegistry()
        .Register(1, "player", () => new PlayerEntity())
        .Register(2, "prop", () => new PropEntity());

    public BaseEntity Create(ushort id) => _byId.TryGetValue(id, out var e) ? e.Factory() : throw new KeyNotFoundException($"unknown entity type {id}");
    public BaseEntity Create(string name) => _byName.TryGetValue(name, out var e) ? e.Factory() : throw new KeyNotFoundException($"unknown entity type '{name}'");
    public bool TryGetId(Type type, out ushort id)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            if (_byType.TryGetValue(t, out var e)) { id = e.Id; return true; }
        }
        id = 0;
        return false;
    }
    public ushort IdOf(BaseEntity entity) => TryGetId(entity.GetType(), out var id) ? id : throw new KeyNotFoundException($"entity type {entity.GetType().Name} is not registered");
    public string NameOf(BaseEntity entity) => TryGetId(entity.GetType(), out var id) ? _byId[id].Name : entity.GetType().Name;
    public IEnumerable<(ushort Id, string Name, Type Type)> All => _byId.Values.Select(e => (e.Id, e.Name, e.Type));
}
