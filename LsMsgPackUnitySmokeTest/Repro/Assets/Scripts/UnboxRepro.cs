using System;
using System.Runtime.CompilerServices;
using UnityEngine;

// IL2CPP: "Attempt to access method info data ... for TTarget with Type when no method info would be passed in" on LsMsgPack's StructPropReader<TTarget, TValue>.Read(object).
// The shape of that class, one variant per define (UNBOX, STRONGBOX, CAST).
namespace UnboxRepro
{
  public struct Plain { public int A; public decimal B; }
  public readonly struct Frozen { public Frozen(decimal a, string b) { A = a; B = b; } public decimal A { get; } public string B { get; } }

  public delegate TValue Getter<TTarget, TValue>(ref TTarget target);

  public abstract class PropReader { public abstract object Read(object target); }
  public abstract class RefPropReader<TTarget> : PropReader { internal abstract object Read(ref TTarget target); }

  public sealed class StructPropReader<TTarget, TValue> : RefPropReader<TTarget> where TTarget : struct
  {
    private readonly Getter<TTarget, TValue> _get;
    public StructPropReader(Getter<TTarget, TValue> get) { _get = get; }

    public override object Read(object target)
    {
#if UNBOX
      return Read(ref Unsafe.Unbox<TTarget>(target));
#elif STRONGBOX
      return Read(ref Unsafe.As<StrongBox<TTarget>>(target).Value);
#else
      TTarget copy = (TTarget)target;
      return Read(ref copy);
#endif
    }

    internal override object Read(ref TTarget target) { return _get(ref target); }
  }

  public static class Runner
  {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Run()
    {
      if (Application.isEditor) return;
      string result;
      try
      {
        object plain = new Plain { A = 1, B = 2.5m };
        object frozen = new Frozen(3.5m, "x");
#if ONLY_PLAIN // which structs: a mutable one of an int and a decimal
        PropReader[] readers = { new StructPropReader<Plain, decimal>((ref Plain p) => p.B), new StructPropReader<Plain, int>((ref Plain p) => p.A) };
        result = readers[0].Read(plain) + " " + readers[1].Read(plain);
#elif ONLY_VECTOR3 // System.Numerics.Vector3 (LsMsgPack's StructPropWriter<Vector3, float, BoxedFilter> failed)
        PropReader reader = new StructPropReader<System.Numerics.Vector3, float>((ref System.Numerics.Vector3 v) => v.Y);
        result = reader.Read(new System.Numerics.Vector3(1, 2, 3)).ToString();
#else
        PropReader[] readers = { new StructPropReader<Plain, decimal>((ref Plain p) => p.B), new StructPropReader<Plain, int>((ref Plain p) => p.A),
          new StructPropReader<Frozen, decimal>((ref Frozen f) => f.A), new StructPropReader<Frozen, string>((ref Frozen f) => f.B) };
        result = readers[0].Read(plain) + " " + readers[1].Read(plain) + " " + readers[2].Read(frozen) + " " + readers[3].Read(frozen);
#endif
      }
      catch (Exception ex) { result = "FAILED " + ex; }
      Debug.Log("[repro] " + result);
      System.IO.File.WriteAllText(Environment.GetCommandLineArgs()[Array.IndexOf(Environment.GetCommandLineArgs(), "-out") + 1], result);
      Application.Quit(0);
    }
  }
}
