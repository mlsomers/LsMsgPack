using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;

namespace LsMsgPack.SignalRTests
{
  public enum Mood
  {
    Calm,
    Happy,
    Angry
  }

  public class Point
  {
    public int X { get; set; }
    public int Y { get; set; }
  }

  public class ChatMessage
  {
    public string User { get; set; }
    public string Text { get; set; }
    public DateTime Sent { get; set; }
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public Mood Mood { get; set; }
    public List<string> Tags { get; set; }
    public Point Where { get; set; }

    public static ChatMessage Sample(int n = 1)
    {
      return new ChatMessage()
      {
        User = "user" + n,
        Text = "Hello wörld " + n,
        Sent = new DateTime(2026, 11, 19, 12, 30, n % 60, DateTimeKind.Utc),
        Id = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e"),
        Amount = 12.34m + n,
        Mood = Mood.Happy,
        Tags = new List<string>() { "a", "b" },
        Where = new Point() { X = n, Y = -n }
      };
    }
  }

  public class Shape
  {
    public string Name { get; set; }
  }

  public class Circle : Shape
  {
    public double Radius { get; set; }
  }

  /// <summary>
  /// The types of the targets, results and stream items by name, as a hub or a client knows them.
  /// </summary>
  public sealed class TestBinder : IInvocationBinder
  {
    public readonly Dictionary<string, Type[]> Targets = new Dictionary<string, Type[]>();
    public readonly Dictionary<string, Type> Results = new Dictionary<string, Type>();
    public readonly Dictionary<string, Type> StreamItems = new Dictionary<string, Type>();

    public IReadOnlyList<Type> GetParameterTypes(string methodName)
    {
      if (!Targets.TryGetValue(methodName, out Type[] types))
        throw new InvalidOperationException($"No method {methodName}");
      return types;
    }

    public Type GetReturnType(string invocationId)
    {
      if (!Results.TryGetValue(invocationId, out Type type))
        throw new InvalidOperationException($"No invocation {invocationId}");
      return type;
    }

    public Type GetStreamItemType(string streamId)
    {
      if (!StreamItems.TryGetValue(streamId, out Type type))
        throw new InvalidOperationException($"No stream {streamId}");
      return type;
    }
  }
}
