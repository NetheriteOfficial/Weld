using System.Numerics;
using Weld.World.Data;
using Weld.World.Streaming;

namespace Weld.World;

public sealed partial class Level
{
    private readonly List<(WorldInstance instance, int lodIndex, int fileStart)> _pendingExplicitLods = new();

    private void RecordExplicitLod(WorldInstance instance, XmlInstance xml, int fileStart)
    {
        if (xml.Lod >= 0) _pendingExplicitLods.Add((instance, xml.Lod, fileStart));
    }

    private void ResolveLodPairs()
    {
        var lods = _instances.Where(i => i.IsLod).ToList();
        if (lods.Count == 0)
        {
            Log.Info("No LOD-flagged definitions; LOD cross-fade inactive.");
            return;
        }

        foreach (var (inst, lodIndex, fileStart) in _pendingExplicitLods)
        {
            var target = fileStart + lodIndex;
            if (target < 0 || target >= _instances.Count || target == inst.Index)
            {
                Log.Warn($"{inst}: <Lod>{lodIndex}</Lod> is out of range for {inst.SourceFile}; ignored.");
                continue;
            }
            var lod = _instances[target];
            if (!lod.IsLod)
                Log.Warn($"{inst}: <Lod> points at '{lod.Definition.ModelName}' which is not LOD-flagged; linking anyway.");
            Link(inst, lod, LodPairingSource.Explicit);
        }
        _pendingExplicitLods.Clear();

        if (!Options.AutoLodPairing)
        {
            Summarize(lods);
            return;
        }

        var lodsByName = new Dictionary<string, List<WorldInstance>>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in lods)
        {
            if (!lodsByName.TryGetValue(l.Definition.ModelName, out var list)) lodsByName[l.Definition.ModelName] = list = new List<WorldInstance>();
            list.Add(l);
        }

        var radius2 = Options.LodPairingRadius * Options.LodPairingRadius;
        foreach (var inst in _instances)
        {
            if (inst.IsLod || inst.LodParent != null) continue;

            if (inst.Definition.LodModelName != null && lodsByName.TryGetValue(inst.Definition.LodModelName, out var explicitLods))
            {
                var best = Nearest(explicitLods, inst.Position, float.MaxValue);
                if (best != null) { Link(inst, best, LodPairingSource.Definition); continue; }
            }

            WorldInstance? byName = null;
            foreach (var candidateName in LodNameCandidates(inst.Definition.ModelName))
            {
                if (!lodsByName.TryGetValue(candidateName, out var list)) continue;
                byName = Nearest(list, inst.Position, radius2);
                if (byName != null) break;
            }
            if (byName != null) { Link(inst, byName, LodPairingSource.NameConvention); continue; }

            if (Options.LodSpatialFallback)
            {
                WorldInstance? spatial = null;
                var bestD = float.MaxValue;
                foreach (var l in lods)
                {
                    if (!l.WorldBounds.Contains(inst.Position)) continue;
                    var d = Vector3.DistanceSquared(l.Position, inst.Position);
                    if (d < bestD) { bestD = d; spatial = l; }
                }
                if (spatial != null) Link(inst, spatial, LodPairingSource.Spatial);
            }
        }

        Summarize(lods);
    }

    private static void Link(WorldInstance hd, WorldInstance lod, LodPairingSource source)
    {
        hd.LodParent?._lodChildren.Remove(hd);
        hd.LodParent = lod;
        hd.LodSource = source;
        if (!lod._lodChildren.Contains(hd)) lod._lodChildren.Add(hd);
    }

    private static WorldInstance? Nearest(List<WorldInstance> candidates, Vector3 p, float maxDistanceSquared)
    {
        WorldInstance? best = null;
        var bestD = maxDistanceSquared;
        foreach (var c in candidates)
        {
            var d = Vector3.DistanceSquared(c.Position, p);
            if (d <= bestD) { bestD = d; best = c; }
        }
        return best;
    }

    public static IEnumerable<string> LodNameCandidates(string modelName)
    {
        yield return "lod" + modelName;
        yield return "lod_" + modelName;
        yield return modelName + "_lod";
        yield return modelName + "lod";
        if (modelName.Length > 3) yield return "lod" + modelName[3..];
    }

    private void Summarize(List<WorldInstance> lods)
    {
        var linked = _instances.Count(i => i.LodParent != null);
        var orphans = lods.Count(l => l._lodChildren.Count == 0);
        Log.Info($"LOD: {lods.Count} LOD instance(s), {linked} HD instance(s) linked " +
                 $"(explicit {_instances.Count(i => i.LodSource == LodPairingSource.Explicit)}, " +
                 $"ide {_instances.Count(i => i.LodSource == LodPairingSource.Definition)}, " +
                 $"name {_instances.Count(i => i.LodSource == LodPairingSource.NameConvention)}, " +
                 $"spatial {_instances.Count(i => i.LodSource == LodPairingSource.Spatial)}); " +
                 $"{orphans} LOD(s) without children behave as plain far objects.");
    }
}
