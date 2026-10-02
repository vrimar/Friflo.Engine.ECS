using System;
using Friflo.Engine.ECS;

[assembly: Parallelize(Workers = 1, Scope = ExecutionScope.ClassLevel)]

namespace Tests.AOT.Struct.ECS {

// Its own binary: a class-valued index anywhere in it keeps IIndexedComponent<> on every type.
[TestClass]
public class Test_AOT_Struct
{
    [TestMethod]
    public void Test_AOT_schema_index_and_relation_types()
    {
        var schema = CreateSchema();
        Assert.AreEqual("ValueStructIndex`2",   schema.GetComponentType<ObjectIdentity>().IndexType.Name);
        Assert.AreEqual("EntityIndex`1",        schema.GetComponentType<Attack>()        .IndexType.Name);
        Assert.AreEqual("EntityIndex`1",        schema.GetComponentType<OwnedBy>()       .IndexType.Name);

        Assert.AreEqual(typeof(int),            schema.GetRelationType<Item>()           .RelationKeyType);
        Assert.AreEqual(typeof(Entity),         schema.GetRelationType<Follow>()         .RelationKeyType);
    }

    [TestMethod]
    public void Test_AOT_struct_index_and_relation()
    {
        CreateSchema();
        var store   = new EntityStore();
        var entity  = store.CreateEntity();
        entity.AddComponent(new ObjectIdentity { Id = new ObjectId(42) });
        entity.AddRelation(new Item { Key = 7 });

        Assert.AreEqual(1, store.ComponentIndex<ObjectIdentity, ObjectId>()[new ObjectId(42)].Count);
        Assert.AreEqual(1, entity.GetRelations<Item>().Length);
    }

    [TestMethod]
    public void Test_AOT_link_component()
    {
        CreateSchema();
        var store   = new EntityStore();
        var target  = store.CreateEntity();
        var source  = store.CreateEntity();
        source.AddComponent(new Attack { Target = target });

        Assert.AreEqual(1, target.GetIncomingLinks<Attack>().Count);
        Assert.AreEqual(1, store.ComponentIndex<Attack, Entity>()[target].Count);

        target.DeleteEntity();
        Assert.IsFalse(source.HasComponent<Attack>());
        Assert.AreEqual(0, store.ComponentIndex<Attack, Entity>().Values.Count);
    }

    [TestMethod]
    public void Test_AOT_entity_index_without_link_component()
    {
        CreateSchema();
        var store   = new EntityStore();
        var owner   = store.CreateEntity();
        var owned   = store.CreateEntity();
        owned.AddComponent(new OwnedBy { Owner = owner });

        Assert.AreEqual(1, owner.CountAllIncomingLinks());
        Assert.AreEqual(1, store.ComponentIndex<OwnedBy, Entity>()[owner].Count);

        owner.DeleteEntity();
        Assert.IsFalse(owned.HasComponent<OwnedBy>());
        Assert.AreEqual(0, store.ComponentIndex<OwnedBy, Entity>().Values.Count);
    }

    [TestMethod]
    public void Test_AOT_link_relation()
    {
        CreateSchema();
        var store   = new EntityStore();
        var source  = store.CreateEntity();
        var target1 = store.CreateEntity();
        var target2 = store.CreateEntity();
        source.AddRelation(new Follow { Target = target1 });
        source.AddRelation(new Follow { Target = target2 });

        Assert.AreEqual(2, source.GetRelations<Follow>().Length);
        Assert.AreEqual(1, target1.GetIncomingLinks<Follow>().Count);

        target1.DeleteEntity();
        var relations = source.GetRelations<Follow>();
        Assert.AreEqual(1,          relations.Length);
        Assert.AreEqual(target2.Id, relations[0].Target.Id);
    }

    private static          EntitySchema    schemaCreated;
    private static readonly object          monitor = new object();

    private static EntitySchema CreateSchema()
    {
        // [Parallelize] is ignored under NativeAOT, so tests race to create the schema
        lock (monitor)
        {
            if (schemaCreated != null) {
                return schemaCreated;
            }
            var aot = new NativeAOT();
            aot.RegisterIndexedComponentStruct<ObjectIdentity, ObjectId>();
            aot.RegisterIndexedComponentEntity<Attack>();
            aot.RegisterIndexedComponentEntity<OwnedBy>();
            aot.RegisterRelation<Item, int>();
            aot.RegisterLinkRelation<Follow>();
            return schemaCreated = aot.CreateSchema();
        }
    }
}

internal readonly struct ObjectId : IEquatable<ObjectId>
{
    private readonly long value;

    public ObjectId(long value) => this.value = value;

    public          bool Equals(ObjectId other) => value == other.value;
    public override bool Equals(object obj)     => obj is ObjectId other && Equals(other);
    public override int  GetHashCode()          => value.GetHashCode();
}

internal struct ObjectIdentity : IIndexedComponent<ObjectId>
{
    public ObjectId Id;
    public readonly ObjectId GetIndexedValue() => Id;
}

internal struct Attack : ILinkComponent
{
    public Entity Target;
    public readonly Entity GetIndexedValue() => Target;
}

internal struct OwnedBy : IIndexedComponent<Entity>
{
    public Entity Owner;
    public readonly Entity GetIndexedValue() => Owner;
}

internal struct Item : IRelation<int>
{
    public int Key;
    public readonly int GetRelationKey() => Key;
}

internal struct Follow : ILinkRelation
{
    public Entity Target;
    public readonly Entity GetRelationKey() => Target;
}

}
