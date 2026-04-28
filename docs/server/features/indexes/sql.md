# SQL

KurrentDB v26.1 introduces support of SQL and [Arrow Flight SQL](https://arrow.apache.org/docs/format/FlightSql.html) protocol that can be used to execute queries on indexes. Arrow Flight SQL is available for the broad set of programming languages and runtimes:
* JDBC driver for Java
* Python
* ADO.NET for C#/F#
* Rust
* R
* Go
* JavaScript/NodeJS

A full set of existing implementations you can find [here](https://arrow.apache.org/docs/implementations.html).

If high-level database driver is not available for necessary language, you can generate client from gRPC Interface Definition Language for FlightSql protocol.

## Introduction

You need to install necessary packages or modules for required language/runtime. Then, the connection string must be configured. FlightSQL protocol is exposed as gRPC endpoint on the same port as the main KurrentDB gRPC API and described in [Networking](../../configuration/networking.md) article. For Python library, the example is `grpc://localhost:2113`.

Also, the authentication is required to access FlightSQL protocol. Currently, the administrative privileges in KurrentDB are required to run the queries. By default, it's `admin` user. Depending on the client library, valid credentials needs to be passed to the server.

Note that only **SELECT** statements are allowed. **INSERT**, **UPDATE**, stored procedures or others DDL statements are not supported.

## SQL dialect

KurrentDB supports PostgreSQL-compatible SQL dialect. System tables are accessible via **kdb** schema. All user-defined indices are available through **usr** table schema. The default index is available through **kdb.records** schema.

The following example demonstrates how to select all records in the specified category:
```sql
SELECT data FROM kdb.records WHERE category='my_category'
```

Every user-defined index has its equivalent table in `usr` schema to be queried. Examine [this article](./user-defined.md) first to configure a user-defined index.

### Default Index Table

List of available columns:
* `log_position` of type `INT64` - the position of the record within the log
* `created_at` of type `INT64` - Unix UTC timestamp of when the record was saved to the log
* `expires_at` of type `INT64`
* `stream` of type `VARCHAR` - stream identifier
* `stream_revision` - stream revision
* `schema_name` of type `VARCHAR` - record schema
* `schema_id` of type `VARCHAR` - record schema identifier
* `schema_format` of type `VARCHAR` - record schema format. Typically, it's `json`
* `category` of type `VARCHAR` - record category
* `record_id` of type BLOB (16 bytes) - unique identifier of the record
* `data` of type `VARCHAR` - the payload of the record. In case of JSON, you can cast this column to `JSON` data type and use arrow navigation operators to access the inner fields
* `metadata` of type `VARCHAR` - the metadata of the record in the form of the JSON

### User-defined Index Table

List of available columns:
* `log_position` of type `INT64` - the position of the record within the log
* `stream_revision` - stream revision
* `created_at` of type `INT64` - Unix UTC timestamp of when the record was saved to the log
* `record_id` of type BLOB (16 bytes) - unique identifier of the record
* A custom field configured as an index

## Supported FlightSQL Features
Arrow FlightSQL is a rich protocol with many features. Its implementation in KurrentDB supports the following functionality:
* Ad-hoc query execution (`CommandStatementQuery` command)
* Prepared statements and parameters binding
* Schema discovery for queries and prepared statements

## Examples
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