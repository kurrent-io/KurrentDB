# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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

**Protocol Buffers** - Located in `/proto` directory:
- gRPC service definitions for streams, persistent subscriptions, operations
- Schema registry protocols in `kurrentdb/protocol/v2/registry/`
- Multi-version protocol support (legacy, v1, v2)

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

### Project Structure

- **Core Projects**: KurrentDB.Core, KurrentDB.Common, KurrentDB.LogV3
- **Transport**: KurrentDB.Transport.Http, KurrentDB.Transport.Tcp
- **Plugins**: Individual plugin projects with naming `KurrentDB.*.PluginName`
- **Testing**: Projects ending in `.Tests` use various testing frameworks
- **UI**: KurrentDB.UI (Blazor), KurrentDB.ClusterNode.Web
- **Connectors**: Server-side data integration via catch-up subscriptions and sinks

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
- Configurable thread pools (reader, worker threads)
- Chunk caching and memory management
- Index optimization with bloom filters and stream existence filters
- Scavenging for disk space management and performance

**Monitoring & Operations**:
- Structured JSON logging with configurable levels
- Prometheus metrics on `/metrics` endpoint
- Integration with OpenTelemetry, Datadog, ElasticSearch
- Statistics collection and HTTP stats endpoint
- Embedded admin UI and legacy web interface

### Development Notes

- Target framework: .NET 8.0 with Server GC enabled by default
- Uses unsafe code blocks for performance-critical operations
- Protocol buffer generation integrated into build process (`KurrentDB.Protocol` project)
- Extensive use of dependency injection and hosted services pattern
- Plugin discovery via assembly scanning and configuration files
- Event-native design with write-ahead log and immutable event streams

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
