using System;

// The members of Unity's types that LsMsgPack's rules look at (UnityEngine.CoreModule 6000.6): public fields as the state, computed and settable properties, private fields.
namespace UnityEngine
{
  /// <summary>
  /// State in public fields; <c>normalized</c> is another Vector3 (written as a property it never ended), an indexer, static properties.
  /// </summary>
  public struct Vector3 : IEquatable<Vector3>
  {
    public float x;
    public float y;
    public float z;

    public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

    public float this[int index]
    {
      get { return index == 0 ? x : index == 1 ? y : z; }
      set { if (index == 0) x = value; else if (index == 1) y = value; else z = value; }
    }

    public float magnitude { get { return (float)Math.Sqrt(x * x + y * y + z * z); } }

    public Vector3 normalized { get { float m = magnitude; return m > 1E-05f ? new Vector3(x / m, y / m, z / m) : zero; } }

    public static Vector3 zero { get { return default(Vector3); } }

    public static Vector3 one { get { return new Vector3(1, 1, 1); } }

    public bool Equals(Vector3 other) { return x == other.x && y == other.y && z == other.z; }

    public override bool Equals(object other) { return other is Vector3 v && Equals(v); }

    public override int GetHashCode() { return x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2); }

    public override string ToString() { return "(" + x + ", " + y + ", " + z + ")"; }
  }

  /// <summary>
  /// State in public fields; <c>eulerAngles</c> has a setter too (computed from the fields), <c>normalized</c> is another Quaternion.
  /// </summary>
  public struct Quaternion : IEquatable<Quaternion>
  {
    public float x;
    public float y;
    public float z;
    public float w;

    public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }

    public Vector3 eulerAngles
    {
      get { return new Vector3(x * 2, y * 2, z * 2); }
      set { x = value.x / 2; y = value.y / 2; z = value.z / 2; w = 1; }
    }

    public Quaternion normalized { get { return this; } }

    public bool Equals(Quaternion other) { return x == other.x && y == other.y && z == other.z && w == other.w; }

    public override bool Equals(object other) { return other is Quaternion q && Equals(q); }

    public override int GetHashCode() { return x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode() ^ w.GetHashCode(); }
  }

  /// <summary>
  /// State in public fields; <c>linear</c> and <c>gamma</c> are other Colors.
  /// </summary>
  public struct Color : IEquatable<Color>
  {
    public float r;
    public float g;
    public float b;
    public float a;

    public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }

    public Color linear { get { return new Color(r * r, g * g, b * b, a); } }

    public Color gamma { get { return new Color((float)Math.Sqrt(r), (float)Math.Sqrt(g), (float)Math.Sqrt(b), a); } }

    public float grayscale { get { return 0.299f * r + 0.587f * g + 0.114f * b; } }

    public bool Equals(Color other) { return r == other.r && g == other.g && b == other.b && a == other.a; }

    public override bool Equals(object other) { return other is Color c && Equals(c); }

    public override int GetHashCode() { return r.GetHashCode() ^ g.GetHashCode() ^ b.GetHashCode() ^ a.GetHashCode(); }
  }

  /// <summary>
  /// State in private fields behind settable properties (not a struct of public fields): written by x and y, its computed properties and private fields left out.
  /// </summary>
  public struct Vector2Int : IEquatable<Vector2Int>
  {
    private int m_X;
    private int m_Y;

    public Vector2Int(int x, int y) { m_X = x; m_Y = y; }

    public int x { get { return m_X; } set { m_X = value; } }

    public int y { get { return m_Y; } set { m_Y = value; } }

    public float magnitude { get { return (float)Math.Sqrt(x * x + y * y); } }

    public int sqrMagnitude { get { return x * x + y * y; } }

    public bool Equals(Vector2Int other) { return m_X == other.m_X && m_Y == other.m_Y; }

    public override bool Equals(object other) { return other is Vector2Int v && Equals(v); }

    public override int GetHashCode() { return m_X ^ (m_Y << 2); }
  }

  /// <summary>
  /// Unity's serializer writes the private fields that have it (and the public ones).
  /// </summary>
  [AttributeUsage(AttributeTargets.Field)]
  public sealed class SerializeField : Attribute
  {
  }
}
