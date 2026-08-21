using System.Collections.Generic;
using Friflo.Engine.ECS;
using NUnit.Framework;
using Tests.ECS.Relations;
using static NUnit.Framework.Assert;

// ReSharper disable InconsistentNaming
namespace Internal.ECS {

public static class Test_ClearRelations
{
    private static long IsLinkedOf(EntityStore store, int id) => store.nodes[id].isLinked;

    private static List<long> ClearBy(System.Action<Entity, List<Entity>> clear)
    {
        var store   = new EntityStore();
        var source  = store.CreateEntity(1);
        var targets = new List<Entity>();
        for (int n = 2; n <= 4; n++) {
            var target = store.CreateEntity(n);
            targets.Add(target);
            source.AddRelation(new AttackRelation { target = target });
        }
        clear(source, targets);

        var state = new List<long> { store.nodes[1].isOwner, source.GetRelations<AttackRelation>().Length };
        foreach (var target in targets) {
            state.Add(IsLinkedOf(store, target.Id));
            state.Add(target.GetIncomingLinks<AttackRelation>().Count);
        }
        return state;
    }

    [Test]
    public static void Test_ClearRelations_matches_RemoveRelation()
    {
        var byClear  = ClearBy((source, _)       => AreEqual(3, source.ClearRelations<AttackRelation>()));
        var byRemove = ClearBy((source, targets) => {
            foreach (var target in targets) {
                IsTrue(source.RemoveRelation<AttackRelation>(target));
            }
        });
        AreEqual(byRemove, byClear);
        foreach (var value in byClear) {
            AreEqual(0, value);
        }
    }

    [Test]
    public static void Test_ClearRelations_clears_isOwner()
    {
        var store  = new EntityStore();
        var entity = store.CreateEntity(1);
        entity.AddRelation(new InventoryItem { type = InventoryItemType.Axe });
        entity.AddRelation(new InventoryItem { type = InventoryItemType.Gun });
        AreNotEqual(0, store.nodes[1].isOwner);

        AreEqual(2, entity.ClearRelations<InventoryItem>());

        AreEqual(0, store.nodes[1].isOwner);
        AreEqual(0, entity.GetRelations<InventoryItem>().Length);
    }
}

}
