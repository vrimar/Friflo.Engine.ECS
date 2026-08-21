// Copyright (c) Ullrich Praetz - https://github.com/friflo. All rights reserved.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Friflo.Engine.ECS.Collections;
using static System.Diagnostics.DebuggerBrowsableState;
using Browse = System.Diagnostics.DebuggerBrowsableAttribute;

// ReSharper disable ConvertToAutoPropertyWhenPossible
// ReSharper disable ConvertToAutoProperty
// ReSharper disable once CheckNamespace
namespace Friflo.Engine.ECS;

/// <summary>
/// Return the child entities of an <see cref="Entity"/>.
/// </summary>
[DebuggerTypeProxy(typeof(ChildEntitiesDebugView))]
public readonly struct ChildEntities : IEnumerable<Entity>
{
#region properties
    public          int                 Count           => ChildIds.count;
    public          ReadOnlySpan<int>   Ids             => ChildIds.GetSpan(store.extension.childHeap, store);

    public          Entity              this[int index] => new Entity(store, ChildIds.GetAt(index, store.extension.childHeap));
    public override string              ToString()      => $"Entity[{Count}]";
    #endregion

#region fields
    [Browse(Never)]     internal readonly   Entity              entity;     // 16
    #endregion

    internal            EntityStore         store           => entity.store;

    /// Resolved per access. An <see cref="IdArray"/> snapshot would survive its owner and read a
    /// recycled block, i.e. another entity's children.
    internal        IdArray             ChildIds        => entity.GetChildIdArray();

    // --- IEnumerable<>
    IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => new ChildEnumerator(this);

    // --- IEnumerable
    IEnumerator                 IEnumerable.GetEnumerator() => new ChildEnumerator(this);

    // --- new
    public ChildEnumerator                  GetEnumerator() => new ChildEnumerator(this);

    internal ChildEntities(Entity entity) {
        this.entity = entity;
    }

    public void ToArray(Entity[] array) {
        var ids = Ids;
        for (int n = 0; n < ids.Length; n++) {
            array[n] = new Entity(store, ids[n]);
        }
    }
    
    internal Entity[] ToArray() {
        var ids     = Ids;
        var array   = new Entity[ids.Length];
        for (int n = 0; n < ids.Length; n++) {
            array[n] = new Entity(store, ids[n]);
        }
        return array;
    }
}

/// <summary>
/// Use to enumerate the child entities stored in <see cref="Entity"/>.<see cref="Entity.ChildEntities"/>.  
/// </summary>
public struct ChildEnumerator : IEnumerator<Entity>
{
#region fields
    private             int         index;      //  4
    private             int         lastCount;  //  4
    private             int         currentId;  //  4
    private readonly    int         count;      //  4
    private readonly    Entity      entity;     // 16
    private readonly    IdArrayHeap heap;       //  8
    #endregion

    internal ChildEnumerator(in ChildEntities childEntities) {
        entity      = childEntities.entity;
        heap        = childEntities.store.extension.childHeap;
        count       = childEntities.ChildIds.count;
        lastCount   = count;
    }

    /// Resolved per access. A snapshot taken at construction would survive a mutation made while
    /// enumerating and read a recycled block, i.e. another entity's children.
    private readonly    IdArray     ChildIds    => entity.GetChildIdArray();

    // --- IEnumerator<>
    public readonly Entity Current { get {
        if (index == 0) {
            throw new IndexOutOfRangeException("Index was out of range. Must be >= 0 and < ChildEntities.Count");
        }
        return new Entity(entity.store, currentId);
    }}

    // --- IEnumerator
    public bool MoveNext() {
        var childIds    = ChildIds;
        int childCount  = childIds.count;
        if (childCount < lastCount) {
            // removed children shifted the remaining ones left - step back or they would be skipped
            index -= lastCount - childCount;
            if (index < 0) {
                index = 0;
            }
        }
        lastCount = childCount;
        // count bounds an enumeration that keeps adding children; childCount stops one that removes them
        if (index < count && index < childCount) {
            // read the id here: resolving it in Current would let user code recycle the block in between
            currentId = childIds.GetAt(index, heap);
            index++;
            return true;
        }
        return false;
    }

    public void Reset() {
        index       = 0;
        lastCount   = ChildIds.count;
    }
    
    object IEnumerator.Current => Current;

    // --- IDisposable
    public void Dispose() { }
}

internal class ChildEntitiesDebugView
{
    [Browse(RootHidden)]
    public  Entity[]        Entities => childEntities.ToArray();

    [Browse(Never)]
    private ChildEntities   childEntities;
        
    internal ChildEntitiesDebugView(ChildEntities childEntities)
    {
        this.childEntities = childEntities;
    }
}