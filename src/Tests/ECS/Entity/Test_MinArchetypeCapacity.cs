using System;
using Friflo.Engine.ECS;
using NUnit.Framework;
using Tests.ECS.Relations;
using static NUnit.Framework.Assert;

// ReSharper disable InconsistentNaming
// ReSharper disable once CheckNamespace
namespace Tests.ECS {

public static class Test_MinArchetypeCapacity
{
    private const int Low = 64;

    [Test]
    public static void Test_MinArchetypeCapacity_default()
    {
        var store = new EntityStore();
        AreEqual(ArchetypeUtils.MinCapacity, store.MinArchetypeCapacity);
        AreEqual(ArchetypeUtils.MinCapacity, store.GetArchetype(ComponentTypes.Get<MyComponent1>()).Capacity);
    }

    [Test]
    public static void Test_MinArchetypeCapacity_component_archetypes()
    {
        var store = new EntityStore(Low);
        AreEqual(Low, store.MinArchetypeCapacity);
        AreEqual(Low, store.GetArchetype(ComponentTypes.Get<MyComponent1>()).Capacity);
        AreEqual(Low, store.GetArchetype(ComponentTypes.Get<MyComponent1, MyComponent2>()).Capacity);

        var entity = store.CreateEntity();
        AreEqual(Low, entity.Archetype.Capacity);
    }

    [Test]
    public static void Test_MinArchetypeCapacity_relation_archetypes()
    {
        var low     = new EntityStore(Low);
        var lowBase = low.CapacitySumArchetypes;
        low.CreateEntity().AddRelation(new IntRelation { value = 1 });

        var high     = new EntityStore();
        var highBase = high.CapacitySumArchetypes;
        high.CreateEntity().AddRelation(new IntRelation { value = 1 });

        AreEqual(Low,                           low.CapacitySumArchetypes  - lowBase);
        AreEqual(ArchetypeUtils.MinCapacity,    high.CapacitySumArchetypes - highBase);
    }

    [Test]
    public static void Test_MinArchetypeCapacity_is_per_store()
    {
        var low  = new EntityStore(Low);
        var high = new EntityStore();
        var types = ComponentTypes.Get<MyComponent1>();

        AreEqual(Low,                        low.GetArchetype(types).Capacity);
        AreEqual(ArchetypeUtils.MinCapacity, high.GetArchetype(types).Capacity);
    }

    [Test]
    public static void Test_MinArchetypeCapacity_grows_by_doubling()
    {
        var store = new EntityStore(Low);
        var arch  = store.GetArchetype(ComponentTypes.Get<MyComponent1>());
        for (int n = 0; n < Low; n++) {
            arch.CreateEntity();
        }
        AreEqual(Low, arch.Capacity);

        arch.CreateEntity();
        AreEqual(2 * Low, arch.Capacity);

        for (int n = 0; n < 200; n++) {
            arch.CreateEntity();
        }
        AreEqual(512, arch.Capacity);
    }

    [Test]
    public static void Test_MinArchetypeCapacity_shrinks_to_the_floor()
    {
        var store = new EntityStore(Low) { ShrinkRatioThreshold = 0 };
        var arch  = store.GetArchetype(ComponentTypes.Get<MyComponent1>());
        var entities = new Entity[2000];
        for (int n = 0; n < entities.Length; n++) {
            entities[n] = arch.CreateEntity();
            entities[n].GetComponent<MyComponent1>().a = n;
        }
        for (int n = 1; n < entities.Length; n++) {
            entities[n].DeleteEntity();
        }
        AreEqual(1,       arch.Count);
        AreEqual(2 * Low, arch.Capacity);
        AreEqual(0,       entities[0].GetComponent<MyComponent1>().a);
    }

    [Test]
    public static void Test_MinArchetypeCapacity_EnsureCapacity()
    {
        var store = new EntityStore(Low);
        var arch  = store.GetArchetype(ComponentTypes.Get<MyComponent1>());
        arch.EnsureCapacity(100);
        AreEqual(128, arch.Capacity);
    }

    [Test]
    public static void Test_MinArchetypeCapacity_chunk_padding_stays_in_bounds()
    {
        var store = new EntityStore(Low);
        var arch  = store.GetArchetype(ComponentTypes.Get<ByteComponent>());
        var query = store.Query<ByteComponent>();
        for (int n = 0; n < 200; n++) {
            foreach (var (components, _) in query.Chunks) {
                var span512 = components.AsSpan512<byte>();
                var span256 = components.AsSpan256<byte>();
                var span128 = components.AsSpan128<byte>();
                IsTrue(span512.Length <= arch.Capacity);
                IsTrue(span256.Length <= arch.Capacity);
                IsTrue(span128.Length <= arch.Capacity);
                for (int i = 0; i < span512.Length; i++) {
                    _ = span512[i];
                }
            }
            arch.CreateEntity();
        }
    }

    [Test]
    public static void Test_MinArchetypeCapacity_rejects_invalid_values()
    {
        foreach (var value in new [] { -1, 0, 1, 32, 63, 96, 100, 768 }) {
            var e = Throws<ArgumentException>(() => _ = new EntityStore(value));
            AreEqual("minArchetypeCapacity", e.ParamName);
        }
        foreach (var value in new [] { 64, 128, 256, 512, 1024 }) {
            AreEqual(value, new EntityStore(value).MinArchetypeCapacity);
        }
    }
}

}
