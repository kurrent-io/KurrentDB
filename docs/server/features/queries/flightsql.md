# SQL

KurrentDB v26.1 introduces support for SQL and the [Arrow Flight SQL](https://arrow.apache.org/docs/format/FlightSql.html) protocol that can be used to execute queries on indexes. Arrow Flight SQL clients are available for the broad set of programming languages and runtimes:
* JDBC driver for Java
* Python
* ADO.NET for C#/F#
* Rust
* R
* Go
* JavaScript/NodeJS

A full set of existing implementations can be found [here](https://arrow.apache.org/docs/implementations.html).

If a high-level database driver is not available for your language, you can generate a client from the gRPC Interface Definition Language for the FlightSql protocol.

## Introduction

To access the Arrow Flight SQL API programmatically, you need to install the necessary packages or modules for your language/runtime. Then, the connection string must be configured. The FlightSQL protocol is exposed as a gRPC endpoint on the same port as the main KurrentDB gRPC API and described in [Networking](../../configuration/networking.md) article. For Python library, the example is `grpc://localhost:2113`.

Authentication as a user authorized to read the `$all` stream is required to query using the FlightSQL protocol.


## SQL dialect

KurrentDB SQL queries are powered by [DuckDB](https://duckdb.org/) which supports a [SQL dialect](https://duckdb.org/docs/lts/sql/dialect/postgresql_compatibility) similar to PostgreSQL.

Note that only **SELECT** statements are allowed. **INSERT**, **UPDATE**, stored procedures or others DDL statements are not supported.

- The default index is available through the **`kdb.records`** table.
- User-defined indexes are available through **`usr.<user-defined-index>`** tables.

### Examples

```sql
-- select records in a specific category
SELECT * FROM kdb.records WHERE category='my_category'

-- select records of a specific schema (traditionally: event type)
SELECT * FROM kdb.records WHERE schema_name = 'OrderCreated'

-- select records written in a date range
SELECT stream, stream_revision, epoch_ms(created_at) append_time
FROM kdb.records
WHERE append_time BETWEEN '2026-04-20' AND '2026-04-30'

-- a more complex query
SELECT
  SUM(cast(data::json->>'Amount' as integer)) MonthlyRequestedAmount,
  data::json->>'Address'->>'Country' Country
FROM kdb.records
WHERE schema_name = 'LoanRequested'
  AND to_timestamp(created_at/1000) BETWEEN '2026-04-01' AND '2026-04-30'
GROUP BY Country;
```

Every user-defined index has a table in the `usr` schema which can be queried. See the [user defined indexes documentation](../indexes/user-defined.md) to create an example user-defined index called `orders-by-country`. The following queries can then be run against it:

```sql
SELECT * FROM usr."orders-by-country" WHERE field_country='Mauritius'

SELECT data::json->>'total' FROM usr."orders-by-country"
  WHERE field_country='Mauritius'
    AND (data::json->>'orderId'='ORD-1234')
```

### `kdb.records` Table

List of available columns:
* `log_position` of type `INT64` - the position of the record within the log
* `created_at` of type `INT64` - Unix UTC timestamp of when the record was saved to the log
* `stream` of type `VARCHAR` - stream identifier
* `stream_revision` of type `INT64` - stream revision
* `schema_name` of type `VARCHAR` - record schema (traditionally: event type)
* `schema_id` of type `VARCHAR` - record schema identifier
* `schema_format` of type `VARCHAR` - record schema format. Typically, it's `json`
* `category` of type `VARCHAR` - record category
* `record_id` of type BLOB (16 bytes) - unique identifier of the record
* `data` of type `VARCHAR` - the payload of the record. In case of JSON, you can cast this column to `JSON` data type and use arrow navigation operators to access the inner fields. If the payload is binary it is converted to a base64 encoded string.
* `metadata` of type `VARCHAR` - the metadata of the record in the form of the JSON

### `usr.<user-defined-index>` Tables

List of available columns:
* `log_position` of type `INT64` - the position of the record within the log
* `stream` of type `VARCHAR` - stream identifier
* `stream_revision` of type `INT64` - stream revision
* `schema_name` of type `VARCHAR` - record schema (traditionally: event type)
* `created_at` of type `INT64` - Unix UTC timestamp of when the record was saved to the log
* `record_id` of type BLOB (16 bytes) - unique identifier of the record
* `data` of type `VARCHAR`
* `metadata` of type `VARCHAR`
* The custom field configured in the index

## Supported FlightSQL Features
Arrow Flight SQL is a rich protocol with many features. Its implementation in KurrentDB supports the following functionality:
* Ad-hoc query execution (`CommandStatementQuery` command)
* Prepared statements and parameters binding
* Schema discovery for queries and prepared statements

## Flight Client Examples
For simplicity, examples below assume `insecure` mode for KurrentDB node.

### Java
The following example demonstrates how to connect to KurrentDB with JDBC:
```java
package org.example;

import java.sql.*;

public class Main {
    public static void main(String[] args) throws SQLException {
        String url = "jdbc:arrow-flight-sql://localhost:2113?useEncryption=false";

        try (Connection conn = DriverManager.getConnection(url);
             PreparedStatement stmt = conn.prepareStatement(
                     "SELECT * FROM kdb.records WHERE log_position > ?")) {

            var meta = stmt.getMetaData();
            System.out.println(meta.getColumnCount());

            stmt.setInt(1, 42);  // 1-based index

            try (ResultSet rs = stmt.executeQuery()) {
                while (rs.next()) {
                    // access columns by index or name
                    System.out.println(rs.getLong("log_position"));
                }
            }
        }
    }
}
```

It requires the following dependencies in POM file in case of Maven:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<project xmlns="http://maven.apache.org/POM/4.0.0"
         xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
         xsi:schemaLocation="http://maven.apache.org/POM/4.0.0 http://maven.apache.org/xsd/maven-4.0.0.xsd">
    <modelVersion>4.0.0</modelVersion>

    <groupId>org.example</groupId>
    <artifactId>AdbcTest</artifactId>
    <version>1.0-SNAPSHOT</version>

    <properties>
        <maven.compiler.source>17</maven.compiler.source>
        <maven.compiler.target>17</maven.compiler.target>
        <project.build.sourceEncoding>UTF-8</project.build.sourceEncoding>
    </properties>

    <dependencies>
        <dependency>
            <groupId>org.apache.arrow</groupId>
            <artifactId>flight-sql-jdbc-driver</artifactId>
            <version>19.0.0</version>
        </dependency>
    </dependencies>

</project>
```

### Python
For Python, the following dependencies needs to be installed first:
```shell
pip install pyarrow
pip install adbc-driver-flightsql
```

Then, ADBC driver is used to run the queries:
```python
import adbc_driver_flightsql.dbapi as dbapi

with dbapi.connect("grpc://localhost:41119") as conn:
    with conn.cursor() as cur:
        cur.execute("SELECT * FROM kdb.records WHERE log_position>?", parameters=[42])
        print(cur.fetchall())          # list of tuples
```

### C#
The following dependency needs to be added to `csproj` file:
```xml
<PackageReference Include="Apache.Arrow.Adbc.Drivers.FlightSql" />
<PackageReference Include="Apache.Arrow.Adbc.Client" />
```

Then, ADO.NET can be used to run the queries:
```cs
using Apache.Arrow.Adbc.Client;
using Apache.Arrow.Adbc.Drivers.FlightSql;

using var driver = new FlightSqlDriver();

var parameters = new Dictionary<string, string> {
    [FlightSqlParameters.ServerAddress] = HostingAddress
};

var options = new Dictionary<string, string> {
    [FlightSqlParameters.ServerAddress] = HostingAddress
};

await using var connection = new AdbcConnection(driver, parameters, options);

await connection.OpenAsync();
await using var command = connection.CreateCommand();
command.CommandText = "SELECT * FROM kdb.records";
await using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync()) {
    Console.WriteLine(reader["log_position"]);
}
```