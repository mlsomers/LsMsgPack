using Microsoft.Data.Sqlite;
using System;

namespace LsMsgPack.DataTests
{
  internal static class Sqlite
  {
    internal static void Execute(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
      using (SqliteCommand command = new SqliteCommand(sql, connection))
      {
        foreach ((string name, object value) in parameters)
          command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
      }
    }
  }
}
