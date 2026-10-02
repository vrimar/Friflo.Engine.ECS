// Copyright (c) Ullrich Praetz - https://github.com/friflo. All rights reserved.
// See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

// ReSharper disable once CheckNamespace
namespace Friflo.Engine.ECS.Index;

internal delegate AbstractComponentIndex CreateComponentIndex(EntityStore store, ComponentType componentType);

internal readonly struct RegisteredIndex
{
    internal readonly   Type                    indexType;
    internal readonly   Type                    valueType;
    internal readonly   Delegate                getIndexedValue;
    internal readonly   CreateComponentIndex    create;

    internal RegisteredIndex(Type indexType, Type valueType, Delegate getIndexedValue, CreateComponentIndex create) {
        this.indexType          = indexType;
        this.valueType          = valueType;
        this.getIndexedValue    = getIndexedValue;
        this.create             = create;
    }
}

internal static class ComponentIndexUtils
{
    internal static readonly Dictionary<Type, RegisteredIndex> RegisteredIndexes = new ();
    
    internal static void Register<T, TValue>(Type indexType, CreateComponentIndex create)
        where T : struct, IIndexedComponent<TValue>
    {
        GetIndexedValue<T, TValue> getIndexedValue = IndexedValueUtils.GetIndexedComponentValue<T, TValue>;
        RegisteredIndexes[typeof(T)] = new RegisteredIndex(indexType, typeof(TValue), getIndexedValue, create);
    }
    
    /// Call constructors of<br/>
    /// <see cref="ValueStructIndex{TIndexedComponent,TValue}"/>
    /// <see cref="ValueClassIndex{TIndexedComponent,TValue}"/>
    /// <see cref="EntityIndex{TIndexedComponent}"/>
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2080", Justification = "TODO")] // TODO
    internal static AbstractComponentIndex CreateComponentIndex(EntityStore store, ComponentType componentType)
    {
        var flags   = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.CreateInstance;
        var paramTypes = new [] { typeof(EntityStore), typeof(ComponentType) };
        var constructor = componentType.IndexType.GetConstructor(flags, null, paramTypes, null);
        if (constructor == null) {
            // constructor is null in Native AOT
            if (!RegisteredIndexes.TryGetValue(componentType.Type, out var registered)) {
                throw new InvalidOperationException($"Native AOT requires registration of IIndexedComponent with aot.RegisterIndexedComponent(). type: {componentType.Type}.");   
            }
            return registered.create(store, componentType);
        }
        var args    = new object[] { store, componentType };
        var obj     = constructor.Invoke(args);
        var index   = (AbstractComponentIndex)obj!;
        return index;
    }
    
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2070", Justification = "TODO")] // TODO
    internal static Type GetIndexType(Type componentType, out Type valueType)
    {
        // Native AOT drops IIndexedComponent<> from the interface map when nothing casts to it
        if (RegisteredIndexes.TryGetValue(componentType, out var registered)) {
            valueType = registered.valueType;
            return registered.indexType;
        }
        var interfaces = componentType.GetInterfaces();
        foreach (var i in interfaces)
        {
            if (!i.IsGenericType) continue;
            var genericType = i.GetGenericTypeDefinition();
            if (genericType != typeof(IIndexedComponent<>)) {
                continue;
            }
            valueType = i.GenericTypeArguments[0];
            return MakeIndexType(valueType, componentType);
        }
        valueType = null;
        return null;
    }
    
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2055", Justification = "TODO")] // TODO
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2065", Justification = "TODO")] // TODO
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2070", Justification = "TODO")] // TODO
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL3050", Justification = "TODO")] // TODO
    private static Type MakeIndexType(Type valueType, Type componentType)
    {
        if (valueType == typeof(Entity)) {
            return typeof(EntityIndex<>).MakeGenericType(new [] { componentType });
        }
        var indexType   = GetComponentIndex(componentType);
        var typeArgs    = new [] { componentType, valueType };
        if (indexType != null) {
            return indexType.                 MakeGenericType(typeArgs);
        }
        if (valueType.IsClass) {
            return typeof(ValueClassIndex<,>).MakeGenericType(typeArgs);
        }
        return typeof(ValueStructIndex<,>).   MakeGenericType(typeArgs);
    }
    
    private static Type GetComponentIndex(Type type)
    {
        foreach (var attr in type.CustomAttributes) {
            if (attr.AttributeType != typeof(ComponentIndexAttribute)) {
                continue;
            }
            var arg = attr.ConstructorArguments;
            return (Type) arg[0].Value;
        }
        return null;
    }
}

