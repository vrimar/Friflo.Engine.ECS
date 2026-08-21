using System;
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
}

}
