# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What's New in 25.1

KurrentDB 25.1 introduces several major features and improvements:

### Major Features
- **Secondary Indexing** - Query optimization with DuckDB-backed indexes for category, event type, and custom indexes
- **Schema Registry** - Event validation and schema management with Surge framework integration
- **Multi-stream Appends** - Atomic writes across multiple streams with optimistic concurrency checks
- **Log Record Properties** - Structured event metadata support (client support in progress)
- **Windows Service** - Native Windows Service deployment option
- **OpenTelemetry Logs Export** - Extended observability with OTLP log export (license required)

### Configuration Changes
- **ServerGC** - Enabled by default for improved performance
- **StreamInfoCacheCapacity** - Default changed to 100,000 (from 0/dynamic sizing)
- **SecondaryIndexing** - Enabled by default
- **MemDb** - Deprecated, will be removed in future version

### New Metrics
- Projection state size, serialization duration, and execution duration metrics
- Persistent subscription parked message and replay metrics
- Garbage collection suspension logging for performance troubleshooting

## Development Commands

### Build
- `./build.sh [version] [configuration]` - Build KurrentDB (Linux/MacOS)
- `./build.ps1 [version] [configuration]` - Build KurrentDB (Windows)
- `dotnet build -c Release /p:Platform=x64 --framework=net8.0 src/KurrentDB.sln` - Direct dotnet build

### Test
- `dotnet test src/KurrentDB.sln` - Run all tests
- `dotnet test src/ProjectName.Tests/` - Run tests for a specific project
- Tests use xunit, NUnit, and TUnit frameworks depending on the project

### Run Single Test
Navigate to the test project directory and use:
- `dotnet test --filter "FullyQualifiedName~TestMethodName"`
- `dotnet test --filter "TestClass"`

### Development Server
- Start server: `dotnet ./src/KurrentDB/bin/Release/net8.0/KurrentDB.dll --dev --db ./tmp/data --index ./tmp/index --log ./tmp/log`
- Default ports: HTTP/gRPC on 2113, Internal TCP on 1112
- Admin UI: `http://localhost:2113` (new embedded UI) or `http://localhost:2113/web` (legacy)
- Windows Service: KurrentDB can now be run as a Windows Service (see installation docs)

### Configuration & Diagnostics
- Configuration files use YAML format (`kurrentdb.conf`)
- Logs location: `/var/log/kurrentdb` (Linux/Mac), `logs/` (Windows)
- Stats endpoint: `http://localhost:2113/stats`
- Metrics endpoint: `http://localhost:2113/metrics` (Prometheus format)

## Architecture Overview

KurrentDB is an event-native database with a distributed, plugin-based architecture:

### Core Components

**KurrentDB.Core** - Central database engine containing:
- Services layer with transport (gRPC, HTTP, TCP), storage, replication, and monitoring
- Transport protocols: gRPC (primary), HTTP API, and legacy TCP via licensed plugin
- Storage engine with write-ahead log, indexing, scavenging, and optional archiving
- Clustering with gossip protocol, leader election, and quorum-based replication
- Authentication/authorization framework with pluggable providers

**Plugin System** - Extensible architecture via `KurrentDB.Plugins`:
- Authentication plugins (LDAP, OAuth, UserCertificates)
- Authorization plugins (StreamPolicy, Legacy)
- Infrastructure plugins (AutoScavenge, OTLP Exporter, Archiving)
- Connectors for external systems (HTTP, Kafka, MongoDB, RabbitMQ, Elasticsearch, Serilog)
- Secondary Indexing plugin - DuckDB-backed query optimization (enabled by default)
- Schema Registry plugin - Event validation and schema management
- API v2 plugin - Next-generation protocol support

**Protocol Buffers** - Located in `/proto` directory:
- gRPC service definitions for streams, persistent subscriptions, operations
- Schema registry protocols in `kurrentdb/protocol/v2/registry/`
- Multi-version protocol support (legacy, v1, v2)

**API v2 Architecture** - Next-generation API (`src/KurrentDB.Api.V2/` + `src/KurrentDB.Plugins.Api.V2/`):
- **Evolving rapidly** - This is an active development area with frequent changes
- Modular plugin-based architecture via `ApiV2Plugin`
- gRPC service implementations with request validation
- Infrastructure: DependencyInjection, Error handling, Validation framework
- Streams service with multi-stream append support
- Protocol v2 integration (`KurrentDB.Protocol.V2.Streams`)
- Separate from legacy API to allow parallel evolution
- **Note**: When working with API v2, expect ongoing refactoring and protocol changes

### Key Services Architecture

**Transport Layer** (`Services/Transport/`):
- `Grpc/` - Primary gRPC API with v2 protocol support and keepalive configuration
- `Http/` - HTTP API with authentication middleware, Kestrel configuration, and AtomPub (deprecated)
- `Tcp/` - Legacy TCP protocol available via licensed plugin

**Storage Layer** (`Services/Storage/`):
- `ReaderIndex/` - Optimized read path with caching, bloom filters, and stream existence filters
- `Replication/` - Leader-follower replication with heartbeat monitoring
- `Archive/` - Long-term storage with pluggable backends (S3, FileSystem) - License Required
- `Scavenging/` - Disk space reclamation with automatic and manual merge operations

**Persistent Subscriptions** (`Services/PersistentSubscription/`):
- Consumer strategies (RoundRobin, Pinned, DispatchToSingle, PinnedByCorrelation)
- Event sourcing with checkpointing, message parking, and competing consumers pattern
- Server-side subscription state management with at-least-once delivery guarantees

**Projections System** (`Projections.Core/`):
- System projections ($by_category, $by_event_type, etc.)
- Custom JavaScript projections with state management
- Real-time event processing and stream linking

**Secondary Indexing** (`KurrentDB.SecondaryIndexing/`):
- DuckDB-powered secondary indexes for optimized queries
- Default indexes: Category, EventType, and configurable custom indexes
- In-flight record tracking and batch processing (default 50,000 events)
- Telemetry and statistics collection

**Schema Registry** (`SchemaRegistry/`):
- Event schema validation and versioning
- Surge framework integration for event processing
- Protocol support in `kurrentdb/protocol/v2/registry/`
- Pluggable architecture with dedicated service layer

### Project Structure

- **Core Projects**: KurrentDB.Core, KurrentDB.Common, KurrentDB.LogV3
- **Transport**: KurrentDB.Transport.Http, KurrentDB.Transport.Tcp
- **API Projects**: KurrentDB.Api.V2, KurrentDB.Plugins.Api.V2 (Protocol v2 support)
- **Plugins**: Individual plugin projects with naming `KurrentDB.*.PluginName`
  - Authentication: KurrentDB.Auth.Ldaps, KurrentDB.Auth.OAuth, KurrentDB.Auth.UserCertificates
  - Authorization: KurrentDB.Auth.StreamPolicyPlugin, KurrentDB.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled
  - Infrastructure: KurrentDB.AutoScavenge, KurrentDB.OtlpExporterPlugin, KurrentDB.Security.EncryptionAtRest
  - Diagnostics: KurrentDB.Diagnostics.LogsEndpointPlugin
- **Secondary Indexing**: KurrentDB.SecondaryIndexing, KurrentDB.DuckDB (90+ total projects)
- **Schema Registry**: SchemaRegistry/ (4 projects: KurrentDB.SchemaRegistry, KurrentDB.SchemaRegistry.Protocol, KurrentDB.Plugins.SchemaRegistry, KurrentDB.SchemaRegistry.Tests)
- **Testing**: Projects ending in `.Tests` use xUnit, NUnit, and TUnit frameworks
  - KurrentDB.Testing - Shared testing infrastructure
  - KurrentDB.Surge.Testing - Surge framework testing utilities
- **UI**: KurrentDB.UI (Blazor embedded UI), KurrentDB.ClusterNode.Web (legacy)
- **Connectors**: Server-side data integration via catch-up subscriptions and sinks
  - Connectors/ (5 projects: KurrentDB.Connectors, KurrentDB.Plugins.Connectors, KurrentDB.Connectors.Contracts, etc.)
- **Supporting Libraries**: KurrentDB.Surge (event processing), KurrentDB.SystemRuntime, KurrentDB.BufferManagement, KurrentDB.Logging

### Configuration

Uses centralized package management:
- `Directory.Packages.props` - Central NuGet package version management
- Projects use `<PackageReference>` without version attributes
- Configuration via YAML files, environment variables, and command line
- Clustering requires certificate-based authentication between nodes
- License key required for enterprise features (TCP Plugin, OTLP Exporter, Archiving, etc.)

### Operational Considerations

**Clustering & High Availability**:
- Quorum-based replication (2n+1 nodes for n-node fault tolerance)
- Gossip protocol for node discovery (DNS or seed-based)
- Read-only replicas for scaling reads without affecting quorum
- Leader election with configurable timeouts and priorities

**Performance & Scaling**:
- Server GC enabled by default for improved performance
- StreamInfoCacheCapacity default of 100,000 (changed from dynamic sizing)
- Configurable thread pools (reader, worker threads)
- Chunk caching and memory management
- Index optimization with bloom filters and stream existence filters
- Secondary indexes with DuckDB for optimized queries
- Scavenging for disk space management and performance

**Monitoring & Operations**:
- Structured JSON logging with configurable levels (Microsoft log levels required)
- Prometheus metrics on `/metrics` endpoint (prefixed with `kurrentdb_`)
- Integration with OpenTelemetry (metrics and logs export with license), Datadog, ElasticSearch
- Statistics collection and HTTP stats endpoint
- Embedded admin UI (new) and legacy web interface (`/web`)
- License status monitoring in embedded UI
- New projection metrics: state size, serialization duration, execution duration
- New persistent subscription metrics: parked message replays, park requests
- GC suspension logging for performance troubleshooting (>48ms logged as Info, >600ms as Warning)

### Development Notes

- Target framework: .NET 8.0 with Server GC enabled by default
- Uses unsafe code blocks for performance-critical operations
- Protocol buffer generation integrated into build process (`KurrentDB.Protocol` project)
- Extensive use of dependency injection and hosted services pattern
- Plugin discovery via assembly scanning and configuration files
- Event-native design with write-ahead log and immutable event streams
- DuckDB integration for secondary indexing capabilities
- Surge framework for event processing and schema registry
- TUnit testing framework adoption (alongside xUnit and NUnit)
- 90+ projects in solution as of v25.1

### Active Development Areas (Expect Frequent Changes)

**API v2** (`src/KurrentDB.Api.V2/` and `src/KurrentDB.Plugins.Api.V2/`):
- This is the next-generation API layer currently under rapid development
- Implements protocol v2 with new features like multi-stream appends and log record properties
- Modular architecture: Infrastructure layer (DI, errors, validation) + Service modules (Streams, etc.)
- Plugin-based activation via `ApiV2Plugin`
- When working in this area, expect ongoing protocol changes, refactoring, and architectural evolution
- Coexists with legacy API to allow gradual migration and experimentation

**Schema Registry** (`src/SchemaRegistry/`):
- Event validation and schema management system
- Integrates with Surge framework for event processing
- Protocol definitions in `proto/kurrentdb/protocol/v2/registry/`
- Plugin system for extensibility

**Secondary Indexing** (`src/KurrentDB.SecondaryIndexing/`):
- DuckDB-backed query optimization layer
- Active development on index strategies and performance tuning

### Important Configuration Defaults (v25.1)

- **SecondaryIndexing:Enabled** - `true` (enabled by default)
- **ServerGC** - `true` (enabled by default)
- **StreamInfoCacheCapacity** - `100000` (changed from `0`/dynamic)
- **TcpReadTimeoutMs** - `10000` (10 seconds)

### Deprecated/Removed Options

- **MemDb** - Deprecated in v25.1, will be removed in future version
- **DisableFirstLevelHttpAuthorization** - Removed in v25.1 (had no effect since v20.6.0)
- See `docs/server/quick-start/upgrade-guide.md` for complete list of breaking changes

## MCP Server Configuration

This repository includes pre-configured MCP (Model Context Protocol) servers for enhanced development experience:

### Available MCP Servers
- **Microsoft Docs MCP Server**: Access official Microsoft and Azure documentation
- **Context7 MCP Server**: Library documentation and code examples
- **Filesystem MCP Server**: Direct file system access to project files with read operations

### Configuration Files
- `.claude/settings.json` - Project-level MCP server configurations (committed to repo)
- `.claude/settings.local.json` - Local user-specific permissions (not committed)
- `.claude/README.md` - Detailed MCP setup documentation

## Querying Microsoft Documentation

You have access to MCP tools called `microsoft_docs_search` and `microsoft_docs_fetch` - these tools allow you to search through and fetch Microsoft's latest official documentation, and that information might be more detailed or newer than what's in your training data set.

When handling questions around how to work with native Microsoft technologies, such as C#, F#, ASP.NET Core, Microsoft.Extensions, NuGet, Entity Framework, the `dotnet` runtime - please use this tool for research purposes when dealing with specific / narrowly defined questions that may occur.

### Usage
When working with Claude Code, you can directly query these servers:
```
# Search Microsoft docs
Search for ASP.NET Core authentication patterns

# Get library documentation  
Get documentation for Entity Framework Core

# Read project files directly
Read the main KurrentDB.Core project file

# Access multiple files efficiently
Read all protocol buffer definitions in the proto directory
```

The servers provide access to up-to-date documentation, examples, and direct file access without manual searching or separate tool calls.
