#:package DuckDB.NET.Data.Full@1.5.3

using DuckDB.NET.Data;

// Feasibility spike: can the agent_data community extension install + load in-process
// through DuckDB.NET 1.5.3 (the version Kurrent.Kontext pins)?
using var connection = new DuckDBConnection("DataSource=:memory:");
connection.Open();

using var version = connection.CreateCommand();
version.CommandText = "PRAGMA version;";
using (var reader = version.ExecuteReader()) {
    reader.Read();
    Console.WriteLine($"engine: {reader.GetString(0)}");
}

using var command = connection.CreateCommand();
command.CommandText =
    """
    INSTALL agent_data FROM community;
    LOAD agent_data;
    SELECT count(*)                                          AS msgs,
           count(DISTINCT session_id)                        AS sessions,
           count(*) FILTER (message_type <> '_parse_error')  AS parsed
    FROM read_conversations();
    """;

using var results = command.ExecuteReader();
do {
    if (!results.Read())
        continue;

    for (var i = 0; i < results.FieldCount; i++)
        Console.WriteLine($"{results.GetName(i)}: {results.GetValue(i)}");
} while (results.NextResult());

Console.WriteLine("IN-PROCESS LOAD: OK");
