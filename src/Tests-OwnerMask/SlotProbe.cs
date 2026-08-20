using System;
using Friflo.Engine.ECS;

// ReSharper disable once CheckNamespace
namespace Tests.OwnerMask {

internal enum SlotKind
{
    Indexed,
    Relation,
    LinkComponent,
    LinkRelation,
}

/// <summary>
/// Exercises one slot type through the public API. Slots resolve at runtime but
/// <c>ComponentIndex&lt;T,TValue&gt;()</c> and friends need T at compile time, so a probe per type
/// bridges that without reflection.
/// </summary>
internal interface ISlotProbe
{
    Type     Type { get; }
    SlotKind Kind { get; }

    /// <paramref name="target"/> applies to link kinds, <paramref name="value"/> to the rest.
    void Add(Entity entity, Entity target, int value);

    /// Rows the store still holds for this type. Zero means fully cleaned up.
    int RowCount(EntityStore store);

    bool IsAttached(Entity entity);
}

internal sealed class IndexedProbe<T> : ISlotProbe
    where T : struct, IIndexedComponent<int>, ISlotValue
{
    public Type     Type => typeof(T);
    public SlotKind Kind => SlotKind.Indexed;

    public void Add(Entity entity, Entity target, int value)
    {
        T component = default;
        component.Value = value;
        entity.AddComponent(component);
    }

    public int  RowCount  (EntityStore store) => store.ComponentIndex<T, int>().Values.Count;
    public bool IsAttached(Entity entity)     => entity.HasComponent<T>();
}

internal sealed class RelationProbe<T> : ISlotProbe
    where T : struct, IRelation<int>, ISlotValue
{
    public Type     Type => typeof(T);
    public SlotKind Kind => SlotKind.Relation;

    public void Add(Entity entity, Entity target, int value)
    {
        T relation = default;
        relation.Value = value;
        entity.AddRelation(relation);
    }

    public int  RowCount  (EntityStore store) => store.EntityRelations<T>().Entities.Count;
    public bool IsAttached(Entity entity)     => entity.GetRelations<T>().Length > 0;
}

internal sealed class LinkComponentProbe<T> : ISlotProbe
    where T : struct, ILinkComponent, ISlotTarget
{
    public Type     Type => typeof(T);
    public SlotKind Kind => SlotKind.LinkComponent;

    public void Add(Entity entity, Entity target, int value)
    {
        T component = default;
        component.Target = target;
        entity.AddComponent(component);
    }

    public int  RowCount  (EntityStore store) => store.LinkComponentIndex<T>().Values.Count;
    public bool IsAttached(Entity entity)     => entity.HasComponent<T>();
}

internal sealed class LinkRelationProbe<T> : ISlotProbe
    where T : struct, ILinkRelation, ISlotTarget
{
    public Type     Type => typeof(T);
    public SlotKind Kind => SlotKind.LinkRelation;

    public void Add(Entity entity, Entity target, int value)
    {
        T relation = default;
        relation.Target = target;
        entity.AddRelation(relation);
    }

    public int  RowCount  (EntityStore store) => store.EntityRelations<T>().Entities.Count;
    public bool IsAttached(Entity entity)     => entity.GetRelations<T>().Length > 0;
}

}
