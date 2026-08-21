using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Friflo.Engine.ECS;
using Friflo.Json.Fliox;
using static System.Diagnostics.DebuggerBrowsableState;
using Browse = System.Diagnostics.DebuggerBrowsableAttribute;

// ReSharper disable RedundantTypeDeclarationBody
namespace Tests.ECS {

[ComponentKey("name")]
[ComponentSymbol("N", "0,0,0")]
public struct EntityName : IComponent
{
    public  string  value;

    [Browse(Never)] public  byte[]  Utf8 => value == null ? null : Encoding.UTF8.GetBytes(value);

    public override         string  ToString() => $"'{value}'";

    public EntityName (string value) {
        this.value = value;
    }
}

[ComponentKey("pos")]
[StructLayout(LayoutKind.Explicit)]
[ComponentSymbol("P",  "0, 170, 0")]
public struct  Position : IComponent, IEquatable<Position>
{
    [Browse(Never)]
    [Ignore]
    [FieldOffset(0)] public     Vector3 value;
    [FieldOffset(0)] public     float   x;
    [FieldOffset(4)] public     float   y;
    [FieldOffset(8)] public     float   z;

    public readonly override string ToString() => $"{x}, {y}, {z}";

    public Position (float x, float y, float z) {
        value = default;
        this.x = x;
        this.y = y;
        this.z = z;
    }
    public          bool    Equals      (Position other)                    => value == other.value;
    public static   bool    operator == (in Position p1, in Position p2)    => p1.value == p2.value;
    public static   bool    operator != (in Position p1, in Position p2)    => p1.value != p2.value;

    [ExcludeFromCodeCoverage] public override   int     GetHashCode()       => throw new NotImplementedException("to avoid boxing");
    [ExcludeFromCodeCoverage] public override   bool    Equals(object obj)  => throw new NotImplementedException("to avoid boxing");
}

[ComponentKey("rot")]
[StructLayout(LayoutKind.Explicit)]
[ComponentSymbol("Rℍ")] // ℍ = Hamilton
public struct  Rotation : IComponent, IEquatable<Rotation>
{
    [Browse(Never)]
    [Ignore]
    [FieldOffset (0)] public    Quaternion  value;
    [FieldOffset (0)] public    float       x;
    [FieldOffset (4)] public    float       y;
    [FieldOffset (8)] public    float       z;
    [FieldOffset(12)] public    float       w;

    public readonly override string ToString() => $"{x}, {y}, {z}, {w}";

    public Rotation (float x, float y, float z, float w) {
        value = default;
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }
    public          bool    Equals      (Rotation other)                    => value == other.value;
    public static   bool    operator == (in Rotation p1, in Rotation p2)    => p1.value == p2.value;
    public static   bool    operator != (in Rotation p1, in Rotation p2)    => p1.value != p2.value;

    [ExcludeFromCodeCoverage] public override   int     GetHashCode()       => throw new NotImplementedException("to avoid boxing");
    [ExcludeFromCodeCoverage] public override   bool    Equals(object obj)  => throw new NotImplementedException("to avoid boxing");
}

[ComponentKey("scl3")]
[StructLayout(LayoutKind.Explicit)]
public struct Scale3 : IComponent, IEquatable<Scale3>
{
    [Browse(Never)]
    [Ignore]
    [FieldOffset(0)] public     Vector3 value;
    [FieldOffset(0)] public     float   x;
    [FieldOffset(4)] public     float   y;
    [FieldOffset(8)] public     float   z;

    public readonly override string ToString() => $"{x}, {y}, {z}";

    public Scale3 (float x, float y, float z) {
        value = default;
        this.x = x;
        this.y = y;
        this.z = z;
    }
    public          bool    Equals      (Scale3 other)                  => value == other.value;
    public static   bool    operator == (in Scale3 p1, in Scale3 p2)    => p1.value == p2.value;
    public static   bool    operator != (in Scale3 p1, in Scale3 p2)    => p1.value != p2.value;

    [ExcludeFromCodeCoverage] public override   int     GetHashCode()       => throw new NotImplementedException("to avoid boxing");
    [ExcludeFromCodeCoverage] public override   bool    Equals(object obj)  => throw new NotImplementedException("to avoid boxing");
}

[ComponentKey("trans")]
[StructLayout(LayoutKind.Explicit)]
public struct  Transform : IComponent
{
    [Browse(Never)]
    [Ignore]
    [FieldOffset (0)] public    Matrix4x4   value;

    [FieldOffset (0)] public    float       m11;
    [FieldOffset (4)] public    float       m12;
    [FieldOffset (8)] public    float       m13;
    [FieldOffset(12)] public    float       m14;

    [FieldOffset(16)] public    float       m21;
    [FieldOffset(20)] public    float       m22;
    [FieldOffset(24)] public    float       m23;
    [FieldOffset(28)] public    float       m24;

    [FieldOffset(32)] public    float       m31;
    [FieldOffset(36)] public    float       m32;
    [FieldOffset(40)] public    float       m33;
    [FieldOffset(44)] public    float       m34;

    [FieldOffset(48)] public    float       m41;
    [FieldOffset(52)] public    float       m42;
    [FieldOffset(56)] public    float       m43;
    [FieldOffset(60)] public    float       m44;
}


public static class ChunkExtensions
{
    public static Span<Vector3>     AsSpanVector3   (this Span <Position>  position)    => MemoryMarshal.Cast<Position, Vector3>    (position);
    public static Span<Vector3>     AsSpanVector3   (this Chunk<Position>  position)    => MemoryMarshal.Cast<Position, Vector3>    (position   .Span);

    public static Span<Quaternion>  AsSpanQuaternion(this Span <Rotation>  rotation)    => MemoryMarshal.Cast<Rotation, Quaternion> (rotation);
    public static Span<Quaternion>  AsSpanQuaternion(this Chunk<Rotation>  rotation)    => MemoryMarshal.Cast<Rotation, Quaternion> (rotation   .Span);

    public static Span<Vector3>     AsSpanVector3   (this Span <Scale3>    scale)       => MemoryMarshal.Cast<Scale3,   Vector3>    (scale);
    public static Span<Vector3>     AsSpanVector3   (this Chunk<Scale3>    scale)       => MemoryMarshal.Cast<Scale3,   Vector3>    (scale      .Span);

    public static Span<Matrix4x4>   AsSpanMatrix4x4 (this Span <Transform> transform)   => MemoryMarshal.Cast<Transform,Matrix4x4>  (transform);
    public static Span<Matrix4x4>   AsSpanMatrix4x4 (this Chunk<Transform> transform)   => MemoryMarshal.Cast<Transform,Matrix4x4>  (transform  .Span);
}

}
