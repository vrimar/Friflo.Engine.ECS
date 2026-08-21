// Copyright (c) Ullrich Praetz - https://github.com/friflo. All rights reserved.
// See LICENSE file in the project root for full license information.

// ReSharper disable once CheckNamespace
namespace Friflo.Engine.ECS.Relations;

internal interface IRelationKeyStash
{
}

internal interface IRelationKeyStash<TKey> : IRelationKeyStash
{
    internal ref TKey GetKeyStash();
}
