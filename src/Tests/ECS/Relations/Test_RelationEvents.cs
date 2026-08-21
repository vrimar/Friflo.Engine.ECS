using System;
using System.Collections.Generic;
using System.IO;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Serialize;
using NUnit.Framework;
using static NUnit.Framework.Assert;

// ReSharper disable InconsistentNaming
namespace Tests.ECS.Relations {

public static class Test_RelationEvents
{
    private class Recorder
    {
        internal readonly List<RelationChanged>  Relations   = new List<RelationChanged>();
        internal readonly List<ComponentChanged> Components  = new List<ComponentChanged>();

        internal Recorder(EntityStore store)
        {
            store.OnRelationChanged     += ev => Relations.Add(ev);
            store.OnComponentAdded      += ev => Components.Add(ev);
            store.OnComponentRemoved    += ev => Components.Add(ev);
        }
    }

    private static Recorder Record(EntityStore store) => new Recorder(store);

    private static void AssertSingle(Recorder rec, Entity entity, RelationChangedAction action, Type type)
    {
        AreEqual(1, rec.Relations.Count);
        var ev = rec.Relations[0];
        AreEqual(action,      ev.Action);
        AreEqual(entity.Id,   ev.EntityId);
        AreEqual(entity,      ev.Entity);
        AreEqual(type,        ev.Type);
        AreSame (store_of(entity), ev.Store);
        IsEmpty (rec.Components);
    }

    private static EntityStore store_of(Entity entity) => entity.Store;

    [Test]
    public static void AddRelation_NewKey_FiresAdd()
    {
        var store  = new EntityStore();
        var entity = store.CreateEntity();
        var rec    = Record(store);

        IsTrue(entity.AddRelation(new IntRelation { value = 1 }));

        AssertSingle(rec, entity, RelationChangedAction.Add, typeof(IntRelation));
        AreEqual(1, rec.Relations[0].Key<IntRelation, int>());
    }

    [Test]
    public static void AddRelation_ExistingKey_FiresUpdate()
    {
        var store  = new EntityStore();
        var entity = store.CreateEntity();
        entity.AddRelation(new IntRelation { value = 1 });
        var rec    = Record(store);

        IsFalse(entity.AddRelation(new IntRelation { value = 1 }));

        AssertSingle(rec, entity, RelationChangedAction.Update, typeof(IntRelation));
        AreEqual(1, rec.Relations[0].Key<IntRelation, int>());
        AreEqual(1, entity.GetRelations<IntRelation>().Length);
    }

    [Test]
    public static void RemoveRelation_PresentKey_FiresRemove()
    {
        var store  = new EntityStore();
        var entity = store.CreateEntity();
        entity.AddRelation(new IntRelation { value = 1 });
        var rec    = Record(store);

        IsTrue(entity.RemoveRelation<IntRelation, int>(1));

        AssertSingle(rec, entity, RelationChangedAction.Remove, typeof(IntRelation));
        AreEqual(0, entity.GetRelations<IntRelation>().Length);
    }

    [Test]
    public static void RemoveRelation_PresentKey_KeyIsRemovedOne()
    {
        var store  = new EntityStore();
        var entity = store.CreateEntity();
        entity.AddRelation(new IntRelation { value = 1 });
        entity.AddRelation(new IntRelation { value = 2 });
        entity.AddRelation(new IntRelation { value = 3 });
        var keys   = new List<int>();
        store.OnRelationChanged += ev => keys.Add(ev.Key<IntRelation, int>());

        IsTrue(entity.RemoveRelation<IntRelation, int>(2));

        AreEqual(new [] { 2 }, keys);
    }

    [Test]
    public static void RemoveRelation_AbsentKey_FiresNothing()
    {
        var store  = new EntityStore();
        var entity = store.CreateEntity();
        entity.AddRelation(new IntRelation { value = 1 });
        var rec    = Record(store);

        IsFalse(entity.RemoveRelation<IntRelation, int>(2));

        IsEmpty(rec.Relations);
        IsEmpty(rec.Components);
    }

    [Test]
    public static void ClearRelations_FiresRemovePerRelation()
    {
        var store  = new EntityStore();
        var entity = store.CreateEntity();
        for (int n = 1; n <= 3; n++) {
            entity.AddRelation(new IntRelation { value = n });
        }
        var rec = Record(store);

        AreEqual(3, entity.ClearRelations<IntRelation>());

        AreEqual(3, rec.Relations.Count);
        foreach (var ev in rec.Relations) {
            AreEqual(RelationChangedAction.Remove, ev.Action);
            AreEqual(entity.Id,                    ev.EntityId);
            AreEqual(typeof(IntRelation),          ev.Type);
        }
        AreEqual(0, entity.GetRelations<IntRelation>().Length);
        IsEmpty (rec.Components);
    }

    [Test]
    public static void ClearRelations_EmptySet_FiresNothing()
    {
        var store  = new EntityStore();
        var entity = store.CreateEntity();
        var rec    = Record(store);

        AreEqual(0, entity.ClearRelations<IntRelation>());

        IsEmpty(rec.Relations);
    }

    [Test]
    public static void ClearRelations_HandlerSeesEmptySet()
    {
        var store  = new EntityStore();
        var entity = store.CreateEntity();
        entity.AddRelation(new IntRelation { value = 1 });
        entity.AddRelation(new IntRelation { value = 2 });
        var counts = new List<int>();
        var keys   = new List<int>();
        store.OnRelationChanged += ev => {
            counts.Add(entity.GetRelations<IntRelation>().Length);
            keys  .Add(ev.Key<IntRelation, int>());
        };

        entity.ClearRelations<IntRelation>();

        AreEqual(new [] { 0, 0 }, counts);
        keys.Sort();
        AreEqual(new [] { 1, 2 }, keys);
    }

    [Test]
    public static void Key_WrongRelationType_Throws()
    {
        var store  = new EntityStore();
        var entity = store.CreateEntity();
        Exception exception = null;
        store.OnRelationChanged += ev => exception = Throws<ArgumentException>(() => ev.Key<AttackRelation, Entity>());

        entity.AddRelation(new IntRelation { value = 1 });

        AreEqual("Key<TRelation,TKey>() - expect relation Type: IntRelation. TRelation: AttackRelation", exception!.Message);
    }

    [Test]
    public static void DeleteEntity_FiresNothing()
    {
        var store  = new EntityStore();
        var entity = store.CreateEntity();
        var target = store.CreateEntity();
        entity.AddRelation(new IntRelation   { value  = 1 });
        entity.AddRelation(new AttackRelation { target = target });
        var rec = Record(store);

        entity.DeleteEntity();

        IsEmpty(rec.Relations);
    }

    [Test]
    public static void LinkRelation_AddRemove_FiresEvents()
    {
        var store  = new EntityStore();
        var source = store.CreateEntity();
        var target = store.CreateEntity();
        var rec    = Record(store);

        source.AddRelation(new AttackRelation { target = target });
        AssertSingle(rec, source, RelationChangedAction.Add, typeof(AttackRelation));
        AreEqual(target, rec.Relations[0].Key<AttackRelation, Entity>());
        AreEqual(1, target.GetIncomingLinks<AttackRelation>().Count);

        rec.Relations.Clear();
        IsTrue(source.RemoveRelation<AttackRelation>(target));
        AssertSingle(rec, source, RelationChangedAction.Remove, typeof(AttackRelation));
        AreEqual(target, rec.Relations[0].Key<AttackRelation, Entity>());
        AreEqual(0, target.GetIncomingLinks<AttackRelation>().Count);
    }

    [Test]
    public static void ClearRelations_LinkRelation_FiresKeyPerTarget()
    {
        var store   = new EntityStore();
        var source  = store.CreateEntity();
        var targets = new List<Entity>();
        for (int n = 0; n < 3; n++) {
            var target = store.CreateEntity();
            targets.Add(target);
            source.AddRelation(new AttackRelation { target = target });
        }
        var keys = new List<int>();
        store.OnRelationChanged += ev => keys.Add(ev.Key<AttackRelation, Entity>().Id);

        AreEqual(3, source.ClearRelations<AttackRelation>());

        keys.Sort();
        AreEqual(new [] { targets[0].Id, targets[1].Id, targets[2].Id }, keys);
    }

    /// The source entities survive the delete, so each is told its relation is gone.
    [Test]
    public static void DeleteLinkTarget_FiresRemovePerSource()
    {
        var store   = new EntityStore();
        var target  = store.CreateEntity();
        var sources = new List<Entity>();
        for (int n = 0; n < 3; n++) {
            var source = store.CreateEntity();
            sources.Add(source);
            source.AddRelation(new AttackRelation { target = target });
        }
        var rec  = Record(store);
        var keys = new List<int>();
        store.OnRelationChanged += ev => keys.Add(ev.Key<AttackRelation, Entity>().Id);

        target.DeleteEntity();

        AreEqual(3, rec.Relations.Count);
        var ids = new List<int>();
        foreach (var ev in rec.Relations) {
            AreEqual(RelationChangedAction.Remove, ev.Action);
            AreEqual(typeof(AttackRelation),       ev.Type);
            ids.Add(ev.EntityId);
        }
        ids.Sort();
        AreEqual(new [] { sources[0].Id, sources[1].Id, sources[2].Id }, ids);
        AreEqual(new [] { target.Id, target.Id, target.Id }, keys);
        foreach (var source in sources) {
            AreEqual(0, source.GetRelations<AttackRelation>().Length);
        }
    }

    [Test]
    public static void Deserialize_FiresNothing()
    {
        var source = new EntityStore();
        var entity = source.CreateEntity(1);
        var target = source.CreateEntity(2);
        entity.AddRelation(new AttackRelation { target = target });
        entity.AddRelation(new InventoryItem  { type = InventoryItemType.Axe, amount = 5 });
        entity.AddComponent(new Position { x = 1, y = 2, z = 3 });

        var serializer = new EntitySerializer();
        using var stream = new MemoryStream();
        serializer.WriteStore(source, stream);
        stream.Position = 0;

        var store  = new EntityStore();
        var rec    = Record(store);
        var result = serializer.ReadIntoStore(store, stream);

        IsNull  (result.error);
        IsEmpty (rec.Relations);
        var entity1 = store.GetEntityById(1);
        AreEqual(1, entity1.GetRelations<AttackRelation>().Length);
        AreEqual(1, entity1.GetRelations<InventoryItem>().Length);
        AreEqual(5, entity1.GetRelations<InventoryItem>()[0].amount);
        IsTrue  (entity1.HasComponent<Position>());
        AreEqual(1, store.GetEntityById(2).GetIncomingLinks<AttackRelation>().Count);
    }

    [Test]
    public static void ClearRelations_ReentrantClear_KeepsOuterKeys()
    {
        var store = new EntityStore();
        var outer = store.CreateEntity();
        var inner = store.CreateEntity();
        foreach (var value in new [] { 1, 2, 3 }) {
            outer.AddRelation(new IntRelation { value = value });
            inner.AddRelation(new IntRelation { value = value + 10 });
        }
        var outerKeys = new List<int>();
        var reentered = false;
        store.OnRelationChanged += ev => {
            if (ev.EntityId == outer.Id) {
                outerKeys.Add(ev.Key<IntRelation, int>());
            }
            if (ev.EntityId == outer.Id && !reentered) {
                reentered = true;
                inner.ClearRelations<IntRelation>();
            }
        };
        AreEqual(3, outer.ClearRelations<IntRelation>());

        outerKeys.Sort();
        AreEqual(new [] { 1, 2, 3 }, outerKeys);
    }

    [Test]
    public static void DeleteEntity_ReentrantTargetDelete_KeepsOuterSources()
    {
        var store   = new EntityStore();
        var outer   = store.CreateEntity();
        var inner   = store.CreateEntity();
        var expected = new List<int>();
        for (int n = 0; n < 3; n++) {
            var outerSource = store.CreateEntity();
            outerSource.AddRelation(new AttackRelation { target = outer });
            expected.Add(outerSource.Id);
            store.CreateEntity().AddRelation(new AttackRelation { target = inner });
        }
        var outerSources = new List<int>();
        var reentered    = false;
        store.OnRelationChanged += ev => {
            if (ev.Key<AttackRelation, Entity>() == outer) {
                outerSources.Add(ev.EntityId);
            }
            if (!reentered) {
                reentered = true;
                inner.DeleteEntity();
            }
        };
        outer.DeleteEntity();

        outerSources.Sort();
        expected.Sort();
        AreEqual(expected, outerSources);
    }

    [Test]
    public static void DeleteEntity_HandlerDeletesSameTarget_IsRejected()
    {
        var store  = new EntityStore();
        var target = store.CreateEntity();
        for (int n = 0; n < 3; n++) {
            store.CreateEntity().AddRelation(new AttackRelation { target = target });
        }
        var count      = store.Count;
        var wasNull    = false;
        store.OnRelationChanged += _ => wasNull |= target.IsNull;
        target.DeleteEntity();

        IsTrue  (wasNull);
        AreEqual(count - 1, store.Count);
    }

    [Test]
    public static void DeleteEntity_HandlerDeletesSource_SkipsItsEvent()
    {
        var store   = new EntityStore();
        var target  = store.CreateEntity();
        var sources = new List<Entity>();
        for (int n = 0; n < 4; n++) {
            var source = store.CreateEntity();
            source.AddRelation(new AttackRelation { target = target });
            sources.Add(source);
        }
        var last      = sources[3];
        var ids       = new List<int>();
        var reentered = false;
        store.OnRelationChanged += ev => {
            ids.Add(ev.EntityId);
            if (!reentered) {
                reentered = true;
                last.DeleteEntity();
            }
        };
        target.DeleteEntity();

        IsFalse (ids.Contains(last.Id));
        AreEqual(3, ids.Count);
    }

    [Test]
    public static void DeleteEntity_HandlerCreatesEntity_DoesNotGetTheDyingId()
    {
        var store = new EntityStore();
        for (int n = 0; n < 8; n++) {
            store.CreateEntity();
        }
        var source = store.CreateEntity();
        var target = store.CreateEntity(10);   // leaves intern.sequenceId below 10
        source.AddRelation(new AttackRelation { target = target });

        Entity created = default;
        store.OnRelationChanged += _ => {
            if (created.IsNull) {
                created = store.CreateEntity();
            }
        };
        var count = store.Count;
        target.DeleteEntity();

        AreNotEqual(target.Id, created.Id);
        IsFalse (created.IsNull);
        AreEqual(count, store.Count);
    }

    [Test]
    public static void DeleteEntity_HandlerMovesArchetypeRows_LeavesArchetypeIntact()
    {
        var store  = new EntityStore();
        var x      = store.CreateEntity();
        var y      = store.CreateEntity();
        var target = store.CreateEntity();
        foreach (var entity in new [] { x, y, target }) {
            entity.AddComponent(new Position());
        }
        var source = store.CreateEntity();
        source.AddRelation(new AttackRelation { target = target });

        var moved = false;
        store.OnRelationChanged += _ => {
            if (!moved) {
                moved = true;
                x.RemoveComponent<Position>();
            }
        };
        target.DeleteEntity();

        var ids = new List<int>();
        foreach (var entity in store.Query<Position>().Entities) {
            ids.Add(entity.Id);
        }
        AreEqual(new [] { y.Id }, ids);
        IsFalse (y.IsNull);
        IsTrue  (y.HasComponent<Position>());
    }

    [Test]
    public static void ClearRelations_HandlerDeletesOwner_StopsFiring()
    {
        var store  = new EntityStore();
        var entity = store.CreateEntity();
        foreach (var value in new [] { 1, 2, 3 }) {
            entity.AddRelation(new IntRelation { value = value });
        }
        var nullSeen = false;
        store.OnRelationChanged += ev => {
            nullSeen |= ev.Entity.IsNull;
            if (!entity.IsNull) {
                entity.DeleteEntity();
            }
        };
        entity.ClearRelations<IntRelation>();

        IsFalse(nullSeen);
    }

    [Test]
    public static void DeleteEntity_HandlerRecyclesSourceId_SkipsItsEvent()
    {
        var store   = new EntityStore();
        var target  = store.CreateEntity();
        var sources = new List<Entity>();
        for (int n = 0; n < 4; n++) {
            var source = store.CreateEntity();
            source.AddRelation(new AttackRelation { target = target });
            sources.Add(source);
        }
        var last      = sources[3];
        var ids       = new List<int>();
        var reentered = false;
        store.OnRelationChanged += ev => {
            ids.Add(ev.EntityId);
            AreEqual(0, ev.Entity.GetRelations<AttackRelation>().Length);
            if (!reentered) {
                reentered = true;
                last.DeleteEntity();
                store.CreateEntity();   // recycles last.Id
            }
        };
        target.DeleteEntity();

        IsFalse(ids.Contains(last.Id));
    }
}

}
