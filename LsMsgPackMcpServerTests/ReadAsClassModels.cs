using System.Collections.Generic;

namespace LsMsgPackMcpServerTests.ReadAsWriter
{
  public class McpOrderDto
  {
    public int Id { get; set; }
    public string Customer { get; set; }
    public string Discount { get; set; }
    public McpAddressDto Address { get; set; }
    public List<McpLineDto> Lines { get; set; }
  }

  public class McpAddressDto
  {
    public string Street { get; set; }
  }

  public class McpLineDto
  {
    public string Product { get; set; }
    public int Quantity { get; set; }
  }
}

namespace LsMsgPackMcpServerTests.ReadAsReader
{
  /// <summary>
  /// The writer's order with Address misspelled and Discount as a number (the data has text).
  /// </summary>
  public class McpOrderEntity
  {
    public int Id { get; set; }
    public string Customer { get; set; }
    public int Discount { get; set; }
    public McpAddressEntity Adress { get; set; }
    public List<McpLineEntity> Lines { get; set; }
  }

  public class McpAddressEntity
  {
    public string Street { get; set; }
  }

  public class McpLineEntity
  {
    public string Product { get; set; }
    public int Quantity { get; set; }
  }
}
