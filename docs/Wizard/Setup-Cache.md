[Setup wizard](README.md) › [Saved](Files.md) › [Only my own app](FilesOwnApp.md) › Thrown away

# Setup: caches and temporary files

Data your .NET app stores for a short while and can always recreate: a distributed cache (Redis, `IDistributedCache`), an output cache, temp files, a memoized result on disk.

## In short

| | |
|---|---|
| Package | `LtMsgPack` (plus the source generator in it, for speed) |
| Serializer | `LtMsgPackSerializer`: the defaults, or without the schema for the smallest entries |
| Keep in mind | put a version of your classes in the cache key |

```
dotnet add package LtMsgPack
```

## Two choices

### A. The defaults (safe, recommended to start with)

```csharp
using LtMsgPack;

LtMsgPackSerializer serializer = new LtMsgPackSerializer(); // keep one for the whole app
```

Every entry carries a small schema with the property names of its classes, so a new version of your app reads entries an old version wrote (and the other way around, during a deploy where old and new servers share the cache). Nothing to think about.

### B. Without the schema (smallest and fastest)

```csharp
LtMsgPackSerializer serializer = new LtMsgPackSerializer(new LtMsgPackOptions
{
  UseInexedSchema = false // objects are only their values, in the order of their properties
});
```

An object is then written as a plain list of its values: `["Ann", 42, true]` instead of a table of names plus the values. For a cache with many small entries (a user profile, a price), the schema of choice A can be larger than the data itself, so B saves a good part of the size and some time.

The price: the reader matches the values to properties **by position**. If you add a property in the middle of a class and deploy, the new version reads `42` from an old entry into the wrong property, without an error. So B is only safe together with the versioned key below.

## The cache key: never read another version's entries

Put something in the key that changes when your classes change. Then a new version simply doesn't find the old entries (a cache miss) and recreates them:

```csharp
using Microsoft.Extensions.Caching.Distributed;

public class OrderCache
{
  // Changes whenever the assembly with the classes is rebuilt from changed code, and is the same on every
  // server running the same build (SDK projects build deterministically).
  private static readonly string Version = typeof(Order).Assembly.ManifestModule.ModuleVersionId.ToString("N").Substring(0, 8);

  private readonly IDistributedCache _cache;
  private readonly LtMsgPackSerializer _serializer;

  public OrderCache(IDistributedCache cache, LtMsgPackSerializer serializer)
  {
    _cache = cache;
    _serializer = serializer;
  }

  public Task SetAsync(Order order) =>
    _cache.SetAsync($"order:{Version}:{order.Id}", _serializer.Serialize(order),
      new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });

  public async Task<Order> GetAsync(int id)
  {
    byte[] data = await _cache.GetAsync($"order:{Version}:{id}");
    return data == null ? null : _serializer.Deserialize<Order>(data);
  }
}
```

A hand-written version number (`"order:v7:..."`) works too, if you remember to raise it. With choice A the version is optional: old entries are still read correctly, the version only stops you from using stale *content* after a change in how it's computed.

Register the serializer once: `builder.Services.AddSingleton(new LtMsgPackSerializer());`.

## More speed: the source generator

LtMsgPack contains a source generator that writes the serialization code of your classes at compile time. For a cache that's hit on every request, that makes writing about 1.6x as fast again:

```csharp
[LtMsgPackSerializable(typeof(Order))]
[LtMsgPackSerializable(typeof(UserProfile))]
public partial class CacheMsgPackContext : LtMsgPackContext { }

LtMsgPackSerializer serializer = new LtMsgPackSerializer(CacheMsgPackContext.Default, new LtMsgPackOptions { UseInexedSchema = false });
```

The bytes are the same with or without it. See [Add-ons](Extras.md#trimmed-and-aot-apps-and-more-speed).

## Watch out

- **Temp files in a shared folder** (`/tmp`, a network share) can be written by others: treat them as untrusted, see [Add-ons](Extras.md#reading-data-you-dont-trust). A cache only your services can write to doesn't need that.
- **Dates come back in UTC** (`Kind = Utc`), the same moment.
- Measure before choosing B: for large entries (a list of 100 orders) the schema is a small part, and A is nearly as small.

## Next

- Speed numbers of the serializers: [README](../../README.md#which-package) and [SourceGenerator.md](../SourceGenerator.md#measurements).
- What the options do: [schema.md](../schema.md).
