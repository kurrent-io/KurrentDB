# Architecture Reference

> Read this when: working on core infrastructure, understanding project layout, or onboarding.

## Core Components

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

## Key Services

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

**Schema Registry** (`SchemaRegistry/`):
- Event schema validation and versioning
- Surge framework integration for event processing
- Protocol support in `kurrentdb/protocol/v2/registry/`

## Project Structure

- **Core Projects**: KurrentDB.Core, KurrentDB.Common, KurrentDB.LogCommon (LogV3 is being removed — never shipped to production)
- **Transport**: KurrentDB.Transport.Http, KurrentDB.Transport.Tcp
- **API Projects**: KurrentDB.Api.V2, KurrentDB.Plugins.Api.V2 (Protocol v2 support)
- **Plugins**: Individual plugin projects with naming `KurrentDB.*.PluginName`
  - Authentication: KurrentDB.Auth.Ldaps, KurrentDB.Auth.OAuth, KurrentDB.Auth.UserCertificates
  - Authorization: KurrentDB.Auth.StreamPolicyPlugin, KurrentDB.Auth.LegacyAuthorizationWithStreamAuthorizationDisabled
  - Infrastructure: KurrentDB.AutoScavenge, KurrentDB.OtlpExporterPlugin, KurrentDB.Security.EncryptionAtRest
  - Diagnostics: KurrentDB.Diagnostics.LogsEndpointPlugin
- **Secondary Indexing**: KurrentDB.SecondaryIndexing, KurrentDB.DuckDB (90+ total projects)
- **Schema Registry**: SchemaRegistry/ (4 projects)
- **Testing**: Projects ending in `.Tests` use xUnit, NUnit, and TUnit frameworks
  - KurrentDB.Testing - Shared testing infrastructure
  - KurrentDB.Surge.Testing - Surge framework testing utilities
- **UI**: KurrentDB.UI (Blazor embedded UI), KurrentDB.ClusterNode.Web (legacy)
- **Connectors**: Server-side data integration via catch-up subscriptions and sinks
- **Supporting Libraries**: KurrentDB.Surge, KurrentDB.SystemRuntime, KurrentDB.BufferManagement, KurrentDB.Logging

## Configuration

- `Directory.Packages.props` - Central NuGet package version management
- Projects use `<PackageReference>` without version attributes
- Configuration via YAML files, environment variables, and command line
- Clustering requires certificate-based authentication between nodes
- License key required for enterprise features (TCP Plugin, OTLP Exporter, Archiving, etc.)

## Operational Considerations

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

**Monitoring & Operations**:
- Structured JSON logging with configurable levels (Microsoft log levels required)
- Prometheus metrics on `/metrics` endpoint (prefixed with `kurrentdb_`)
- Integration with OpenTelemetry (metrics and logs export with license), Datadog, ElasticSearch
- Statistics collection and HTTP stats endpoint
- Embedded admin UI (new) and legacy web interface (`/web`)

## Development Notes

- Target framework: .NET 10.0 with Server GC enabled by default
- Uses unsafe code blocks for performance-critical operations
- Protocol buffer generation integrated into build process (`KurrentDB.Protocol` project)
- Extensive use of dependency injection and hosted services pattern
- Plugin discovery via assembly scanning and configuration files
- Event-native design with write-ahead log and immutable event streams
- DuckDB integration for secondary indexing capabilities
- Surge framework for event processing and schema registry
- TUnit testing framework adoption (alongside xUnit and NUnit)
- 90+ projects in solution as of v25.1
