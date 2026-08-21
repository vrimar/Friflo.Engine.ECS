// Copyright (c) Ullrich Praetz - https://github.com/friflo. All rights reserved.
// See LICENSE file in the project root for full license information.


using System;
using Friflo.Engine.ECS.Relations;
using static System.Diagnostics.DebuggerBrowsableState;
using Browse = System.Diagnostics.DebuggerBrowsableAttribute;

// ReSharper disable once CheckNamespace
// ReSharper disable InconsistentNaming
namespace Friflo.Engine.ECS;

/// <summary>
/// The modification type of a <see cref="RelationChanged"/> event: <see cref="Remove"/>, <see cref="Add"/> or <see cref="Update"/> relation.
/// </summary>
public enum RelationChangedAction : byte
{
    /// <summary> An <see cref="IRelation"/> is removed from an <see cref="Entity"/>. </summary>
    Remove  = 0,
    /// <summary> An <see cref="IRelation"/> is added to an <see cref="Entity"/>. </summary>
    Add     = 1,
    /// <summary> An <see cref="IRelation"/> of an <see cref="Entity"/> is updated when calling
    /// <see cref="RelationExtensions.AddRelation{TRelation}"/> with a key the entity already has. </summary>
    Update  = 2,
}

/// <summary>
/// Is the event for event handlers added to <see cref="EntityStore.OnRelationChanged"/>.
/// </summary>
/// <remarks>
/// These events are fired on:
/// <list type="bullet">
///     <item><see cref="RelationExtensions.AddRelation{TRelation}"/></item>
///     <item><see cref="RelationExtensions.RemoveRelation{TRelation,TKey}"/></item>
///     <item><see cref="RelationExtensions.RemoveRelation{TRelation}(Entity,Entity)"/></item>
///     <item><see cref="RelationExtensions.ClearRelations{TRelation}"/> - once per removed relation</item>
///     <item><see cref="Entity.DeleteEntity"/> of a link relation target - once per entity linking it</item>
/// </list>
/// The event carries the key of the changed relation - see <see cref="Key{TRelation,TKey}"/> - but not its value.<br/>
/// Read the current relations of the <see cref="Entity"/> with
/// <see cref="RelationExtensions.GetRelations{TRelation}"/> or
/// <see cref="RelationExtensions.TryGetRelation{TRelation,TKey}"/>.<br/>
/// On <see cref="RelationChangedAction.Remove"/> the relation is already gone when the event is fired.<br/>
/// <br/>
/// Deleting an entity fires no events for the relations it owned - as it fires no
/// <see cref="ComponentChanged"/> events for its components.<br/>
/// It does fire events for the relations it was the <see cref="ILinkRelation"/> target of, as these are
/// owned by entities that outlive the delete.
/// </remarks>
public readonly struct  RelationChanged
{
#region fields
    /// <summary>The <see cref="EntityStore"/> containing the <see cref="Entity"/> that emitted the event.</summary>
    public  readonly    EntityStore             Store;

    /// <summary>The <c>Id</c> of the <see cref="Entity"/> that emitted the event.</summary>
    [Browse(Never)]
    public  readonly    int                     EntityId;

    /// <summary>The executed entity change: <see cref="RelationChangedAction.Remove"/>,
    /// <see cref="RelationChangedAction.Add"/> or <see cref="RelationChangedAction.Update"/> relation.</summary>
    public  readonly    RelationChangedAction   Action;

    [Browse(Never)]
    public  readonly    int                     StructIndex;

    [Browse(Never)]
    private readonly    IRelationKeyStash       stash;

    // use nested class to minimize noise in debugger
    private static class Static {
        internal static readonly ComponentType[] ComponentTypes = EntityStoreBase.Static.EntitySchema.components;
    }
    #endregion

#region properties
    /// <summary>The <see cref="Entity"/> that emitted the event - aka the publisher.</summary>
    public              Entity                  Entity          => new Entity(Store, EntityId);

    /// <summary>The <see cref="ECS.ComponentType"/> of the added / updated / removed relation.</summary>
    /// <remarks>
    /// Use <see cref="ComponentType.AsEnum{TEnum}"/> to handle specific relation types with a switch statement.
    /// </remarks>
    [Browse(Never)]
    public              ComponentType           RelationType    => Static.ComponentTypes[StructIndex];

    /// <summary>The <see cref="System.Type"/> of the added / updated / removed relation.</summary>
    public              Type                    Type            => RelationType.Type;

    public override     string                  ToString()      => $"entity: {EntityId} - event > {Action} {RelationType}";
    #endregion

    internal RelationChanged(EntityStore store, int entityId, RelationChangedAction action, int structIndex, IRelationKeyStash stash)
    {
        Store           = store;
        EntityId        = entityId;
        Action          = action;
        StructIndex     = structIndex;
        this.stash      = stash;
    }

    /// <summary>
    /// Returns the key of the added / updated / removed relation.<br/> <b>Note</b>: See Remarks for restrictions.
    /// </summary>
    /// <remarks>
    /// <b>Note</b>:
    /// The <see cref="Key{TRelation,TKey}"/> return value is only valid within the event handler call.<br/>
    /// <see cref="RelationChanged"/> may return an invalid value when calling it outside the event handler scope.<br/>
    /// Instead store the value returned by <see cref="Key{TRelation,TKey}"/> within the handler when using it after the event handler returns.<br/>
    /// Reason: For performance there is only one field per relation type storing the key.<br/>
    /// </remarks>
    /// <typeparam name="TRelation"> The relation type of the changed relation - see <see cref="Type"/>.</typeparam>
    /// <typeparam name="TKey"> The key type of <typeparamref name="TRelation"/>.</typeparam>
    /// <exception cref="ArgumentException"> In case the key is accessed with the wrong relation type. </exception>
    public TKey Key<TRelation, TKey>()
        where TRelation : struct, IRelation<TKey>
    {
        if (StructInfo<TRelation>.Index == StructIndex) {
            return ((IRelationKeyStash<TKey>)stash).GetKeyStash();
        }
        throw TypeException(typeof(TRelation));
    }

    private ArgumentException TypeException(Type type) {
        return new ArgumentException($"Key<TRelation,TKey>() - expect relation Type: {RelationType.Name}. TRelation: {type.Name}");
    }
}
