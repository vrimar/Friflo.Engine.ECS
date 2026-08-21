using System;
using System.Collections.Generic;
using System.IO;
using Friflo.Json.Fliox;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Serialize;
using NUnit.Framework;
using Tests.ECS.Relations;
using static NUnit.Framework.Assert;

// ReSharper disable InconsistentNaming
namespace Tests.ECS {

public static class Test_Regressions
{
    [Test]
    public static void InsertChild_NegativeIndex_DoesNotMutate()
    {
        var store = new EntityStore();
        var p     = store.CreateEntity();
        var q     = store.CreateEntity();
        var c     = store.CreateEntity();
        q.AddChild(c);

        Throws<IndexOutOfRangeException>(() => p.InsertChild(-1, c));

        AreEqual(1, q.ChildCount);
        AreEqual(0, p.ChildCount);
        AreEqual(q.Id, c.Parent.Id);
    }

    [Test]
    public static void ChildEntities_HeldAcrossDelete_DoesNotReadRecycledBlock()
    {
        var store  = new EntityStore();
        var parent = store.CreateEntity();
        for (int n = 0; n < 3; n++) {
            parent.AddChild(store.CreateEntity());
        }
        var children = parent.ChildEntities;
        AreEqual(3, children.Count);
        parent.DeleteEntity();

        var other = store.CreateEntity();
        for (int n = 0; n < 3; n++) {
            other.AddChild(store.CreateEntity());
        }
        Throws<NullReferenceException>(() => { var _ = children.Count; });
    }

    [Test]
    public static void ReadIntoStore_UnknownRelationKey_IsPreserved()
    {
        var store     = new EntityStore();
        var converter = EntityConverter.Default;
        var data      = new DataEntity {
            pid        = 1,
            components = new JsonValue("{\"item\":[{\"type\":\"Axe\",\"amount\":5}],\"unknownRel\":[{\"a\":1}]}")
        };
        var entity = converter.DataEntityToEntity(data, store, out var error);

        IsNull  (error);
        AreEqual(1, entity.GetRelations<InventoryItem>().Length);
        AreEqual(5, entity.GetRelations<InventoryItem>()[0].amount);

        var unresolved = entity.GetComponent<Unresolved>();
        AreEqual(1,             unresolved.components.Length);
        AreEqual("unknownRel",  unresolved.components[0].key);
        AreEqual("[{\"a\":1}]", unresolved.components[0].value.AsString());

        var result = converter.EntityToDataEntity(entity, null, false);
        IsTrue(result.components.AsString().Contains("\"unknownRel\":[{\"a\":1}]"));
    }

    [Test]
    public static void WriteStore_UnknownRelationKey_SurvivesRoundTrip()
    {
        var store     = new EntityStore();
        var converter = EntityConverter.Default;
        var data      = new DataEntity {
            pid        = 1,
            components = new JsonValue("{\"unknownRel\":[{\"a\":1}],\"item\":[{\"type\":\"Axe\",\"amount\":5}]}")
        };
        converter.DataEntityToEntity(data, store, out var error);
        IsNull(error);

        var serializer = new EntitySerializer();
        var stream     = new MemoryStream();
        serializer.WriteStore(store, stream);
        stream.Position = 0;

        var target = new EntityStore();
        IsNull(serializer.ReadIntoStore(target, stream).error);

        var entity = target.GetEntityById(1);
        AreEqual(1,             entity.GetRelations<InventoryItem>().Length);
        AreEqual("unknownRel",  entity.GetComponent<Unresolved>().components[0].key);
        AreEqual("[{\"a\":1}]", entity.GetComponent<Unresolved>().components[0].value.AsString());
    }

    [Test]
    public static void DataEntityToEntity_ComponentKeyAsArray_ReturnsError()
    {
        var store     = new EntityStore(PidType.UsePidAsId);
        var converter = new EntityConverter();
        var data      = new DataEntity {
            pid        = 1,
            components = new JsonValue("{\"pos\":[{\"x\":1}]}")
        };
        converter.DataEntityToEntity(data, store, out var error);

        AreEqual("'components' member is an array but 'pos' is not a relation type. id: 1", error);
    }

    [Test]
    public static void ChildEntities_RemoveDuringEnumeration_DoesNotReadRecycledBlock()
    {
        var store  = new EntityStore();
        var parent = store.CreateEntity();
        var other  = store.CreateEntity();
        var c0     = store.CreateEntity();
        var c1     = store.CreateEntity();
        var c2     = store.CreateEntity();
        parent.AddChild(c0);
        parent.AddChild(c1);
        parent.AddChild(c2);

        var visited = new List<int>();
        foreach (var child in parent.ChildEntities) {
            visited.Add(child.Id);
            if (child.Id != c0.Id) {
                continue;
            }
            parent.RemoveChild(c0);
            other.AddChild(store.CreateEntity());
            other.AddChild(store.CreateEntity());
            other.AddChild(store.CreateEntity());
        }
        CollectionAssert.IsSubsetOf(visited, new[] { c0.Id, c1.Id, c2.Id });
        AreEqual(2, parent.ChildCount);
        AreEqual(c1.Id, parent.ChildEntities[0].Id);
        AreEqual(c2.Id, parent.ChildEntities[1].Id);
    }

    [Test]
    public static void ChildEntities_AddDuringEnumeration_Terminates()
    {
        var store  = new EntityStore();
        var parent = store.CreateEntity();
        parent.AddChild(store.CreateEntity());
        parent.AddChild(store.CreateEntity());

        int visited = 0;
        foreach (var _ in parent.ChildEntities) {
            if (++visited > 100) {
                break;
            }
            parent.AddChild(store.CreateEntity());
        }
        AreEqual(2, visited);
        AreEqual(4, parent.ChildCount);
    }

    [Test]
    public static void DeleteEntity_ThrowingRelationHandler_CompletesTeardown()
    {
        var store   = new EntityStore();
        var parent  = store.CreateEntity();
        var target  = store.CreateEntity();
        var source  = store.CreateEntity();
        parent.AddChild(target);
        target.AddChild(store.CreateEntity());
        source.AddRelation(new AttackRelation { target = target });

        store.OnRelationChanged += _ => throw new InvalidOperationException("from handler");

        var targetId = target.Id;
        var count    = store.Count;
        Throws<InvalidOperationException>(() => target.DeleteEntity());

        AreEqual(count - 1,  store.Count);
        AreEqual(0,          parent.ChildCount);
        IsTrue  (target.IsNull);

        var reused = store.CreateEntity();
        AreEqual(targetId,   reused.Id);
        AreEqual(0,          reused.ChildCount);
        IsTrue  (target.IsNull);
    }

    [Test]
    public static void DeleteEntity_ThrowingRelationHandler_ClearsEveryLinkRelationType()
    {
        var store   = new EntityStore();
        var target  = store.CreateEntity();
        var source1 = store.CreateEntity();
        var source2 = store.CreateEntity();
        source1.AddRelation(new AttackRelation { target = target });
        source2.AddRelation(new GuardRelation  { target = target });
        var targetId = target.Id;

        store.OnRelationChanged += _ => throw new InvalidOperationException("from handler");
        Throws<InvalidOperationException>(() => target.DeleteEntity());

        AreEqual(0, source1.GetRelations<AttackRelation>().Length);
        AreEqual(0, source2.GetRelations<GuardRelation>().Length);

        var reused = store.CreateEntity();
        AreEqual(targetId, reused.Id);
        AreEqual(0, reused.GetIncomingLinks<GuardRelation>().Count);
    }

    [Test]
    public static void RelationChanged_HandlerAddsRelation_KeyStaysValidForLaterHandlers()
    {
        var store = new EntityStore();
        var a     = store.CreateEntity();
        var b     = store.CreateEntity();
        store.OnRelationChanged += args => {
            if (args.EntityId == a.Id && args.Key<IntRelation,int>() == 7) {
                b.AddRelation(new IntRelation { value = 99 });
            }
        };
        var keys = new List<string>();
        store.OnRelationChanged += args => keys.Add($"{args.EntityId}:{args.Key<IntRelation,int>()}");

        a.AddRelation(new IntRelation { value = 7 });

        AreEqual(new [] { $"{b.Id}:99", $"{a.Id}:7" }, keys.ToArray());
    }

    [Test]
    public static void DeleteEntity_ReentrantFromDeleteEvent_KeepsCountCorrect()
    {
        var store = new EntityStore();
        var a     = store.CreateEntity();
        var b     = store.CreateEntity();
        var count = store.Count;
        bool once = false;
        store.OnEntityDelete += args => {
            if (once) return;
            once = true;
            args.Entity.DeleteEntity();
        };
        a.DeleteEntity();

        AreEqual(count - 1, store.Count);
        IsFalse (b.IsNull);
    }

    [Test]
    public static void ClearRelations_HandlerUnsubscribes_IsNotInvokedAgain()
    {
        var store  = new EntityStore();
        var entity = store.CreateEntity();
        for (int n = 1; n <= 3; n++) {
            entity.AddRelation(new IntRelation { value = n });
        }
        int calls = 0;
        Action<RelationChanged> handler = null;
        handler = _ => { calls++; store.OnRelationChanged -= handler; };
        store.OnRelationChanged += handler;

        entity.ClearRelations<IntRelation>();

        AreEqual(1, calls);
    }

    [Test]
    public static void ChildEntities_DeleteCurrentDuringEnumeration_VisitsEveryChild()
    {
        for (int count = 1; count <= 8; count++) {
            var store  = new EntityStore();
            var parent = store.CreateEntity();
            for (int n = 0; n < count; n++) {
                parent.AddChild(store.CreateEntity());
            }
            var visited = new List<int>();
            foreach (var child in parent.ChildEntities) {
                visited.Add(child.Id);
                child.DeleteEntity();
            }
            AreEqual(count, visited.Count,     $"child count: {count}");
            AreEqual(0,     parent.ChildCount, $"child count: {count}");
        }
    }

    [Test]
    public static void ChildEntities_RemoveCurrentDuringEnumeration_VisitsEveryChild()
    {
        for (int count = 1; count <= 8; count++) {
            var store  = new EntityStore();
            var parent = store.CreateEntity();
            for (int n = 0; n < count; n++) {
                parent.AddChild(store.CreateEntity());
            }
            var visited = new List<int>();
            foreach (var child in parent.ChildEntities) {
                visited.Add(child.Id);
                parent.RemoveChild(child);
            }
            AreEqual(count, visited.Count,     $"child count: {count}");
            AreEqual(0,     parent.ChildCount, $"child count: {count}");
        }
    }
}

}
