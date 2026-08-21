using Friflo.Engine.ECS;
using NUnit.Framework;
using static NUnit.Framework.Assert;

// ReSharper disable InconsistentNaming
namespace Internal.ECS {

public static class Test_ChildIdStorage
{
    /// Child ids are keyed by entity id outside the archetype, so deleting an entity does not free them
    /// implicitly. Unless the slot is cleared, pool arrays leak and a recycled id inherits old children.
    [Test]
    public static void Test_ChildIdStorage_freed_on_delete()
    {
        var store   = new EntityStore(PidType.UsePidAsId);
        var heap    = store.extension.childHeap;
        var baseline = heap.Count;

        var parent  = store.CreateEntity();
        for (int n = 0; n < 20; n++) {
            parent.AddChild(store.CreateEntity());
        }
        AreEqual(20, parent.ChildCount);
        AreNotEqual(baseline, heap.Count);

        parent.DeleteEntity();
        AreEqual(baseline, heap.Count);
    }

    [Test]
    public static void Test_ChildIdStorage_recycled_id_has_no_children()
    {
        var store   = new EntityStore(PidType.RandomPids);
        store.RecycleIds = true;

        var parent  = store.CreateEntity();
        int id      = parent.Id;
        parent.AddChild(store.CreateEntity());
        parent.AddChild(store.CreateEntity());
        AreEqual(2, parent.ChildCount);

        parent.DeleteEntity();

        var reused = store.CreateEntity();
        AreEqual(id, reused.Id);
        AreEqual(0, reused.ChildCount);
        AreEqual(0, reused.ChildIds.Length);
    }

    [Test]
    public static void Test_ChildIdStorage_AddChild_is_no_structural_change()
    {
        var store   = new EntityStore(PidType.UsePidAsId);
        var parent  = store.CreateEntity();
        var before  = parent.Archetype;

        parent.AddChild(store.CreateEntity());

        AreSame(before, parent.Archetype);
        AreEqual(0, parent.Archetype.ComponentCount);
    }
}

}
