using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace LtMsgPack.DataTests
{
  /// <summary>
  /// A property per column of <see cref="Data.AllTypes"/>, Nullable for the value types: the class whose List the rows are written as.
  /// </summary>
  public class AllTypesRow
  {
    public int? Id { get; set; }
    public string Name { get; set; }
    public decimal? Amount { get; set; }
    public DateTime? Created { get; set; }
    public Guid? Key { get; set; }
    public bool? Active { get; set; }
    public double? Score { get; set; }
    public long? Big { get; set; }
    public byte[] Data { get; set; }
    public short? Small { get; set; }
    public float? Ratio { get; set; }
    public byte? Tiny { get; set; }
  }

  /// <summary>
  /// The default entry name of the rows (DataReaderExtensions.DefaultTypeName), with two of the columns of AllTypes.
  /// </summary>
  public class Row
  {
    public long? Big { get; set; }
    public string Name { get; set; }
  }

  /// <summary>
  /// Created by its constructor (no setters for Id and Name): the mapper collects the values first.
  /// </summary>
  public sealed class ImmutableRow
  {
    public ImmutableRow(int id, string name)
    {
      Id = id;
      Name = name;
    }

    public int Id { get; }
    public string Name { get; }
    public double? Score { get; set; }
  }

  public enum Status { None = 0, Open = 1, Paid = 2 }

  public class InvoiceRow
  {
    public int Id { get; set; }
    public string Number { get; set; }
    public string Customer { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public decimal Vat { get; set; }
    public bool Paid { get; set; }
    public Guid Reference { get; set; }
    public int Lines { get; set; }
    public string Note { get; set; }
  }

  public class MappedBase
  {
    public int Id { get; set; }
  }

  /// <summary>
  /// Names that differ from the columns: [Column], case, underscores; [NotMapped]; a constructor value; an enum from an integer; an inherited property.
  /// </summary>
  public class MappedRow : MappedBase
  {
    [Column("customer_name")]
    public string Customer { get; set; }

    public string FirstName { get; set; }

    public int? Count { get; set; }

    public string Comment { get; set; } = "none";

    public Status Status { get; set; }

    [NotMapped]
    public string Extra { get; set; }

    public long Total { get; set; }
  }
}
