---
title: 'Sql Sink'
---

<Badge type="info" vertical="middle" text="License Required"/>

## Overview

The SQL sink writes events to SQL databases by executing SQL statements. SQL
statements can be configured by defining a mapping between schema names and SQL
statements.

## Supported Databases

The connector supports the following database systems:
- Microsoft SQL Server
- PostgreSQL

::: warning
This connector only processes events with the `application/json` content type. Events with other content types will fail processing.
:::

## Quick Start

### SQL Server

You can create the SQL Server sink connector as follows. Replace `{{id}}` with your desired connector ID:

```http request
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
    "instanceTypeName": "sql-sink",
    "type": "SqlServer",
    "connectionString": "Server=127.0.0.1,1433;Database=master;User Id=sa;Password=YourPassword123;TrustServerCertificate=True",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "vehicles",
    "reducer:mappings": "<base64-encoded-mappings-json>"
}
```

### PostgreSQL

```http request
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
    "instanceTypeName": "sql-sink",
    "type": "PostgreSql",
    "connectionString": "Host=127.0.0.1;Port=54322;Database=postgres;Username=postgres;Password=postgres",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "vehicles",
    "reducer:mappings": "<base64-encoded-mappings-json>"
}
```

## Specifying SQL Statements

Define SQL statement templates with parameter placeholders (prefixed with `@`) and JavaScript extractor functions that extract values from records. 

**Example configuration:**

```javascript
{
  "VehicleRegistered": {
    "Statement": "INSERT INTO vehicle_registrations (id, vin) VALUES (@id, @vin)",
    "Extractor": "(record) => ({ id: record.value.registrationId, vin: record.value.vin })"
  },
  "VehicleTransferred": {
    "Statement": "UPDATE vehicle_registrations SET owner_name = @newOwnerName WHERE vin = @vin",
    "Extractor": "(record) => ({ vin: record.value.vin, newOwnerName: record.value.newOwnerName })"
  }
}
```

The configuration is passed as a base64-encoded JSON string using the `reducer:mappings` option.

::: note 
The extractor function receives a `record` parameter following the [KurrentDB record structure](../features.md#kurrentdb-record). It must return an object with properties matching the parameter placeholders in the SQL statement (without the `@` prefix).
:::

### Helper Functions

Use helper functions to ensure values are correctly typed for your database columns:

| Function           | Use For                           | Example                            |
|--------------------|-----------------------------------|------------------------------------|
| `Guid(string)`     | `UUID`/`UNIQUEIDENTIFIER` columns | `Guid(record.value.id)`            |
| `DateTime(string)` | `TIMESTAMP`/`DATETIME` columns    | `DateTime(record.value.createdAt)` |
| `TimeSpan(string)` | `TIME`/`INTERVAL` columns         | `TimeSpan(record.value.duration)`  |

**Example:**

```javascript
{
  "OrderPlaced": {
    "Statement": "INSERT INTO orders (id, placed_at, customer_name) VALUES (@id, @placedAt, @customerName)",
    "Extractor": "(record) => ({ id: Guid(record.value.orderId), placedAt: DateTime(record.value.timestamp), customerName: record.value.customer.name })"
  }
}
```

## Settings

Adjust these settings to specify the behavior and interaction of your SQL sink connector with KurrentDB, ensuring it operates according to your requirements and preferences.

::: tip
The SQL sink connector inherits a set of common settings that are used to configure the connector. The settings can be found in
the [Sink Options](../settings.md#sink-options) page.
:::

The SQL sink connector can be configured with the following options:
### Core Options

| Name               | Details                                                                                                                                                             |
| ------------------ | :------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `type`             | **Type**: Enum<br><br>**Description:**<br>Type of SQL database to connect to.<br><br>**Accepted Values:** `SqlServer`, `PostgreSql`<br><br>**Default**: `SqlServer` |
| `connectionString` | _required_<br><br>**Description:**<br>Connection string to the SQL database. Authentication options will override credentials in the connection string.             |
| `reducer:mappings` | **Description:**<br>Base64-encoded JSON object mapping schema names to SQL statement templates and parameter extractors.                                            |

### Authentication

#### Username/Password

| Name                      | Details                                                |
|---------------------------|:-------------------------------------------------------|
| `authentication:username` | **Description:** Username for database authentication. |
| `authentication:password` | **Description:** Password for database authentication. |

This will overwrite any username/password specified in the connection string.

#### Client Certificate (PostgreSQL)

| Name                                 | Details                                                            |
|--------------------------------------|:-------------------------------------------------------------------|
| `authentication:clientCertificate`   | **Description:** Base64 encoded client certificate for mutual TLS. |
| `authentication:certificatePassword` | **Description:** Password for the client certificate.              |

### Resilience

| Name                                       | Details                                                                                                                                                                                                              |
|--------------------------------------------|:---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `resilience:enabled`                       | **Type**: boolean<br><br>**Description:** Enables resilience features for database operations.<br><br>**Default**: `true`                                                                                            |
| `resilience:commandTimeout`                | **Type**: int<br><br>**Description:** Command timeout in seconds.<br><br>**Default**: `30`                                                                                                                           |
| `resilience:connectionTimeout`             | **Type**: int<br><br>**Description:** Connection timeout in seconds for establishing the initial database connection.<br><br>**Default**: `15`                                                                       |
| `resilience:minPoolSize`                   | **Type**: int<br><br>**Description:** Minimum number of connections in the pool.<br><br>**Default**: `0`                                                                                                             |
| `resilience:maxPoolSize`                   | **Type**: int<br><br>**Description:** Maximum number of connections in the pool.<br><br>**Default**: `100`                                                                                                           |
| `resilience:connectionLifetime`            | **Type**: int<br><br>**Description:** Maximum lifetime of a connection in seconds. When a connection is returned to the pool, it is destroyed if its lifetime exceeds this value.<br><br>**Default**: `0` (no limit) |
| `resilience:firstDelayBound:upperLimitMs`  | **Type**: double<br><br>**Description:** Upper time limit in milliseconds for the first backoff delay bound.<br><br>**Default**: `60000` (1 minute)                                                                  |
| `resilience:firstDelayBound:delayMs`       | **Type**: double<br><br>**Description:** Delay in milliseconds for the first backoff bound.<br><br>**Default**: `5000` (5 seconds)                                                                                   |
| `resilience:secondDelayBound:upperLimitMs` | **Type**: double<br><br>**Description:** Upper time limit in milliseconds for the second backoff delay bound.<br><br>**Default**: `3600000` (1 hour)                                                                 |
| `resilience:secondDelayBound:delayMs`      | **Type**: double<br><br>**Description:** Delay in milliseconds for the second backoff bound.<br><br>**Default**: `600000` (10 minutes)                                                                               |
| `resilience:thirdDelayBound:upperLimitMs`  | **Type**: double<br><br>**Description:** Upper time limit in milliseconds for the third backoff delay bound. `-1` means unlimited.<br><br>**Default**: `-1`                                                          |
| `resilience:thirdDelayBound:delayMs`       | **Type**: double<br><br>**Description:** Delay in milliseconds for the third backoff bound.<br><br>**Default**: `3600000` (1 hour)                                                                                   |

The settings `commandTimeout`, `connectionTimeout`, `minPoolSize`, `maxPoolSize`, and `connectionLifetime` will override any corresponding values specified in the connection string.


## Examples

### Inserting into PostgreSQL

This example demonstrates how to insert vehicle registration records into a local PostgreSQL instance on Supabase using the SQL sink connector.

#### Prerequisites

- [Supabase CLI](https://supabase.com/docs/guides/local-development/cli/getting-started#installing-the-supabase-cli) installed and running locally

#### Step 1: Get the Database Connection String

Run the following command to get your local Supabase connection details:

```bash
supabase status
```

**Expected output:**

```bash
Database URL: postgresql://postgres:postgres@127.0.0.1:54322/postgres
Studio URL: http://127.0.0.1:54323
...
```

::: tip
Note down the **Studio URL** - you'll need it to verify the data insertion in step 6.
:::

#### Step 2: Format the Connection String

The SQL connector requires the connection string in ADO.NET format:

```
Host=<host>;Port=<port>;Database=<database>;Username=<username>;Password=<password>
```

**Example using the output above:**

```
Host=127.0.0.1;Port=54322;Database=postgres;Username=postgres;Password=postgres
```

#### Step 3: Create the Database Table

1. Open the Supabase Studio at `http://127.0.0.1:54323`
2. Navigate to the **SQL Editor**
3. Execute the following SQL:

```sql
CREATE TABLE vehicle_registrations (
    id UUID PRIMARY KEY,
    vin VARCHAR(255) NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
```

#### Step 4: Create the Statement Mappings

Define SQL statement templates with parameter extractors for each schema name. Use helper functions like `Guid()` to convert string values to their proper types:

```javascript
{
  "VehicleRegistered": {
    "Statement": "INSERT INTO vehicle_registrations (id, vin) VALUES (@id, @vin)",
    "Extractor": "(record) => ({ id: Guid(record.value.registrationId), vin: record.value.vin })"
  }
}
```

::: tip
The `Guid()` helper function converts the string `registrationId` to a proper `System.Guid` type, which maps to PostgreSQL's `UUID` type.
:::

#### Step 5: Encode the Mappings to Base64

**PowerShell:**

```powershell
$mappings = @'
{
  "VehicleRegistered": {
    "Statement": "INSERT INTO vehicle_registrations (id, vin) VALUES (@id, @vin)",
    "Extractor": "(record) => ({ id: Guid(record.value.registrationId), vin: record.value.vin })"
  }
}
'@

$encoded = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($mappings))
Write-Output $encoded
```

**Bash/Linux:**

```bash
echo -n '{
  "VehicleRegistered": {
    "Statement": "INSERT INTO vehicle_registrations (id, vin) VALUES (@id, @vin)",
    "Extractor": "(record) => ({ id: Guid(record.value.registrationId), vin: record.value.vin })"
  }
}' | base64 -w 0
```

**Expected output:**

```
ewogICJWZWhpY2xlUmVnaXN0ZXJlZCI6IHsKICAgICJTdGF0ZW1lbnQiOiAiSU5TRVJUIElOVE8gdmVoaWNsZV9yZWdpc3RyYXRpb25zIChpZCwgdmluKSBWQUxVRVMgKEBpZCwgQHZpbikiLAogICAgIkV4dHJhY3RvciI6ICIocmVjb3JkKSA9PiAoeyBpZDogcmVjb3JkLnZhbHVlLnJlZ2lzdHJhdGlvbklkLCB2aW46IHJlY29yZC52YWx1ZS52aW4gfSkiCiAgfQp9
```

#### Step 6: Create and Start the Connector

**Create the connector:**

```http
POST /connectors/{{id}}
Host: localhost:2113
Content-Type: application/json

{
  "settings": {
    "instanceTypeName": "sql-sink",
    "type": "PostgreSql",
    "connectionString": "Host=127.0.0.1;Port=54322;Database=postgres;Username=postgres;Password=postgres",
    "reducer:mappings": "<base64-encoded-mappings-from-step-5>",
    "subscription:filter:scope": "stream",
    "subscription:filter:filterType": "streamId",
    "subscription:filter:expression": "vehicles"
  }
}
```

**Start the connector:**

```http
POST /connectors/{{id}}/start
Host: localhost:2113
```

::: tip
The connector will now listen for records on the `vehicles` stream and insert them into the database.
:::

#### Step 7: Test with Sample Data

Append a test event to the `vehicles` stream:

```http
POST /streams/vehicles
Host: localhost:2113
Content-Type: application/vnd.eventstore.events+json

[
  {
    "eventId": "{{$guid}}",
    "eventType": "VehicleRegistered",
    "data": {
      "registrationId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "vin": "1HGBH41JXMN109186"
    }
  }
]
```

#### Step 8: Verify the Results

1. Open Supabase Studio at `http://127.0.0.1:54323`
2. Navigate to **Table Editor** → **vehicle_registrations**
3. You should see the inserted record:

| id                                   | vin               | created_at          |
|--------------------------------------|-------------------|---------------------|
| a1b2c3d4-e5f6-7890-abcd-ef1234567890 | 1HGBH41JXMN109186 | 2025-11-26 14:23:15 |
