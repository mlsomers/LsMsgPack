using System;

namespace LsMsgPack
{
  /// <summary>
  /// Values that could not be read, thrown once all data was read with <see cref="ReadErrorHandling.FailDeferred"/>. <see cref="Differences"/> has all of them (<see cref="Difference.IsError"/>),
  /// the other differences and the paths of the objects; <see cref="Exception.InnerException"/> is the first error.
  /// </summary>
  public class ReadErrorsException : MsgPackException
  {
    internal ReadErrorsException(ReadDifferences differences, Exception first)
      : base(Message(differences), first)
    {
      Differences = differences;
      Data[ReadDifferences.ExceptionDataKey] = differences;
    }

    public ReadDifferences Differences { get; }

    private static string Message(ReadDifferences differences)
    {
      int errors = differences.ErrorCount;
      return string.Concat(errors == 1 ? "1 value" : string.Concat(errors.ToString(System.Globalization.CultureInfo.InvariantCulture), " values"),
        " could not be read (ReadErrors = FailDeferred). ", differences.GenerateReport());
    }
  }
}
