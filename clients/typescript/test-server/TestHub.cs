using Microsoft.AspNetCore.SignalR;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LsMsgPackTsTestServer
{
  /// <summary>
  /// The hub of clients/typescript/test/signalr.test.mjs ("/hub", both protocols of LsMsgPack.SignalR): values out (with type ids, every kind), values back, streams both ways, calls to the client.
  /// The *Json methods return what the server holds as System.Text.Json (the names of the classes), the expected objects.
  /// </summary>
  public class TestHub : Hub
  {
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions() { PropertyNamingPolicy = null };

    public Invoice[] Invoices(int count) => Data.Invoices(count);

    public string InvoicesJson(int count) => JsonSerializer.Serialize(Data.Invoices(count), Json);

    public Invoice[] EchoInvoices(Invoice[] invoices) => invoices;

    public string EchoInvoicesJson(Invoice[] invoices) => JsonSerializer.Serialize(invoices, Json);

    public Zoo Zoo() => new Zoo()
    {
      Title = "zoo",
      Animals = new List<Animal>() { new Dog() { Name = "Rex", Barks = 3 }, new Cat() { Name = "Tom", Indoor = true } },
      Favorite = new Cat() { Name = "Kit" },
      Anything = new Dog() { Name = "Any", Barks = 1 },
    };

    /// <summary>
    /// The zoo the client sends back (type ids as $type): the classes the server made, as text.
    /// </summary>
    public string EchoZoo(Zoo zoo)
    {
      List<string> animals = new List<string>();
      foreach (Animal animal in zoo.Animals)
        animals.Add(animal.GetType().Name + ":" + animal.Name);
      return string.Join(",", animals) + ";" + zoo.Favorite?.GetType().Name + ";" + zoo.Anything?.GetType().Name;
    }

    public Kinds Kinds() => Data.KindValues();

    public async IAsyncEnumerable<int> Count(int count, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      for (int i = 0; i < count; i++)
      {
        await Task.Yield();
        yield return i;
      }
    }

    public async Task<int> Sum(IAsyncEnumerable<int> numbers)
    {
      int sum = 0;
      await foreach (int n in numbers)
        sum += n;
      return sum;
    }

    public Task Notify(int number) => Clients.Caller.SendAsync("Notified", Data.Invoices(1)[0], number);

    /// <summary>
    /// A client result: the server asks the client and returns its answer.
    /// </summary>
    public async Task<string> Ask(string question)
    {
      Address answer = await Clients.Caller.InvokeAsync<Address>("Answer", question, CancellationToken.None);
      return answer.City + "/" + answer.Street;
    }
  }
}
