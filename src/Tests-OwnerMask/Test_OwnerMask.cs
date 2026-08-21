using System;
using System.Collections.Generic;
using System.Linq;
using Friflo.Engine.ECS;
using NUnit.Framework;
using static NUnit.Framework.Assert;

// ReSharper disable InconsistentNaming
// ReSharper disable once CheckNamespace
namespace Tests.OwnerMask {

/// <summary>
/// Indexed component and relation types share a single-word bit mask on <c>EntityNode.isOwner</c> /
/// <c>isLinked</c>. This assembly declares 63 of them specifically to cross the width of that mask.
/// </summary>
public static class Test_OwnerMask
{
    /// <see cref="ComponentType.RelationType"/> is internal, so identify relations by their key type.
    private static bool SpendsOwnerSlot(ComponentType type) =>
        type != null && (type.IndexType != null || type.RelationKeyType != null);

    private static List<ComponentType> SlotTypes()
    {
        _ = new EntityStore(); // building a store is what registers the schema
        return EntityStore.GetEntitySchema().Components.ToArray().Where(SpendsOwnerSlot).ToList();
    }

    /// Slot assignment follows assembly scan order, so never name the type expected on a given slot.
    private static ISlotProbe ProbeAtOrPastSlot(int structIndex, params SlotKind[] kinds)
    {
        foreach (var type in SlotTypes()) {
            if (type.StructIndex < structIndex)                     continue;
            if (!SlotProbes.TryGet(type.Type, out var probe))        continue;
            if (Array.IndexOf(kinds, probe.Kind) == -1)              continue;
            return probe;
        }
        Ignore($"no {string.Join("/", kinds)} type landed at or past StructIndex {structIndex}");
        return null;
    }

    private static ISlotProbe ProbeAtSlot(int structIndex)
    {
        var type = SlotTypes().FirstOrDefault(t => t.StructIndex == structIndex);
        if (type == null) {
            Ignore($"no indexed/relation type landed on StructIndex {structIndex}");
        }
        return SlotProbes.Get(type!.Type);
    }

    [Test]
    public static void Schema_HasEnoughSlotTypes_ToCrossTheMask()
    {
        var types = SlotTypes();
        var max   = types.Max(t => t.StructIndex);
        IsTrue(max > 32, $"repro needs indexed/relation types past StructIndex 32, highest was {max}");
    }

    [Test]
    public static void EverySlotType_RowRemoved_WhenEntityDeleted()
    {
        var stranded = new List<string>();
        foreach (var type in SlotTypes()) {
            if (!SlotProbes.TryGet(type.Type, out var probe)) {
                continue; // engine-owned type such as UniqueEntity
            }
            var store  = new EntityStore();
            var entity = store.CreateEntity();
            var target = store.CreateEntity();
            probe.Add(entity, target, 42);
            if (probe.RowCount(store) != 1) {
                stranded.Add($"{type.Type.Name}@{type.StructIndex} (row never recorded)");
                continue;
            }
            entity.DeleteEntity();
            if (probe.RowCount(store) != 0) {
                stranded.Add($"{type.Type.Name}@{type.StructIndex}");
            }
        }
        IsEmpty(stranded, $"rows survived their deleted entity: {string.Join(", ", stranded)}");
    }

    [Test]
    public static void IncomingLinks_Removed_WhenTargetDeleted()
    {
        var probe  = ProbeAtOrPastSlot(32, SlotKind.LinkComponent, SlotKind.LinkRelation);
        var store  = new EntityStore();
        var source = store.CreateEntity();
        var target = store.CreateEntity();
        probe.Add(source, target, 0);
        IsTrue(probe.IsAttached(source));

        target.DeleteEntity();
        IsFalse(probe.IsAttached(source), $"{probe.Type.Name} still links the deleted target entity");
    }

    // Mirrors MaxOwnerSlot - the highest slot EntityNode.isOwner can hold.
    private const int MaxOwnerSlot = 63;

    [Test]
    public static void Delete_Succeeds_ForSlot31Owner()
    {
        var probe  = ProbeAtSlot(31);
        var store  = new EntityStore();
        var entity = store.CreateEntity();
        var target = store.CreateEntity();
        probe.Add(entity, target, 42);

        entity.DeleteEntity(); // 31 is the sign bit of a 32-bit mask
        AreEqual(0, probe.RowCount(store));
    }

    [Test]
    public static void Delete_Succeeds_ForSlot63Owner()
    {
        var probe  = ProbeAtSlot(MaxOwnerSlot);
        var store  = new EntityStore();
        var entity = store.CreateEntity();
        var target = store.CreateEntity();
        probe.Add(entity, target, 42);

        entity.DeleteEntity(); // 63 is the sign bit of the widened 64-bit mask
        AreEqual(0, probe.RowCount(store));
    }

    [Test]
    public static void Schema_ReachesTheHighestOwnerSlot()
    {
        var max = SlotTypes().Max(t => t.StructIndex);
        AreEqual(MaxOwnerSlot, max,
            "repro must land a type on the highest owner slot, else the sign-bit path is never executed");
    }

    [Test]
    public static void RecycledId_DoesNotInheritStaleRow()
    {
        var probe = ProbeAtOrPastSlot(32, SlotKind.Indexed);
        var store = new EntityStore();
        IsTrue(store.RecycleIds, "test assumes id recycling, which is the default");

        var doomed = store.CreateEntity();
        int id     = doomed.Id;
        probe.Add(doomed, default, 42);
        doomed.DeleteEntity();

        var reused = store.CreateEntity();
        AreEqual(id, reused.Id, "expected the deleted id to be recycled");
        AreEqual(0, probe.RowCount(store),
            $"{probe.Type.Name} row outlived its entity and now resolves to live entity {reused.Id}");
    }

    [Test]
    public static void CopyEntity_ReindexesIndexedComponent_PastSlot31()
    {
        var probe  = ProbeAtOrPastSlot(31, SlotKind.Indexed);
        var store  = new EntityStore();
        var source = store.CreateEntity();
        var target = store.CreateEntity();
        probe.Add(source, default, 100);
        probe.Add(target, default, 200);
        AreEqual(2, probe.RowCount(store));

        source.CopyEntity(target); // target's value becomes 100, so 200 must leave the index
        AreEqual(1, probe.RowCount(store), $"{probe.Type.Name} kept the target's stale indexed value");
    }
}

}
