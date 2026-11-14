﻿using System;
using System.Runtime.CompilerServices;

namespace LsMsgPack
{
  /// <summary>
  /// Holds a struct that is the root value of a typed call (<see cref="LsMsgPackSerializer.Serialize{T}(T)"/>, <see cref="LsMsgPackSerializer.Deserialize{T}(byte[])"/>, the stream writer and reader)
  /// where the entry code passes the root as an <c>object</c>: in place of a box per call. Kept in the state a call rents per thread (Serializer.WriteState.Slot, <see cref="Reading.ReadContext.Slot"/>, so a nested call has its own),
  /// for the last struct root type: calls that alternate two struct root types make one per call, as the box did.
  /// <para>Writing: it is <see cref="Writing.WriteContext.Root"/>, read by <see cref="LsMsgPackSerializer.TypedWriter{T}"/>. Reading: the root reader fills it (<see cref="Reading.ValueReader.ReadIntoSlot"/>) and returns it instead of the boxed value
  /// (the paths that collect differences or errors and the unconverted root value return the boxed value: <see cref="RootSlot{T}.Take"/> takes either).</para>
  /// </summary>
  internal abstract class RootSlot
  {
    /// <summary>
    /// The type of a root value as the entry code got it: the type of the struct for a slot.
    /// </summary>
    internal static Type TypeOf(object root)
    {
      RootSlot slot = root as RootSlot;
      return slot is null ? root.GetType() : slot.ValueType;
    }

    internal abstract Type ValueType { get; }
  }

  internal sealed class RootSlot<T> : RootSlot
  {
    /// <summary>
    /// The root is held in a slot: structs other than <see cref="Nullable{T}"/> (whose null is a null box).
    /// The callers test <c>typeof(T).IsValueType &amp;&amp; RootSlot&lt;T&gt;.IsPlainStruct</c> in their own body: the JIT folds both per instantiation, also the first in shared code (reference types: no generic lookup),
    /// but it did not inline a helper method with that test into shared code (a call per Deserialize of a class, 3-5%).
    /// </summary>
    internal static readonly bool IsPlainStruct = typeof(T).IsValueType && Nullable.GetUnderlyingType(typeof(T)) == null;

    internal T Value;

    /// <summary>
    /// The slot kept in <paramref name="kept"/> when it is one of <typeparamref name="T"/>, otherwise a new one kept there.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static RootSlot<T> Of(ref RootSlot kept)
    {
      RootSlot<T> slot = kept as RootSlot<T>; // a sealed class: one compare of the method table
      if (slot is null)
        kept = slot = new RootSlot<T>();
      return slot;
    }

    /// <summary>
    /// The value is not kept alive.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Clear()
    {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
      if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
#endif
        Value = default(T);
    }

    /// <summary>
    /// The value a Deserialize call returned.
    /// </summary>
    /// <param name="result">A slot (its value is taken and cleared), null (nil with the indexed schema) or the boxed value (paths that do not fill the slot; a root value that could not be converted fails the cast, as before)</param>
    internal static T Take(object result)
    {
      if (result is RootSlot<T> slot)
      {
        T value = slot.Value;
        slot.Clear();
        return value;
      }
      return result is null ? default(T) : (T)result;
    }

    internal override Type ValueType { get { return typeof(T); } }
  }
}
