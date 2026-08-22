# Changelog

All notable changes to `CoFuse.Engine.ECS` and `CoFuse.Engine.ECS.Boost`.

This project is a fork of [friflo/Friflo.Engine.ECS](https://github.com/friflo/Friflo.Engine.ECS)
by Ullrich Praetz. Versions carry a `-fuse.N` suffix; the major version tracks this
fork's own compatibility line, not upstream's.

## 4.1.0-fuse.1

### Added

- **The archetype capacity floor is a per-store setting.** `new EntityStore(minArchetypeCapacity)`
  and `new EntityStore(pidType, minArchetypeCapacity)` set the capacity every archetype of that
  store reserves and the step it grows and shrinks by; `EntityStoreBase.MinArchetypeCapacity`
  reads it back. It must be a power of two `>= 64` — capacities are reached by doubling and by
  rounding up to a power of two, and below 64 the `Chunk<T>.AsSpan512` padding would read past
  the component array.

  Stores built with the existing constructors are unchanged: the floor defaults to
  `ArchetypeUtils.MinCapacity` (512). A low floor pays off when a process runs many stores that
  each hold few entities per archetype — a store keeps every archetype it has touched for its own
  life, so an archetype holding one entity still reserves the floor. A store with many entities
  per archetype should keep the default and pay fewer array copies.

## 4.0.0-fuse.1

Breaking release. Removes seven built-in component types and the query marker
attributes, moves child entity storage out of the archetype, and fixes 30 defects —
7 introduced by this fork, 23 inherited from upstream.

### Removed

Built-in components. Declare these in your own project instead; they carried no
engine behaviour.

| Removed | Replacement |
| --- | --- |
| `Position`, `Rotation`, `Scale3`, `Transform` | declare your own `IComponent` structs |
| `EntityName` | declare your own |
| `UniqueEntity` and `EntityStore.Find*` | declare your own component and index it |
| `TreeNode` | `Entity.ChildEntities`, `Entity.ChildIds`, `Entity.ChildCount` |

Query marker attributes — `AllComponents`, `AnyComponents`, `AllTags`, `AnyTags`,
`WithoutAllComponents`, `WithoutAnyComponents`, `WithoutAllTags`, `WithoutAnyTags`.
They existed for the upstream source generator, which this package does not ship.

The `Disabled` tag and `Unresolved` component are retained.

### Changed

- **`AddChild` is no longer a structural change.** Child ids live in a store side
  table instead of a `TreeNode` component, so adding a child does not move the
  parent to another archetype. Component references (`ref T`) held across an
  `AddChild` stay valid, no component-added event fires, and `AddChild` is safe to
  call while iterating a query over the parent's archetype.
- **`foreach` over `ChildEntities` visits every child when the loop deletes the
  current one.** Removed children shift the remainder left; the enumerator now
  steps back instead of skipping.
- **A component key whose JSON value is an array is an error.** It previously
  landed silently in `Unresolved`.
- **Duplicate ids in `DataEntity.children` throw** `InvalidOperationException`
  instead of `IndexOutOfRangeException` from the diff.
- **`InsertChild` clamps the index** when a `ChildEntitiesChanged` handler shrinks
  the target parent, instead of half-applying the move.
- **Serializing a link target that is not alive keeps its id.** Only a stale
  revision now means the target was deleted, so two link relations to distinct
  missing targets no longer collapse into one.

### Added

- `OnRelationChanged` event and `Entity.ClearRelations`. Relations are not
  components, so they get their own event type rather than riding
  `ComponentChanged`.

### Fixed

Entity tree:

- `ChildEnumerator` snapshotted the `IdArray`, so mutating children mid-`foreach`
  could yield another entity's ids.
- Deserializing an entity with a shorter or empty children list kept the dropped
  children attached and pointing at the old parent — the result of a load depended
  on whether a `ChildEntitiesChanged` handler happened to be subscribed.
- `InsertChild` moving an existing child to `index == ChildCount` threw after the
  removal, leaving the child detached but still pointing at the parent.
- `RemoveTreeParent` wrote out of bounds of `parentMap`.
- Walks up the tree looped forever when a handler closed a parent cycle.
- The child id span was not copied before handlers could refill the shared buffer.

Component index and relations:

- A null indexed component value left `EntityNode.isOwner` clear, so
  `DeleteEntity()` skipped the index. The row outlived the entity and matched the
  next entity given its id.
- `RemoveEntityRelations` skipped the version bump, so relation snapshots outlived
  their owner.
- `RemoveIncomingLinks` left a stale `isLinked` bit.

Schema:

- The `EntityNode` owner mask is a `long`, so more than 31 indexed component and
  relation types can be registered. Schemas that exceed the limit are rejected
  instead of silently corrupting entity deletion.
- `BitSet` is four 64-bit words and C# masks the shift count, so a component type
  past index 255 silently aliased onto another type's bit and archetype lookup
  returned a null heap. Such schemas are now rejected.

Serialization:

- `ReadRawComponents` returned on the first relation array, dropping every
  component after it.
- `DataEntitySerializer` traversed only object members, so `DataEntity.DebugJSON`
  returned an error string instead of JSON for every entity holding a relation.

### Infrastructure

- Test projects and `Engine.Hub` target `net10.0`.
- NativeAOT tests create the schema and run in CI. Three of them never called
  `CreateSchema()`, and `dotnet test` uses VSTest, which does not discover that
  project — so the failures were invisible.
- 18 regression tests added, each verified to fail at the commit that introduced
  the defect it covers.

### Known gaps

Two cases need process isolation to test (see `src/Tests-OwnerMask` for the
pattern) and are not covered:

- NativeAOT `CreateSchema` retry, which needs 64 indexed types.
- A schema with more than 255 component types.

## 3.6.0-fuse.1

First release of the fork, as `CoFuse.Engine.ECS`.

- Widened the `EntityNode` owner mask to `long` so more than 31 indexed component
  and relation types can be registered, and rejected schemas that exceed the limit
  instead of silently corrupting entity deletion.
- Renamed the package from `Friflo.Engine.ECS`.

Based on upstream `8e75b6b4`.
