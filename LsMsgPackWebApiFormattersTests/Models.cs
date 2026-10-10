namespace LsMsgPackWebApiFormattersTests
{
  public abstract class Animal
  {
    public string Name { get; set; }
  }

  public class Dog : Animal
  {
    public int Barks { get; set; }
  }

  /// <summary>
  /// An Order of another version: with a property Order does not have.
  /// </summary>
  public class OrderWithExtra
  {
    public int Id { get; set; }
    public string Customer { get; set; }
    public string Extra { get; set; }
  }

  public class Order
  {
    public int Id { get; set; }
    public string Customer { get; set; }
    public double[] Amounts { get; set; }
  }
}
