using System.ComponentModel.DataAnnotations;

namespace LsMsgPackMvcTests
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

    [Required]
    public string Customer { get; set; }

    [Range(0, 1000)]
    public int Quantity { get; set; }

    public double[] Amounts { get; set; }
  }
}
