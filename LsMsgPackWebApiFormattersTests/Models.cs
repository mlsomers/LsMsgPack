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

  public class Order
  {
    public int Id { get; set; }
    public string Customer { get; set; }
    public double[] Amounts { get; set; }
  }
}
