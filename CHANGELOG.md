# Changelog

**Note:** Changelog updates are no longer tracked in this file. Future changelog updates can be found in [Releases](https://github.com/EventStore/EventStore/releases).

## [24.10.0] - 2024-11-13

### Fixed
- Fixed potential partial read in TFChunk. [EventStore#4608](https://github.com/EventStore/EventStore/pull/4608)
- Runtime information log on startup [EventStore#4572](https://github.com/EventStore/EventStore/pull/4572)
- Enhance shutdown service to handle pre-terminated services. [EventStore#4563](https://github.com/EventStore/EventStore/pull/4563)
- Explicitly deny stream operations in fallback policy. [EventStore#4567](https://github.com/EventStore/EventStore/pull/4567)
- Don't time out projection manager reads. [EventStore#4557](https://github.com/EventStore/EventStore/pull/4557)
- Only use the fallback policy for stream access. Revert to the legacy policy for endpoint access. [EventStore#4537](https://github.com/EventStore/EventStore/pull/4537)
- Fix authorization policy registry being loaded in insecure mode. [EventStore#4530](https://github.com/EventStore/EventStore/pull/4530)
- If the scavenge is unable to write a scavenge point it will now error rather than stall. [EventStore#4512](https://github.com/EventStore/EventStore/pull/4512)
- Reverted free mem stat to be MemAvailable rather than MemFree on linux. [EventStore#4509](https://github.com/EventStore/EventStore/pull/4509)
- Missing BSD-3 attribution since the recent license change. [EventStore#4493](https://github.com/EventStore/EventStore/pull/4493)
- Don't write the database default `ProjectionExecutionTimeout` in the projection persisted state on creation. [EventStore#4432](https://github.com/EventStore/EventStore/pull/4432)
- Ignore dlls that are not .NET assemblies when loading plugins. [EventStore#4380](https://github.com/EventStore/EventStore/pull/4380)
- Gossip on single node. [EventStore#4367](https://github.com/EventStore/EventStore/pull/4367)
- Optimize CPU usage of the timer service when the database is idle. [EventStore#4224](https://github.com/EventStore/EventStore/pull/4224) - thanks [@taspeotis](https://github.com/taspeotis)!
- Redaction: Return the chunk's `MinCompatibleVersion` instead of `Version` when retrieving event positions. [EventStore#4354](https://github.com/EventStore/EventStore/pull/4354)
- Avoid replaying deleted events from the source stream when replaying parked messages for a persistent subscription. [EventStore#4300](https://github.com/EventStore/EventStore/pull/4300)
- LeaderId and Epoch number would sometimes be empty (Guid.empty) for a follower node or a read only replica that would join a cluster whose leader is already elected. https://eventstore.aha.io/develop/requirements/DB-26-4 . [Linear Issue](https://linear.app/eventstore/issue/DB-611/leaderid-sometimes-guidempty-in-telemetry) [EventStore#4256](https://github.com/EventStore/EventStore/pull/4256)
- Remove redundant check which is always true. [EventStore#4265](https://github.com/EventStore/EventStore/pull/4265)
- Typos in option descriptions. [EventStore#4215](https://github.com/EventStore/EventStore/pull/4215)

### Added
- ReadOnlyReplica and archiver flags to machine metadata in license. [EventStore#4597](https://github.com/EventStore/EventStore/pull/4597)
- License error to `/license` endpoint. [EventStore#4549](https://github.com/EventStore/EventStore/pull/4549)
- Log warning on startup about removed "Plugins" configuration subsection. [EventStore#4540](https://github.com/EventStore/EventStore/pull/4540)
- Telemetry configuration section to telemetry that is sent. [EventStore#4532](https://github.com/EventStore/EventStore/pull/4532)
- License headers to files. [EventStore#4487](https://github.com/EventStore/EventStore/pull/4487)
- Make sure all systems that require a shutdown process complete before exiting. [EventStore#4403](https://github.com/EventStore/EventStore/pull/4403)
- Licence header to source files. [EventStore#4455](https://github.com/EventStore/EventStore/pull/4455)
- Chunk read distribution metric. [EventStore#4445](https://github.com/EventStore/EventStore/pull/4445)
- Operating System to telemetry. [EventStore#4443](https://github.com/EventStore/EventStore/pull/4443)
- `GET /admin/scavenge/last` endpoint. [EventStore#4419](https://github.com/EventStore/EventStore/pull/4419)
- Groundwork for Archive. [EventStore#4427](https://github.com/EventStore/EventStore/pull/4427) and [EventStore#4417](https://github.com/EventStore/EventStore/pull/4417)
- Handling for missing labels in `metricsconfig.json`. [EventStore#4420](https://github.com/EventStore/EventStore/pull/4420)
- Facilitate node-to-node communication over HTTP for plugins. [EventStore#4409](https://github.com/EventStore/EventStore/pull/4409)
- License and Notices to build output. [EventStore#4404](https://github.com/EventStore/EventStore/pull/4404)
- License mappings for Qodana. [EventStore#4398](https://github.com/EventStore/EventStore/pull/4398)
- Qodana to CI. [EventStore#4392](https://github.com/EventStore/EventStore/pull/4392)
- A log for which auth policy plugin is being used. [EventStore#4377](https://github.com/EventStore/EventStore/pull/4377)
- Padding after the SourceContext in the console log output. [EventStore#4376](https://github.com/EventStore/EventStore/pull/4376)
- Allow loading multiple policies for authorization. [EventStore#4305](https://github.com/EventStore/EventStore/pull/4305)
- A banner linking to Event Store Navigator in the UI. [EventStore#4323](https://github.com/EventStore/EventStore/pull/4323)
- Support for chunk data transformation plugins. [EventStore#4258](https://github.com/EventStore/EventStore/pull/4258)
- Chunk version 4 (`Transformed`) file format. [EventStore#4289](https://github.com/EventStore/EventStore/pull/4289)
- Support for forward compatibility in chunks. [EventStore#4289](https://github.com/EventStore/EventStore/pull/4289)
- Added librdkafka redist package. [EventStore#4378](https://github.com/EventStore/EventStore/pull/4378)

### Changed
- Only use `AuthorizationPolicyRegistryFactory` when the `internal` authorization plugin is enabled. [EventStore#4594](https://github.com/EventStore/EventStore/pull/4594)
- Internal changes to allow bundling of MD5 plugin. [EventStore#4582](https://github.com/EventStore/EventStore/pull/4582)
- Restrict Subscriptions.ProcessMessages operations in the fallback policy. [EventStore#4570](https://github.com/EventStore/EventStore/pull/4570)
- Improved shutdown logging on license validation error. [EventStore#4565](https://github.com/EventStore/EventStore/pull/4565)
- Upgraded to [Jint v4](https://github.com/sebastienros/jint/releases/tag/v4.0.0). [EventStore#4339](https://github.com/EventStore/EventStore/pull/4339) - thanks [@lahma](https://github.com/lahma)!
- `Authorization:PolicyType` option to `Authorization:DefaultPolicyType`. [EventStore#4545](https://github.com/EventStore/EventStore/pull/4545)
- Improved shutdown logging. [EventStore#4548](https://github.com/EventStore/EventStore/pull/4548)
- Notices file to reflect package upgrades. [EventStore#4517](https://github.com/EventStore/EventStore/pull/4517)
- gRPC log level to fatal (actually keeping the same behaviour as previous versions). [EventStore#4521](https://github.com/EventStore/EventStore/pull/4521)
- Moved plugable components out of 'Plugins' section in telemetry. [EventStore#4474](https://github.com/EventStore/EventStore/pull/4474)
- Plugin configuration to no longer be nested in `Plugins` section. [EventStore#4471](https://github.com/EventStore/EventStore/pull/4471)
- (All) plugins can now be configured from the main yaml config file. [EventStore#4470](https://github.com/EventStore/EventStore/pull/4470)
- Software License to [ESLv2](https://www.eventstore.com/blog/introducing-event-store-license-v2-eslv2). [EventStore#4452](https://github.com/EventStore/EventStore/pull/4452)
- Cosmetic changes to modernize the code base. [EventStore#4429](https://github.com/EventStore/EventStore/pull/4429)
- Refactor projections to use `IPublisher` and `ISubscriber` instead of `IQueuedHandler` and `IBus`. [EventStore#4413](https://github.com/EventStore/EventStore/pull/4413)
- `InMemoryBus` becomes asynchronous dispatcher. Async handlers now can use `IAsyncHandle<T>` interface to enable async execution. [EventStore#4408](https://github.com/EventStore/EventStore/pull/4408)
- Group projection processing classes into namespaces. [EventStore#4412](https://github.com/EventStore/EventStore/pull/4412)
- Internal message bus is changed to be lock-free for better performance. Also, the change drives further perf improvements. [EventStore#4390](https://github.com/EventStore/EventStore/pull/4390)
- 3rd party license notices now in NOTICE.html. [EventStore#4402](https://github.com/EventStore/EventStore/pull/4402)
- Take whether a leader is resigning into account before the node priority when selecting the best candidate for an election. [EventStore#4371](https://github.com/EventStore/EventStore/pull/4371)
- Upgraded all serilog packages. [EventStore#4341](https://github.com/EventStore/EventStore/pull/4341)
- Disabled logger init check when debugging. [EventStore#4341](https://github.com/EventStore/EventStore/pull/4341)
- Console logging now includes SourceContext with component name. [EventStore#4341](https://github.com/EventStore/EventStore/pull/4341)
- Default log uses `EventStore` as the name. [EventStore#4341](https://github.com/EventStore/EventStore/pull/4341)
- When adding serilog to the host we now correctly clear all existing providers. [EventStore#4341](https://github.com/EventStore/EventStore/pull/4341)
- If debug, no exception is thrown if logging was not initialized. [EventStore#4341](https://github.com/EventStore/EventStore/pull/4341)
- If debug, any background service exception stops the host. [EventStore#4341](https://github.com/EventStore/EventStore/pull/4341)
- If debug, configuration tries to load appsettings.json and appsettings.Development.json. [EventStore#4341](https://github.com/EventStore/EventStore/pull/4341)
- Separated persistent subscription metrics into multiple instruments. [EventStore#4315](https://github.com/EventStore/EventStore/pull/4315)
- Separated projections metrics into multiple instruments. [EventStore#4312](https://github.com/EventStore/EventStore/pull/4312)
- Unit tests use TCP as a plugin. [EventStore#4210](https://github.com/EventStore/EventStore/pull/4210)

### Removed
- Byte order marks from source code. [EventStore#4450](https://github.com/EventStore/EventStore/pull/4450)
- Support for extremely old (V1) PTables. [EventStore#4447](https://github.com/EventStore/EventStore/pull/4447)
- Unused `TimeoutScheduler` from projections. [EventStore#4434](https://github.com/EventStore/EventStore/pull/4434)
- `IBus` interface is removed with no replacement to clearly distinguish roles of message dispatcher and message scheduler. Removed unused schedulers and related tests. [EventStore#4408](https://github.com/EventStore/EventStore/pull/4408)
- Replace the generic `PublishSubscribeDisptacher` with a more specific `ReaderSubscriptionDispatcher` in projections. [EventStore#4413](https://github.com/EventStore/EventStore/pull/4413)
- `/histogram/{name}` endpoint [EventStore#4394](https://github.com/EventStore/EventStore/pull/4394)

### Breaking Changes
- The `/histogram/{name}` endpoint has been removed. [EventStore#4394](https://github.com/EventStore/EventStore/pull/4394)
- Support v1 PTables has been removed. [EventStore#4447](https://github.com/EventStore/EventStore/pull/4447)
- Otel Exporter commercial plugin configuration has changed to be nested within the `EventStore` subsection for consistency with the other plugins. [Upgrade guide](https://developers.eventstore.com/server/v24.10/quick-start/upgrade-guide.html#breaking-changes)
- User certificates commercial plugin configuration is no longer nested in a `Plugins` subsection. [Upgrade guide](https://developers.eventstore.com/server/v24.10/quick-start/upgrade-guide.html#breaking-changes)
- External TCP API and related configuration settings have been removed. [Upgrade guide](https://developers.eventstore.com/server/v24.10/quick-start/upgrade-guide.html#breaking-changes)

## [23.10.3] - 2024-09-18

### Changed
- Upgraded all grpc and proto related packages. [EventStore#4340](https://github.com/EventStore/EventStore/pull/4340)

### Fixed
- Transitive dependency vulnerabilities with `System.Text.Json` and `System.Formats.Asn1`. [EventStore#4340](https://github.com/EventStore/EventStore/pull/4340)
- Projections shutdown timeout check is now published on the correct thread. [EventStore#4407](https://github.com/EventStore/EventStore/pull/4407)
- Don't write the database default `ProjectionExecutionTimeout` in the projection persisted state on creation. [EventStore#4423](https://github.com/EventStore/EventStore/pull/4423)
- Multistream projections don't always start properly when the input stream is truncated or deleted.[EventStore#4431](https://github.com/EventStore/EventStore/pull/4431)

## [24.6.0] - 2024-06-26

### Changed
- <internal changes> [EventStore#4202](https://github.com/EventStore/EventStore/pull/4202)
- Reduced FileHandle usage by 80%. Now 1 per chunk instead of 5+. [EventStore#4174](https://github.com/EventStore/EventStore/pull/4174)
- Automate and Unify Configuration. [EventStore#4144](https://github.com/EventStore/EventStore/pull/4144)
- Change status for incomplete scavenges from "Failed" to "Interrupted". [EventStore#4225](https://github.com/EventStore/EventStore/pull/4225)
- Wire up Authorization/Authentication/MD5 plugins to DI. [EventStore#4229](https://github.com/EventStore/EventStore/pull/4229)
- The EventStore.TestClient to use the logconfig.json from EventStore.ClusterNode. [EventStore#4243](https://github.com/EventStore/EventStore/pull/4243)
- Runtime and Process stats such as CPU Usage and Disk IO. [EventStore#4257](https://github.com/EventStore/EventStore/pull/4257)
- Open the database faster by building the midpoints for scavenged chunks later on demand. [EventStore#4214](https://github.com/EventStore/EventStore/pull/4214)
- Allow an extra chunk at startup. [EventStore#4263](https://github.com/EventStore/EventStore/pull/4263)
- Refactored Authentication and Authorization plugins. [EventStore#4266](https://github.com/EventStore/EventStore/pull/4266)
- Always replicate the chunk header. [EventStore#4211](https://github.com/EventStore/EventStore/pull/4211)
- Index merge now continues even if there is not enough memory to store the Bloom filter. [EventStore#4285](https://github.com/EventStore/EventStore/pull/4285)
- Changed telemetry service to reflect plugin changes. [EventStore#4290](https://github.com/EventStore/EventStore/pull/4290)

### Fixed
- Improve error messaging for non-existent projections in gRPC API. [EventStore#4197](https://github.com/EventStore/EventStore/pull/4197)
- Handle UUIDOption being null in gRPC calls. [EventStore#4158](https://github.com/EventStore/EventStore/pull/4158)
- TestClient package dependencies. [EventStore#4207](https://github.com/EventStore/EventStore/pull/4207)
- Error sending usage telemetry in certain circumstances. [EventStore#4231](https://github.com/EventStore/EventStore/pull/4231)
- Events written in explicit transactions via TCP can be missing from $all reads/subscriptions. [EventStore#4251](https://github.com/EventStore/EventStore/pull/4251)
- Use the advertised addresses for identifying nodes in the scavenge log. [EventStore#4247](https://github.com/EventStore/EventStore/pull/4247)
- Properly handle startup corner case where a scavenged chunk was replicated and switched in but the writer checkpoint wasn't yet updated. [EventStore#4264](https://github.com/EventStore/EventStore/pull/4264)
- Test client `wrflgrpc` no longer produces accidental idempotent writes. [EventStore#4280](https://github.com/EventStore/EventStore/pull/4280)
- Finalizer bug no longer causes process exit if a memory allocation fails. [EventStore#4284](https://github.com/EventStore/EventStore/pull/4284)
- Incorrect handling of SIGHUP. [EventStore#4293](https://github.com/EventStore/EventStore/pull/4293)
- Plugin loader wrongly loaded interfaces and abstract classes. [EventStore#4290](https://github.com/EventStore/EventStore/pull/4290)
- Version numbering of EventStore.Common.dll. [EventStore#4298](https://github.com/EventStore/EventStore/pull/4298)
- Redacted events break replication on a follower node. [EventStore#4291](https://github.com/EventStore/EventStore/pull/4291)

### Added
- Add the elections counter metric so users can set alerts if the number of elections over a certain period of time exceeds some number. [EventStore#4179](https://github.com/EventStore/EventStore/pull/4179)
- Log an error when the node certificate is not a valid server certificate. [EventStore#4249](https://github.com/EventStore/EventStore/pull/4249)
- New UnixSignalManager using PosixSignalRegistration. [EventStore#4257](https://github.com/EventStore/EventStore/pull/4257)
- EventStore.SystemRuntime project. [EventStore#4257](https://github.com/EventStore/EventStore/pull/4257)
- Add more information to the current database options API response. [EventStore#3934](https://github.com/EventStore/EventStore/pull/3934)
- Allow statusCode for health requests to be provided in query string. e.g. `/health/live?liveCode=200`. [EventStore#4268](https://github.com/EventStore/EventStore/pull/4268)
- Refactoring to support chunk data transformation. [EventStore#4217](https://github.com/EventStore/EventStore/pull/4217)
- Replicate the transform header to followers. [EventStore#4242](https://github.com/EventStore/EventStore/pull/4242)
- Chunk version 4 (Transformed) file format. [EventStore#4289](https://github.com/EventStore/EventStore/pull/4289)
- Support for forward compatibility in chunks. [EventStore#4289](https://github.com/EventStore/EventStore/pull/4289)
- Support for chunk data transformation plugins. [EventStore#4258](https://github.com/EventStore/EventStore/pull/4258)
- More read response exceptions. [EventStore#4301](https://github.com/EventStore/EventStore/pull/4301)
- New metrics for projection subsystem. [EventStore#4267](https://github.com/EventStore/EventStore/pull/4267)
- Extend metrics for Persistent Subscriptions. [EventStore#4248](https://github.com/EventStore/EventStore/pull/4248)
- A warning to the log when a projection state size becomes greater than 8 MB. [EventStore#4276](https://github.com/EventStore/EventStore/pull/4276)

### Updated
- Dockerfile for newer buildkit. [EventStore#4204](https://github.com/EventStore/EventStore/pull/4204)

### Removed
- EventStore.Common.Utils project and other unused code. [EventStore#4257](https://github.com/EventStore/EventStore/pull/4257)
- Remove redundant check which is always true in LeaderReplicationService. [EventStore#4265](https://github.com/EventStore/EventStore/pull/4265)

### Breaking Changes
- The database now restarts a second time to complete a truncation operation. [EventStore#4258](https://github.com/EventStore/EventStore/pull/4258)
- Unbuffered config setting now has no effect. Let us know if you're using this feature. [EventStore#4286](https://github.com/EventStore/EventStore/pull/4286)
- Change event type of Persistent Subscription checkpoint to `$SubscriptionCheckpoint`. [EventStore#4213](https://github.com/EventStore/EventStore/pull/4213)

## [23.10.2] - 2024-07-10

### Changed
- Upgrade to .NET 8. [EventStore#4046](https://github.com/EventStore/EventStore/pull/4046)
- Explicitly set the shutdown timeout to 5s, which was default in previous dotnet versions. Behaviour unchanged since previous release. [EventStore#4110](https://github.com/EventStore/EventStore/pull/4110)
- Index merge now continues even if there is not enough memory to store the Bloom filter. [EventStore#4296](https://github.com/EventStore/EventStore/pull/4296)

### Fixed
- Upgraded package reference for [CVE-2024-0057](https://github.com/advisories/GHSA-68w7-72jg-6qpp). [EventStore#4164](https://github.com/EventStore/EventStore/pull/4164)
- Events written in explicit transactions via TCP can be missing from $all reads/subscriptions [EventStore#4253](https://github.com/EventStore/EventStore/pull/4253)
- TestClient package dependencies [EventStore#4207](https://github.com/EventStore/EventStore/pull/4207)
- Finalizer bug no longer causes process exit if a memory allocation fails. [EventStore#4292](https://github.com/EventStore/EventStore/pull/4292)
- Redacted events break replication on a follower node. [EventStore#4304](https://github.com/EventStore/EventStore/pull/4304)
- Prevent 64-bit integer overflow in `GetMidpointIndex()` / `IsMidpointIndex()`. [EventStore#4333](https://github.com/EventStore/EventStore/pull/4333)

## [24.2.0] - 2024-02-25

### Changed
- Set the default value of `CertificateReservedNodeCommonName` to empty string [EventStore#4001](https://github.com/EventStore/EventStore/pull/4001)
- Upgrade to .NET 8. [EventStore#4046](https://github.com/EventStore/EventStore/pull/4046)
- Don't require ReadIndex in the enumerators when subscribing from $all [EventStore#4057](https://github.com/EventStore/EventStore/pull/4057)
- Simplified HTTP pipeline [EventStore#4088](https://github.com/EventStore/EventStore/pull/4088)
- Do not autosize thread count and streaminfocache size in containerized environments [EventStore#4103](https://github.com/EventStore/EventStore/pull/4103)
- Explicitly set the shutdown timeout to 5s, which was default in previous dotnet versions. Behaviour unchanged since previous release. [EventStore#4110](https://github.com/EventStore/EventStore/pull/4110)
- Re-authorize stream access in live subscriptions when stream metadata changes. [EventStore#4104](https://github.com/EventStore/EventStore/pull/4104)
- Re-authorize stream access in live subscriptions when default ACLs change. [EventStore#4116](https://github.com/EventStore/EventStore/pull/4116)
- Re-authorize subscriptions to `$all` when its stream metadata (`$$$all`) changes. [EventStore#4118](https://github.com/EventStore/EventStore/pull/4118)
- Upgrade Jint to version 3.0.0. [EventStore#4121](https://github.com/EventStore/EventStore/pull/4121)
- Updated plugin API. [EventStore#4126](https://github.com/EventStore/EventStore/pull/4126)
- Decouple the enumerators from gRPC so they can work directly with connectors. [EventStore#3998](https://github.com/EventStore/EventStore/pull/3998)

### Added
- gRPC stream subscriptions with smooth transitions between live and catchup. Subscriptions no longer drop with "consumer too slow" reason. [EventStore#4093](https://github.com/EventStore/EventStore/pull/4093)
- gRPC `$all` subscriptions with smooth transitions between live and catchup. Subscriptions no longer drop with "consumer too slow" reason. [EventStore#4117](https://github.com/EventStore/EventStore/pull/4117)
- General support for plugin configuration. [EventStore#4130](https://github.com/EventStore/EventStore/pull/4130)
- Improved support for plugins to perform authorization checks. [EventStore#4145](https://github.com/EventStore/EventStore/pull/4145)
- `$mem-gossip` memory stream. [EventStore#4123](https://github.com/EventStore/EventStore/pull/4123)
- Support for a wider range of authentication plugins. (facilitates user X.509 certificates plugin) [EventStore#4148](https://github.com/EventStore/EventStore/pull/4148)
- Support for new packaging pipeline. [EventStore#4157](https://github.com/EventStore/EventStore/pull/4157)

### Fixed
- Addressed [CVE-2024-26133](https://github.com/EventStore/EventStore/security/advisories/GHSA-6r53-v8hj-x684): Potential password leak in the EventStoreDB Projections Subsystem.
- 'Unknown' error reported to client after successful idempotent write to deleted stream. [EventStore#4059](https://github.com/EventStore/EventStore/pull/4059)
- Report same version info when using different kind of release tags (annotated or lightweight). [EventStore#4081](https://github.com/EventStore/EventStore/pull/4081)
- Calls to `/stats/replication` on a single node cluster would hang forever. [EventStore#4102](https://github.com/EventStore/EventStore/pull/4102)
- gRPC stream subscription now receives a stream deleted exception when subscribing to a tombstoned stream from `End` [EventStore#4108](https://github.com/EventStore/EventStore/pull/4108)
- gRPC stream subscription now receives `CaughtUp` message when subscribing to a non-existing or soft-deleted stream. Previously in such cases, the stream subscription enumerator was looping in catch-up mode until a new event is received (and thus it never sent `CaughtUp` to the subscription) [EventStore#4108](https://github.com/EventStore/EventStore/pull/4108)
- Initialize replication service heartbeat interval with `ReplicationHeartbeatInterval` instead of `NodeHeartbeatInterval `. [EventStore#4125](https://github.com/EventStore/EventStore/pull/4125)
- `$all` subscription enumerator returns `InvalidPosition` when subscribing at an invalid position. [EventStore#4128](https://github.com/EventStore/EventStore/pull/4128)
- Build on ARM, AnyCPU solution settings was actually using X64. [EventStore#4129](https://github.com/EventStore/EventStore/pull/4129)
- Filtered `$all` subscription enumerator returns `InvalidPosition` when subscribing at an invalid position. [EventStore#4131](https://github.com/EventStore/EventStore/pull/4131)
- upgraded package reference for CVE-2024-0057 [EventStore#4165](https://github.com/EventStore/EventStore/pull/4165)

### Removed
- Unnecessary code [EventStore#4087](https://github.com/EventStore/EventStore/pull/4087)
- Remove mentions of external TCP in the docs. [EventStore#4151](https://github.com/EventStore/EventStore/pull/4151)

### Breaking Changes

- Remove the external TCP API and related configuration options. [EventStore#4113](https://github.com/EventStore/EventStore/pull/4113) and [EventStore#4153](https://github.com/EventStore/EventStore/pull/4153)
- The following options have been removed:
    - `AdvertiseTcpPortToClientAs`
    - `DisableExternalTcpTls`
    - `EnableExternalTcp`
    - `ExtHostAdvertiseAs`
    - `ExtTcpHeartbeatInterval`
    - `ExtTcpHeartbeatTimeout`
    - `ExtTcpPort`
    - `ExtTcpPortAdvertiseAs`
    - `NodeHeartbeatInterval`
    - `NodeHeartbeatTimeout`
    - `NodeTcpPort`
    - `NodeTcpPortAdvertiseAs`
- The behaviour for filtered `$all` checkpoints has changed ([EventStore#4131](https://github.com/EventStore/EventStore/pull/4131)). Instead of receiving a checkpoint exactly after the checkpoint interval, a checkpoint will be issued at least once per checkpoint interval:
    - When live, checkpoints will still be issued after exactly checkpoint interval events.
    - When catching up, checkpoints will still be issued after exactly checkpoint interval events (except that the first checkpoint will be offset by one event).
    - When transitioning from catch-up to live, a checkpoint is issued.
    - When falling behind from live to catch-up, a checkpoint is issued.

## [23.10.1] - 2024-02-20

### Fixed
- Addressed [CVE-2024-26133](https://github.com/EventStore/EventStore/security/advisories/GHSA-6r53-v8hj-x684): Potential password leak in the EventStoreDB Projections Subsystem.

## [22.10.5] - 2024-02-20

### Fixed
- Addressed [CVE-2024-26133](https://github.com/EventStore/EventStore/security/advisories/GHSA-6r53-v8hj-x684): Potential password leak in the EventStoreDB Projections Subsystem.

## [21.10.11] - 2024-02-20

### Fixed
- Addressed [CVE-2024-26133](https://github.com/EventStore/EventStore/security/advisories/GHSA-6r53-v8hj-x684): Potential password leak in the EventStoreDB Projections Subsystem.

## [20.10.6] - 2024-02-20

### Fixed
- Addressed [CVE-2024-26133](https://github.com/EventStore/EventStore/security/advisories/GHSA-6r53-v8hj-x684): Potential password leak in the EventStoreDB Projections Subsystem.

## [22.10.4] - 2023-11-22

### Fixed
- Checkpoints of filtered $all subscription not always send on correct interval. [EventStore#4023](https://github.com/EventStore/EventStore/pull/4023)
- A way for unreplicated data to appear in a subscription or reads before being truncated [EventStore#4018](https://github.com/EventStore/EventStore/pull/4018)
- Updating a persistent subscription clears the filter [EventStore#4017](https://github.com/EventStore/EventStore/pull/4107)
- Persistent subscription error code regression introduced this release. [EventStore#3963](https://github.com/EventStore/EventStore/pull/3963)
- FilteredAllSubscription checkpoint now continues to update after becomming live. [EventStore#3734](https://github.com/EventStore/EventStore/pull/3734)

### Removed
- Extra checkpoint when subscription to $all goes live. [EventStore#4023](https://github.com/EventStore/EventStore/pull/4023)

## [21.10.10] - 2023-11-22

### Fixed
- Checkpoints of filtered $all subscription not always send on correct interval. [EventStore#4035](https://github.com/EventStore/EventStore/pull/4035)
- Patch Newtonsoft from `13.0.1` to `13.0.2` [EventStore#3679](https://github.com/EventStore/EventStore/pull/3679)
- Database checkpoints becomes inconsistent when running out of disk space [EventStore#3682](https://github.com/EventStore/EventStore/pull/3682)
- Slow persistent subscription consumer no longer slows down other subscribers [EventStore#3710](https://github.com/EventStore/EventStore/pull/3710)
- Cancel reads already in the reader queues when the gRPC call is cancelled [EventStore#3719](https://github.com/EventStore/EventStore/pull/3719)
- Downgraded an error log message that is not really an error to debug level [EventStore#3728](https://github.com/EventStore/EventStore/pull/3728)
- Sender of create / update Persistent Subscription now notified of failure [EventStore#3755](https://github.com/EventStore/EventStore/pull/3755)
- Support bloom filters for 400gb+ index files. [EventStore#3880](https://github.com/EventStore/EventStore/pull/3880)
- Bump gRPC packages for [CVE-2023-32731](https://github.com/advisories/GHSA-cfgp-2977-2fmm) [EventStore#3893](https://github.com/EventStore/EventStore/pull/3893)
- Bump System.Text.RegularExpressions for [CVE-2019-0820](https://github.com/advisories/GHSA-cmhx-cq75-c4mj) [EventStore#3893](https://github.com/EventStore/EventStore/pull/3893)
- Bump System.Drawing.Common [CVE-2021-24112](https://github.com/advisories/GHSA-rxg9-xrhp-64gj) [EventStore#3893](https://github.com/EventStore/EventStore/pull/3893)
- Bump System.Security.Cryptography.* [CVE-2022-34716](https://github.com/advisories/GHSA-2m65-m22p-9wjw) [EventStore#3893](https://github.com/EventStore/EventStore/pull/3893)
- A way for unreplicated data to appear in a subscription or reads before being truncated [EventStore#4018](https://github.com/EventStore/EventStore/pull/4018)
- Updating a persistent subscription clears the filter [EventStore#4017](https://github.com/EventStore/EventStore/pull/4017)
- Checkpoints of filtered $all subscription not always send on correct interval. [EventStore#4035](https://github.com/EventStore/EventStore/pull/4035)

### Removed
- Extra checkpoint when subscription to $all goes live. [EventStore#4035](https://github.com/EventStore/EventStore/pull/4035)
- Some redundant code. [EventStore#3722](https://github.com/EventStore/EventStore/pull/3722)


## [23.10.0] - 2023-10-13

### Breaking Changes
- Renamed PersistentConfig1 system event type to $PersistentConfig [EventStore#3932](https://github.com/EventStore/EventStore/pull/3932)
- Certain configuration parameters (mainly Interface options) have been deprecated and renamed, which would make it more clear as to what each parameter/option does. [EventStore#3907](https://github.com/EventStore/EventStore/pull/3907)

### Changed
- Always read the active chunk from memory instead of from the FileStream. [EventStore#3890](https://github.com/EventStore/EventStore/pull/3890)
- CI Unit test settings [EventStore#3943](https://github.com/EventStore/EventStore/pull/3943)
- Rename Telemetry namespace to Metrics [EventStore#3971](https://github.com/EventStore/EventStore/pull/3971)
- Configure `CertificateReservedNodeCommonName` to be the node's CN by default [EventStore#3966](https://github.com/EventStore/EventStore/pull/3966)
- Make `CertificateReservedNodeCommonName` dynamically reloadable [EventStore#3966](https://github.com/EventStore/EventStore/pull/3966)
- Log levels [EventStore#3973](https://github.com/EventStore/EventStore/pull/3973)
- Updated reference to EventStore.Plugins to 23.10.1 [EventStore#3990](https://github.com/EventStore/EventStore/pull/3990)
- No longer log misleading error about 'Max internal streams limit' when running out of file handles [EventStore#3902](https://github.com/EventStore/EventStore/pull/3902)

### Removed
- Unused code around checkpoints [EventStore#3900](https://github.com/EventStore/EventStore/pull/3900)
- Removed unused code paths [EventStore#3928](https://github.com/EventStore/EventStore/pull/3928)

### Added
- Restored OpenAPI specification for HTTP API. [EventStore#3886](https://github.com/EventStore/EventStore/pull/3886)
- "CaughtUp" gRPC control message for subscriptions when a subscription becomes live [EventStore#3899](https://github.com/EventStore/EventStore/pull/3899)
- Prevent implicit transactions from spanning multiple chunks [EventStore#3918](https://github.com/EventStore/EventStore/pull/3918)
- Documentation for FIPS 140-2 compliance [EventStore#3948](https://github.com/EventStore/EventStore/pull/3948)
- Documentation for redaction [EventStore#3949](https://github.com/EventStore/EventStore/pull/3949)
- Add steps in Documentation to update certificates [EventStore#3940](https://github.com/EventStore/EventStore/pull/3940)
- Call home database telemetry. [EventStore#3947](https://github.com/EventStore/EventStore/pull/3947)
- Support for more plugins use cases [EventStore#3984](https://github.com/EventStore/EventStore/pull/3984)
- Support for pluggable subsystems [EventStore#3986](https://github.com/EventStore/EventStore/pull/3986)
- Support clusters with nodes that have certificates with different CNs [EventStore#3960](https://github.com/EventStore/EventStore/pull/3960)
- Implement $mem-node-state in-memory stream. [EventStore#3985](https://github.com/EventStore/EventStore/pull/3985)
- Server support for unicode passwords [EventStore#3974](https://github.com/EventStore/EventStore/pull/3974)

### Fixed
- Race conditions when caching/uncaching chunks [EventStore#3930](https://github.com/EventStore/EventStore/pull/3930)
- Server now always returns a valid address when replying with a NotHandled.NotLeader response [EventStore#3869](https://github.com/EventStore/EventStore/pull/3869)
- Prevent torn transactions during replication [EventStore#3896](https://github.com/EventStore/EventStore/pull/3896)
- Checkpoints of filtered $all subscription not always sent on correct interval. [EventStore#3941](https://github.com/EventStore/EventStore/pull/3941)
- Report same version info when using different kind of release tags (annotated or lightweight). [EventStore#3950](https://github.com/EventStore/EventStore/pull/3950)
- Bug in replication test: Replica was subscribing from first epoch instead of the second one [EventStore#3975](https://github.com/EventStore/EventStore/pull/3975)
- An way for unreplicated data to appear in a subscription or reads before being truncated [EventStore#3972](https://github.com/EventStore/EventStore/pull/3972)
- Updating a persistent subscription no longer clears the filter [EventStore#3957](https://github.com/EventStore/EventStore/pull/3957)
- Cache client certificate authentication results for better performance/to make sure already established TLS connections continue to work properly. [EventStore#3966](https://github.com/EventStore/EventStore/pull/3966)
- Certificate was disposed during the call if it was not an X509Certificate2 object [EventStore#3960](https://github.com/EventStore/EventStore/pull/3960)
- Wildcard certificate names should have at least 3 domain labels [EventStore#3960](https://github.com/EventStore/EventStore/pull/3960)
- Support usernames/passwords with unicode characters in UI (https://github.com/EventStore/EventStore.UI/pull/364) [EventStore#3992](https://github.com/EventStore/EventStore/pull/3992)
- Persistent subscription error code regression introduced this release [EventStore#3996](https://github.com/EventStore/EventStore/pull/3996)

## [22.10.3] 2023-08-31

### Fixed

- Calling the admin/reloadconfig endpoint only reloaded/updated the LogLevel and the certificates were not updated on reloading the config.  [EventStore#3898](https://github.com/EventStore/EventStore/pull/3898)
- Support bloom filters for 400gb+ index files. [EventStore#3881](https://github.com/EventStore/EventStore/pull/3881)

### Changed

- CI Unit test settings [EventStore#3943](https://github.com/EventStore/EventStore/pull/3943)
- Bump gRPC packages for [CVE-2023-32731](https://nvd.nist.gov/vuln/detail/CVE-2023-32731) [EventStore#3895](https://github.com/EventStore/EventStore/pull/3895)
- Bump `System.Security.Cryptography.Pkcs` from 7.0.1 to 7.0.2. [EventStore#3874](https://github.com/EventStore/EventStore/pull/3874)

## [23.6.0] 2023-07-28

### Breaking Changes

- Disabled anonymous access by default (AllowAnonymousStreamAccess, AllowAnonymousEndpointAccess). [EventStore#3905](https://github.com/EventStore/EventStore/pull/3905)
- Remove PrepareCount and CommitCount db settings and docs. [EventStore#3858](https://github.com/EventStore/EventStore/pull/3858)

### Fixed
- Improved synchronization during TFChunk disposal. [EventStore#3674](https://github.com/EventStore/EventStore/pull/3674)
- Fix projection progress report. [EventStore#3655](https://github.com/EventStore/EventStore/pull/3655)
- FilteredAllSubscription checkpoint now continues to update after becomming live. [EventStore#3726](https://github.com/EventStore/EventStore/pull/3726)
- (EventStore.TestClient) Too strict cleanup condition causing a memory leak in rare cases when server becomes unresponsive. [EventStore#3705](https://github.com/EventStore/EventStore/pull/3705)
- (EventStore.TestClient) Cancelation of current command. [EventStore#3705](https://github.com/EventStore/EventStore/pull/3705)
- Use a separate IODispatcher for scavenge log. [EventStore#3741](https://github.com/EventStore/EventStore/pull/3741)
- Don't require an equals sign when parsing a command line argument followed by an integer. [EventStore#3757](https://github.com/EventStore/EventStore/pull/3757)
- Ignore exceptions when failing to change PTable index file permissions. [EventStore#3807](https://github.com/EventStore/EventStore/pull/3807)
- Correctly put a projection in the faulted state if the last event in the checkpoint stream is not a checkpoint. [EventStore#3816](https://github.com/EventStore/EventStore/pull/3816)
- Handle events that have been deleted from (now empty) chunks but not from the index. [EventStore#3813](https://github.com/EventStore/EventStore/pull/3813)
- Race condition in ManagedProjection code when deleting a projection. [EventStore#3812](https://github.com/EventStore/EventStore/pull/3812)
- Don't throw an error in projections if projection substreams (e.g. emitted streams, checkpoint streams etc.) do not exist when deleting a projection. [EventStore#3812](https://github.com/EventStore/EventStore/pull/3812)
- Only log that a connection to a persistent subscription has been dropped if there was a subscription running on the TCP connection. [EventStore#3824](https://github.com/EventStore/EventStore/pull/3824)
- Do not change the handlerType of a projection when it is updated. [EventStore#3823](https://github.com/EventStore/EventStore/pull/3823)
- WrongExpectedVersion error in projections when multiple projection write requests arrive within a short period (create-create, create-delete, delete-create, delete-delete). [EventStore#3817](https://github.com/EventStore/EventStore/pull/3817)
- (EventStore.TestClient) Only call `context.Success()` once to prevent early exit of command. [EventStore#3828](https://github.com/EventStore/EventStore/pull/3828)
- Prevent risk of implicit transactions being partially written when crossing a chunk boundary on the leader node. [EventStore#3808](https://github.com/EventStore/EventStore/pull/3808)
- Bump reference of System.Security.Cryptography.Pkcs for CVE-2023-29331 [EventStore#3878](https://github.com/EventStore/EventStore/pull/3878)
- Bump transitive reference of System.Security.Cryptography.Pkcs from 7.0.2. [EventStore#3877](https://github.com/EventStore/EventStore/pull/3877)
- Bump `System.Security.Cryptography.Pkcs` from 7.0.1 to 7.0.2. [EventStore#3874](https://github.com/EventStore/EventStore/pull/3874)
- Fix IsExportable check for dev certs when unning EventStore with `--dev` on Windows. [EventStore#3875](https://github.com/EventStore/EventStore/pull/3875)
- Support bloom filters for 400gb+ index files. [EventStore#3876](https://github.com/EventStore/EventStore/pull/3876)
- Bump gRPC packages for [CVE-2023-32731](https://nvd.nist.gov/vuln/detail/CVE-2023-32731) [EventStore#3889](https://github.com/EventStore/EventStore/pull/3889)
- Projections throw an out of order error for Progress changed. [EventStore#3887](https://github.com/EventStore/EventStore/pull/3887)
- Calling the admin/reloadconfig endpoint only reloaded/updated the LogLevel and the certificates were not updated on reloading the config.  [EventStore#3868](https://github.com/EventStore/EventStore/pull/3868)
- Bump gRPC packages for [CVE-2023-32731](https://nvd.nist.gov/vuln/detail/CVE-2023-32731) [EventStore#3895](https://github.com/EventStore/EventStore/pull/3895)
- Make Process CPU a UpDownCounter rather than Counter. [EventStore#3892](https://github.com/EventStore/EventStore/pull/3892)
- Bump gRPC packages for [CVE-2023-32731](https://github.com/advisories/GHSA-cfgp-2977-2fmm) [EventStore#3893](https://github.com/EventStore/EventStore/pull/3893)
- Bump System.Text.RegularExpressions for [CVE-2019-0820](https://github.com/advisories/GHSA-cmhx-cq75-c4mj) [EventStore#3893](https://github.com/EventStore/EventStore/pull/3893)
- Bump System.Drawing.Common [CVE-2021-24112](https://github.com/advisories/GHSA-rxg9-xrhp-64gj) [EventStore#3893](https://github.com/EventStore/EventStore/pull/3893)
- Bump System.Security.Cryptography.* [CVE-2022-34716](https://github.com/advisories/GHSA-2m65-m22p-9wjw) [EventStore#3893](https://github.com/EventStore/EventStore/pull/3893)
- Prevent invalid message timeouts from being set in persistent subscriptions UI. [EventStore#3906](https://github.com/EventStore/EventStore/pull/3906)
- Incorrect number of behind messages in the persistent subscriptions UI. [EventStore#3906](https://github.com/EventStore/EventStore/pull/3906)
- Improve test stability [EventStore#3908](https://github.com/EventStore/EventStore/pull/3908)
- Options API correctly hides the values of the newly-added sensitive options [EventStore#3911](https://github.com/EventStore/EventStore/pull/3911)
- Ignore unknown options when dumping startup configuration [EventStore#3917](https://github.com/EventStore/EventStore/pull/3917)
- Sender of create / update Persistent Subscription now notified of failure [EventStore#3747](https://github.com/EventStore/EventStore/pull/3747)

### Added
- Error message when user performs an CRUD operation to $all stream using HTTP API. [EventStore#3830](https://github.com/EventStore/EventStore/pull/3830)
- Source generator for dynamic message type ids. [EventStore#3684](https://github.com/EventStore/EventStore/pull/3684)
- Checkpoints metric. [EventStore#3685](https://github.com/EventStore/EventStore/pull/3685)
- Metric for tracking the current state of the Node. [EventStore#3686](https://github.com/EventStore/EventStore/pull/3686)
- Metric for tracking the current state of the Scavenge. [EventStore#3686](https://github.com/EventStore/EventStore/pull/3686)
- Metric for tracking the current state of the Index operations (merge/scavenge). [EventStore#3686](https://github.com/EventStore/EventStore/pull/3686)
- Histograms for gRPC reads and appends. [EventStore#3695](https://github.com/EventStore/EventStore/pull/3695)
- A process-wide stopwatch for measuring durations. [EventStore#3703](https://github.com/EventStore/EventStore/pull/3703)
- (EventStore.TestClient) Log errors for gRPC write flood. [EventStore#3705](https://github.com/EventStore/EventStore/pull/3705)
- Implement Init/Rebuild index status report. [EventStore#3707](https://github.com/EventStore/EventStore/pull/3707)
- Max queue durations over period per queue. [EventStore#3698](https://github.com/EventStore/EventStore/pull/3698)
- Queue Processing Duration Histograms by Message Type. [EventStore#3730](https://github.com/EventStore/EventStore/pull/3730)
- Improved warning when using an invalid delimiter in gossip seed. [EventStore#3757](https://github.com/EventStore/EventStore/pull/3757)
- Deprecation warnings for UnsafeIgnoreHardDeletes and AlwaysKeepsScavenged. [EventStore#3778](https://github.com/EventStore/EventStore/pull/3778)
- The ability to set default admin and ops passwords on first run of the database. [EventStore#3738](https://github.com/EventStore/EventStore/pull/3738)
- Suggestions for unknown/invalid configuration parameters. [EventStore#3785](https://github.com/EventStore/EventStore/pull/3785)
- Inauguration status tracker. [EventStore#3699](https://github.com/EventStore/EventStore/pull/3699)
- Support for unix sockets. [EventStore#3713](https://github.com/EventStore/EventStore/pull/3713)
- APIs for external redaction. [EventStore#3713](https://github.com/EventStore/EventStore/pull/3713)
- Default location for trusted root certs. [EventStore#3811](https://github.com/EventStore/EventStore/pull/3811)
- ES version in gossip message between nodes. [EventStore#3792](https://github.com/EventStore/EventStore/pull/3792)
- Every node will monitor versions of other alive nodes; if version mismatch detected in cluster, nodes will log this. [EventStore#3792](https://github.com/EventStore/EventStore/pull/3792)
- "/gossip" endpoint will also have ES version info.  [EventStore#3792](https://github.com/EventStore/EventStore/pull/3792)
- Handle redacted events in AtomPub. [EventStore#3793](https://github.com/EventStore/EventStore/pull/3793)
- Options to restrict EventStoreDB access for anonymous users. [EventStore#3787](https://github.com/EventStore/EventStore/pull/3787)
- Metrics for count of events being read/written and bytes being read. [EventStore#3737](https://github.com/EventStore/EventStore/pull/3737)
- Metrics for current/total/failed grpc calls. [EventStore#3825](https://github.com/EventStore/EventStore/pull/3825)
- Log of telemetry configuration on startup. [EventStore#3835](https://github.com/EventStore/EventStore/pull/3835)
- Chunk and StreamInfo cache hits/misses metrics. [EventStore#3829](https://github.com/EventStore/EventStore/pull/3829)
- Storage writer flush size/duration metrics. [EventStore#3827](https://github.com/EventStore/EventStore/pull/3827)
- Support for per projection execution timeout. [EventStore#3831](https://github.com/EventStore/EventStore/pull/3831)
- Add system, process, and connection metrics. [EventStore#3777](https://github.com/EventStore/EventStore/pull/3777)
- Add paged bytes, virtual bytes, and thread pool tasks queue length metrics. [EventStore#3838](https://github.com/EventStore/EventStore/pull/3838)
- Context when logging the telemetry config. [EventStore#3845](https://github.com/EventStore/EventStore/pull/3845)
- Collect count, size and capacity metrics from DynamicCacheManager. [EventStore#3840](https://github.com/EventStore/EventStore/pull/3840)
- Metric for queue busy/idle. [EventStore#3841](https://github.com/EventStore/EventStore/pull/3841)
- Add CG max Execution Engine Suspension duration. [EventStore#3842](https://github.com/EventStore/EventStore/pull/3842)
- (EventStore.TestClient) Read $all command `RDALLGRPC`. [EventStore#3828](https://github.com/EventStore/EventStore/pull/3828)
- Log EventStore version every 12 hours so that the version is logged in every log file. [EventStore#3853](https://github.com/EventStore/EventStore/pull/3853) and [EventStore#3882](https://github.com/EventStore/EventStore/pull/3882)
- Based on header of the private key file, ES will now accept encrypted and unencrypted PKCS8 private key files. [EventStore#3851](https://github.com/EventStore/EventStore/pull/3851)
- Support for the FIPS commercial plugin. [EventStore#3846](https://github.com/EventStore/EventStore/pull/3846)
- Polishing new metrics. [EventStore#3879](https://github.com/EventStore/EventStore/pull/3879)
- Show MiniNode logs in case test fails. [EventStore#3866](https://github.com/EventStore/EventStore/pull/3866)
- Allow setting projection execution timeout per projection through the UI. [EventStore#3906](https://github.com/EventStore/EventStore/pull/3906)
- Log error message for commonName mismatch when nodes gossip with each other. [EventStore#3814](https://github.com/EventStore/EventStore/pull/3814)
- Log warnings and errors when close to the max chunk number limit. [EventStore#3643](https://github.com/EventStore/EventStore/pull/3643)
- Log a warning if something is blocking the connection between nodes. [EventStore#3839](https://github.com/EventStore/EventStore/pull/3839)
- Log a warning when certificates are close to expiry. [EventStore#3855](https://github.com/EventStore/EventStore/pull/3855)
- New 'OverrideAnonymousGossipAccess' option which will override the 'AllowAnonymousEndpiontAccess' setting for the gossip endpoint when set to true. [EventStore#3920](https://github.com/EventStore/EventStore/pull/3920)

### Changed
- Dump configuration settings in their respective groupings at startup. [EventStore#3752](https://github.com/EventStore/EventStore/pull/3752)
- Log more readable error message in the server logs when the SSL handshakes fail. [EventStore#3832](https://github.com/EventStore/EventStore/pull/3832)
- Improved log message for connectivity problem between nodes. [EventStore#3839](https://github.com/EventStore/EventStore/pull/3839)
- Update log level to Warning if AllowUnknownOptions sets to true. [EventStore#3848](https://github.com/EventStore/EventStore/pull/3848)
- Lower the log level of NACK logs for persistent subscriptions. [EventStore#3854](https://github.com/EventStore/EventStore/pull/3854)
- Update UI build after latest changes. [EventStore#3683](https://github.com/EventStore/EventStore/pull/3683)
- Publish messages from the persistent subscriptions IODispatcher to the Persistent Subscriptions queue rather than the main queue. [EventStore#3702](https://github.com/EventStore/EventStore/pull/3702)
- CI Unit test settings. [EventStore#3712](https://github.com/EventStore/EventStore/pull/3712)
- (EventStore.TestClient) Log available commands separately. [EventStore#3705](https://github.com/EventStore/EventStore/pull/3705)
- Adjustments to reduce allocations. [EventStore#3731](https://github.com/EventStore/EventStore/pull/3731)
- (EventStore.TestClient) Support specifying values with a metric prefix with the `testclient`. [EventStore#3748](https://github.com/EventStore/EventStore/pull/3748)
- Update Jint library version to 3.0.0-beta-2048. [EventStore#3788](https://github.com/EventStore/EventStore/pull/3788)
- Minor adjustments in solution file. [EventStore#3798](https://github.com/EventStore/EventStore/pull/3798)
- Move metric configuration from arrays to dictionaries. [EventStore#3837](https://github.com/EventStore/EventStore/pull/3837)
- IO metrics using Counter instrument to ObservableCounter instrument. [EventStore#3834](https://github.com/EventStore/EventStore/pull/3834)
- Use `$GITHUB_OUTPUT` for workflow output. [EventStore#3833](https://github.com/EventStore/EventStore/pull/3833)
- Use latest `actions/checkout@v3`. [EventStore#3833](https://github.com/EventStore/EventStore/pull/3833)
- Disable queue processing metrics by default, they add a lot of histograms. [EventStore#3850](https://github.com/EventStore/EventStore/pull/3850)
- Update help output so that it matches the documentation. [EventStore#3904](https://github.com/EventStore/EventStore/pull/3904)

### Removed
- Remove options dump from server logs incase the config is incorrect. [EventStore#3847](https://github.com/EventStore/EventStore/pull/3847)
- Unnecessary allocation on read. [EventStore#3691](https://github.com/EventStore/EventStore/pull/3691)
- Some redundant code. [EventStore#3709](https://github.com/EventStore/EventStore/pull/3709)
- Cleanup V8 scripts. [EventStore#3740](https://github.com/EventStore/EventStore/pull/3740)
- Some logging to prevent flooding of console/logs. [EventStore#3828](https://github.com/EventStore/EventStore/pull/3828)
- Software licenses we no longer depend on. [EventStore#3885](https://github.com/EventStore/EventStore/pull/3885)

## [22.10.2] - 2023-05-15

### Fixed
- Handle events that have been deleted from (now empty) chunks but not from the index [EventStore#3813](https://github.com/EventStore/EventStore/pull/3813)
- Sender of create / update Persistent Subscription now notified of failure [EventStore#3747](https://github.com/EventStore/EventStore/pull/3747)

### Added
- Deprecation warnings for UnsafeIgnoreHardDeletes and AlwaysKeepsScavenged [EventStore#3778](https://github.com/EventStore/EventStore/pull/3778)

## [22.10.1] - 2023-02-13

### Fixed
- Whitespace [EventStore#3649](https://github.com/EventStore/EventStore/pull/3649)
- Patch Newtonsoft from `13.0.1` to `13.0.2` [EventStore#3677](https://github.com/EventStore/EventStore/pull/3677)
- Database checkpoints becomes inconsistent when running out of disk space [EventStore#3681](https://github.com/EventStore/EventStore/pull/3681)
- Slow persistent subscription consumer no longer slows down other subscribers [EventStore#3701](https://github.com/EventStore/EventStore/pull/3701)
- Cancel reads already in the reader queues when the gRPC call is cancelled [EventStore#3718](https://github.com/EventStore/EventStore/pull/3718)
- FilteredAllSubscription checkpoint now continues to update after becomming live [EventStore#3276](https://github.com/EventStore/EventStore/pull/3726)
- Downgraded an error log message that is not really an error to debug level [EventStore#3727](https://github.com/EventStore/EventStore/pull/3727)

### Added
- Improvements to Scavenge HTTP API (query if scavenge is running, stop any running scavenge) [EventStore#3656](https://github.com/EventStore/EventStore/pull/3656)
- ScavengeId to log context [EventStore#3656](https://github.com/EventStore/EventStore/pull/3656)
- More information to SLOW QUEUE MSG logs for reads and writes [EventStore#3706](https://github.com/EventStore/EventStore/pull/3706)

### Changed
- Use version 22.10 in the docs [EventStore#3714](https://github.com/EventStore/EventStore/pull/3714)

### Fixes
- #https://github.com/EventStore/home/issues/884 [EventStore#3694](https://github.com/EventStore/EventStore/pull/3694)

## [21.10.9] - 2022-11-21

### Added

- Dev mode option to generate and trust dev certs for a secure single node on localhost. [EventStore#3606](https://github.com/EventStore/EventStore/pull/3606)
- AllowUnknownOptions option that allows EventStoreDB to start when unknown options are present (default: false) [EventStore#3653](https://github.com/EventStore/EventStore/pull/3653)

### Fixed
- Node not going into ready state due to synchronization bug in UserManagementService [EventStore#3599](https://github.com/EventStore/EventStore/pull/3599)
- Race condition when creating a persistent subscription to a stream at the same time as creating that stream. [EventStore#3601](https://github.com/EventStore/EventStore/pull/3601)
- Quick responses for authentication requests when the node is not ready [EventStore#3609](https://github.com/EventStore/EventStore/pull/3609)
- Edge cases in maxage read fast path. [EventStore#3646](https://github.com/EventStore/EventStore/pull/3646)
- Make MergeIndexes endpoint return proper result. Fixes issue #3573  [EventStore#3573](https://github.com/EventStore/EventStore/pull/3573)
- Incorrect error message when deleting a stream using gRPC. Fixes issue #3547 [EventStore#3583](https://github.com/EventStore/EventStore/pull/3583)
- Duplicate Persistent Subscriptions in the EventStoreDB UI [EventStore#3533](https://github.com/EventStore/EventStore/pull/3533)

### Changed

- Start standard projections and atompub over http when running in dev mode [EventStore#3615](https://github.com/EventStore/EventStore/pull/3615)
- Display node status as 'Unreachable' instead of 'Dead' in the cluster status page [EventStore#3612](https://github.com/EventStore/EventStore/pull/3612)

## [22.10.0] - 2022-11-10

### Security
- Updated Newtonsoft.Json from 11.0.2 to 13.0.1. [EventStore#3507](https://github.com/EventStore/EventStore/pull/3507)
- Upgrade sqlite to v3.39.2 for [CVE-2022-35737](https://github.com/advisories/GHSA-jw36-hf63-69r9) [EventStore#3631](https://github.com/EventStore/EventStore/pull/3631)

### Added
- New scavenge algorithm [EventStore#3520](https://github.com/EventStore/EventStore/pull/3520)
- Support for custom log templates [EventStore#3577](https://github.com/EventStore/EventStore/pull/3577)
- Support for logical chunk numbers up to 400k [EventStore#3589](https://github.com/EventStore/EventStore/pull/3589)
- Logging when server receives gRPC calls that are retries [EventStore#3588](https://github.com/EventStore/EventStore/pull/3588)
- CI: run tests in target runtime container [EventStore#3597](https://github.com/EventStore/EventStore/pull/3597)
- support for .NET 6.0 [EventStore#3595](https://github.com/EventStore/EventStore/pull/3595)
- Dynamically resize stream info caches based on available memory [EventStore#3569](https://github.com/EventStore/EventStore/pull/3569)
- Dev mode option to generate and trust dev certs for a secure single node on localhost. [EventStore#3606](https://github.com/EventStore/EventStore/pull/3606)
- Tuning new scavenge based on cloud tests [EventStore#3645](https://github.com/EventStore/EventStore/pull/3645)
- AllowUnknownOptions option that allows EventStoreDB to start when unknown options are present (default: false) [EventStore#3653](https://github.com/EventStore/EventStore/pull/3653)

### Fixed
- Make MergeIndexes endpoint return proper result. Fixes issue #3573  [EventStore#3573](https://github.com/EventStore/EventStore/pull/3573)
- gRPC calls can no longer stall if received during server initialization. [EventStore#3584](https://github.com/EventStore/EventStore/pull/3584)
- Incorrect error message when deleting a stream using gRPC. Fixes issue #3547 [EventStore#3583](https://github.com/EventStore/EventStore/pull/3583)
- Node not going into ready state due to synchronization bug in UserManagementService [EventStore#3599](https://github.com/EventStore/EventStore/pull/3599)
- Race condition when creating a persistent subscription to a stream at the same time as creating that stream. [EventStore#3601](https://github.com/EventStore/EventStore/pull/3601)
- Quick responses for authentication requests when the node is not ready [EventStore#3609](https://github.com/EventStore/EventStore/pull/3609)
- Exit application when hosted service shuts down [EventStore#3624](https://github.com/EventStore/EventStore/pull/3624)
- Markdown syntax error [EventStore#3629](https://github.com/EventStore/EventStore/pull/3629)
- Downgrade GitInfo due to breaking change [EventStore#3633](https://github.com/EventStore/EventStore/pull/3633)
- Duplicate Persistent Subscriptions in the EventStoreDB UI [EventStore#3533](https://github.com/EventStore/EventStore/pull/3533)
- Silenced warning `Tried to read actual position -1` when it does not represent a suspicious occurrence. [EventStore#3632](https://github.com/EventStore/EventStore/pull/3632)
- Edge cases in maxage read fast path. [EventStore#3646](https://github.com/EventStore/EventStore/pull/3646)

### Changed
- Update support for Keep Alive as per RFC. Fixes issue #3259  [EventStore#3574](https://github.com/EventStore/EventStore/pull/3574)
- Deleting persistent subscriptions to $all over HTTP is not supported [EventStore#3593](https://github.com/EventStore/EventStore/pull/3593)
- Don't catch and log `OperationCanceledException` in `PolicyAuthorizationProvider`. [EventStore#3594](https://github.com/EventStore/EventStore/pull/3594)
- Extend unit test timings slightly for tests that sometimes timeout [EventStore#3596](https://github.com/EventStore/EventStore/pull/3596)
- Start standard projections and atompub over http when running in dev mode [EventStore#3615](https://github.com/EventStore/EventStore/pull/3615)
- Display node status as 'Unreachable' instead of 'Dead' in the cluster status page [EventStore#3612](https://github.com/EventStore/EventStore/pull/3612)
- Fix typos and improve grammar on the server documentations' diagnostics page [EventStore#3616](https://github.com/EventStore/EventStore/pull/3616)
- Update EventStore.Plugins to latest version [EventStore#3619](https://github.com/EventStore/EventStore/pull/3619)

## [21.10.8] - 2022-09-16

### Security
- Updated Newtonsoft.Json from 11.0.2 to 13.0.1. [EventStore#3507](https://github.com/EventStore/EventStore/pull/3507)

### Added
- Support for custom log templates [EventStore#3577](https://github.com/EventStore/EventStore/pull/3577)
- Support for logical chunk numbers up to 400k [EventStore#3589](https://github.com/EventStore/EventStore/pull/3589)

### Fixed
- gRPC calls can no longer stall if received during server initialization. [EventStore#3584](https://github.com/EventStore/EventStore/pull/3584)
- Memory leak caused by EventPipeEventSource. [EventStore#3578](https://github.com/EventStore/EventStore/pull/3578) - Thanks [@PaskeS](https://github.com/PaskeS)!
- Double serialization of projections using $init function [EventStore#3564](https://github.com/EventStore/EventStore/pull/3564) - Thanks [@PaskeS](https://github.com/PaskeS)!

## [22.6.0] - 2022-07-21

### Changed
- Consolidate to single protobuf implementation [EventStore#3362](https://github.com/EventStore/EventStore/pull/3362)
- Updated gRPC to include fix for flaky tests [EventStore#3364](https://github.com/EventStore/EventStore/pull/3364)
- Reject gRPC call to create a projection if `TrackEmittedStreams=true` and `EmitEnabled=false` [EventStore#3412](https://github.com/EventStore/EventStore/pull/3412)
- Proto names of `emit_enabled` and `track_emitted_streams` swapped for backwards compatibility [EventStore#3412](https://github.com/EventStore/EventStore/pull/3412)
- Read operations in IODispatcher are no longer tracked if they don't have a timeoutAction [EventStore#3435](https://github.com/EventStore/EventStore/pull/3435)
- Sort the dump of the configuration settings alphabetically [EventStore#3475](https://github.com/EventStore/EventStore/pull/3475)
- Update UI for 22.6.0 [EventStore#3519](https://github.com/EventStore/EventStore/pull/3519)

### Fixed
- Update the epoch checkpoint with the proper position before going offline for truncation [EventStore#3414](https://github.com/EventStore/EventStore/pull/3414)
- Removed aggregate exception when closing a call to the stats endpoint [EventStore#3495](https://github.com/EventStore/EventStore/pull/3495)
- Possiblility of unreplicated writes appearing in reads and subscriptions [EventStore#3527](https://github.com/EventStore/EventStore/pull/3527)
- Removed unnecessary "Should never complete request twice error" [EventStore#3522](https://github.com/EventStore/EventStore/pull/3522)

### Added
- Logging around read timeouts in projection emitted streams and emitted stream trackers [EventStore#3435](https://github.com/EventStore/EventStore/pull/3435)
- Populate $all position for stream reads/subscriptions/persistent subscriptions. gRPC only. Non-transaction events only. [EventStore#3459](https://github.com/EventStore/EventStore/pull/3459)
- `TrustedRootCertificateStoreName`, `TrustedRootCertificateStoreLocation`, `TrustedRootCertificateSubjectName` and `TrustedRootCertificateThumbprint` certificate options. [EventStore#3498](https://github.com/EventStore/EventStore/pull/3498)
- Allow loading trusted root certificates from the Windows cert store. [EventStore#3498](https://github.com/EventStore/EventStore/pull/3498)
- Better summaries for CI Runs [EventStore#3496](https://github.com/EventStore/EventStore/pull/3496) - Thanks [@Tyrrrz](https://github.com/Tyrrrz)!

## [21.10.7] - 2022-07-29

### Fixed

- Prevent gRPC subscriptions from hanging while catching up [EventStore#3532](https://github.com/EventStore/EventStore/pull/3532)
- Fix "Known" and "Current" values showing as NaN in Persistent Subscriptions Commercial UI

## [21.10.6] - 2022-07-20

### Fixed
- Possiblility of unreplicated writes appearing in reads and subscriptions [EventStore#3527](https://github.com/EventStore/EventStore/pull/3527)
- Removed unnecessary "Should never complete request twice error" [EventStore#3522](https://github.com/EventStore/EventStore/pull/3522)

## [21.10.5] - 2022-06-15

This changelog includes the fixes from 21.10.3 and 21.10.4, as these were cloud-only releases.

### Added

- Support for DNS discovery with a secure cluster having only DNS SANs (including wildcard SANs). [EventStore#3460](https://github.com/EventStore/EventStore/pull/3460)
- `deadline_duration` feature for batch append. [EventStore#3454](https://github.com/EventStore/EventStore/pull/3454)
- Fast path for forward reads that start after the LastEventNumber. [EventStore#3479](https://github.com/EventStore/EventStore/pull/3479)

### Fixed

- Attempt to reconnect to the leader every second if the node fails to establish a connection (for example due to DNS lookup timeout). [EventStore#3462](https://github.com/EventStore/EventStore/pull/3462)
- Fix invalid cast when completing a scavenge after it has been interrupted. [EventStore#3478](https://github.com/EventStore/EventStore/pull/3478)
- Can now use the ptable bloom filters after an index scavenge. [EventStore#3493](https://github.com/EventStore/EventStore/pull/3493)
- Ensure no pending writes can be incorrectly acked or published when going offline for truncation. [EventStore#3502](https://github.com/EventStore/EventStore/pull/3502)
- Metadata values need to be raw json format. [EventStore#3503](https://github.com/EventStore/EventStore/pull/3503)
- Fix invalid event number error when opening a deleted event from $all in the UI. [EventStore.UI#326](https://github.com/EventStore/EventStore.UI/pull/326)
- Fix previous button showing an empty screen in the UI if there are no events. [EventStore.UI#323](https://github.com/EventStore/EventStore.UI/pull/323)
- Handle NotFound errors more gracefully in the UI. [EventStore.UI#324](https://github.com/EventStore/EventStore.UI/pull/324)
- Fix off by one error in behind message count in persistent subscriptions UI. [EventStore.UI#320](https://github.com/EventStore/EventStore.UI/pull/320)

### Changed

- Improve state serialization speed. [EventStore#3490](https://github.com/EventStore/EventStore/pull/3490)

### Removed

- Remove the red and green status dots from persistent subscriptions UI. [EventStore.UI#318](https://github.com/EventStore/EventStore.UI/pull/318)

## [21.10.4] - 2022-04-26 (Cloud only)

### Fixed
- Invalid cast when trying to complete a scavenge after the scavenge was interrupted [EventStore#3476](https://github.com/EventStore/EventStore/pull/3476)

### Added
- Fast path for forward reads that start after the LastEventNumber [EventStore#3474](https://github.com/EventStore/EventStore/pull/3474)

## [21.10.3] - 2022-04-11 (Cloud only)

### Fixed
- Attempt to reconnect to the leader every second if the node fails to establish a connection (for example due to DNS lookup timeout). [EventStore#3458](https://github.com/EventStore/EventStore/pull/3458)

### Added
- Support for DNS discovery with a secure cluster having only DNS SANs (including wildcard SANs) [EventStore#3427](https://github.com/EventStore/EventStore/pull/3427)
- deadline_duration feature for batch append [EventStore#3454](https://github.com/EventStore/EventStore/pull/3454)

## [21.10.2] - 2022-03-04

### Fixed
- Set chunk end number to the max between current end number and the added chunk number [EventStore#3365](https://github.com/EventStore/EventStore/pull/3365)
- Use the term "certificate signed by a private CA" instead of "self-signed certificates" [EventStore#3372](https://github.com/EventStore/EventStore/pull/3372)
- Directory.EnumerateFiles regression causing slower startup/truncation times on large databases [EventStore#3385](https://github.com/EventStore/EventStore/pull/3385)
- Include current stream revision on internal delete messages [EventStore#3405](https://github.com/EventStore/EventStore/pull/3405)
- Null reference exception when getting persistent subscription info [EventStore#3408](https://github.com/EventStore/EventStore/pull/3408)
- IODispatcher bug causing password change notifications to be missed sometimes [EventStore#3421](https://github.com/EventStore/EventStore/pull/3421)
- Make password changes more robust [EventStore#3429](https://github.com/EventStore/EventStore/pull/3429)
- Improved Stream Existence Filter flushes [EventStore#3425](https://github.com/EventStore/EventStore/pull/3425)
- Send full certificate chain from both server and client side during TLS handshake (requires manually adding intermediate certificates to the store) [EventStore#3446](https://github.com/EventStore/EventStore/pull/3446)
- Prevent projections subsystem from getting stuck in a stopping state due to read timeouts [EventStore#3441](https://github.com/EventStore/EventStore/pull/3441)
- Risk of PreLeader not successfully transitioning to Leader if a TCP disconnect occurs at just the right time. [EventStore#3443](https://github.com/EventStore/EventStore/pull/3443)

### Added
- Expose `emitEnabled` on creation of continuous projection. [EventStore#3384](https://github.com/EventStore/EventStore/pull/3384)
- Log GC settings on startup [EventStore#3434](https://github.com/EventStore/EventStore/pull/3434)

## [21.10.1] - 2021-12-16

### Fixed
- Exception in scheduled message callback crashes server [EventStore#3270](https://github.com/EventStore/EventStore/pull/3270)
- IODispatcher is now threadsafe for request tracking [EventStore#3270](https://github.com/EventStore/EventStore/pull/3270)
- InvalidOperationException caused by reading RequestStream after completing the PersistentSubscription gRPC call  [EventStore#3287](https://github.com/EventStore/EventStore/pull/3287)
- Return correct status code in batch append [EventStore#3295](https://github.com/EventStore/EventStore/pull/3295)
- Fix `partitionBy` not working with numbers [EventStore#3325](https://github.com/EventStore/EventStore/pull/3325)
- Fix link parsing in persistent subscription service. [EventStore#3328](https://github.com/EventStore/EventStore/pull/3328)
- MaxAge fast path: corner cases and support for SkipIndexScanOnRead [EventStore#3339](https://github.com/EventStore/EventStore/pull/3339)
- use last indexed position of all stream when consumer subscribes to all filtered live [EventStore#3342](https://github.com/EventStore/EventStore/pull/3342)
- Prevent risk of deadlock when creating a PersistentSubscriptionGroup [EventStore#3344](https://github.com/EventStore/EventStore/pull/3344)

### Added
- Support for ARM64 on Linux. [EventStore#3076](https://github.com/EventStore/EventStore/pull/3076)
- GetInfo, ReplayParked, List, RestartSubsystem operations to persistent subscription gRPC proto [EventStore#3352](https://github.com/EventStore/EventStore/pull/3352)
- String ConsumerStrategy property when creating persistent subscriptions over gRPC [EventStore#3352](https://github.com/EventStore/EventStore/pull/3352)
- Extra logging on startup [EventStore#3346](https://github.com/EventStore/EventStore/pull/3346)

### Changed
- Use file checkpoints on Linux and memory mapped checkpoints on Windows [EventStore#3340](https://github.com/EventStore/EventStore/pull/3340)
- Deprecate NamedConsumerStrategy when creating persistent subscriptions over gRPC [EventStore#3352](https://github.com/EventStore/EventStore/pull/3352)

## [20.10.5] - 2021-12-06

### Fixed

- Prevent nodes that aren't part of a cluster from pruning gossip seeds [EventStore#3116](https://github.com/EventStore/EventStore/pull/3116)
- Overflow error in persistent subscriptions extra statistics [EventStore#3162](https://github.com/EventStore/EventStore/pull/3162)
- Prevent the EpochManager from attempting to read epochs that should have been cached [EventStore#3189](https://github.com/EventStore/EventStore/pull/3189)
- Off-by-one error in Index committer service [EventStore#3196](https://github.com/EventStore/EventStore/pull/3196)
- Race condition where a node becomes leader and immediately writes to streams that have recently been written to but not[EventStore#3187](https://github.com/EventStore/EventStore/pull/3187)
- Additional Options to Logging [EventStore#3207](https://github.com/EventStore/EventStore/pull/3207)
- Object disposed exception in EventCountersHelper on shutdown [EventStore#3245](https://github.com/EventStore/EventStore/pull/3245)
- Added verification checking if a message is of NotHandled type in PersistentSubscriptions Read message handling [EventStore#3158](https://github.com/EventStore/EventStore/pull/3158)
- Breaking change in log file name by removing log rotation options [EventStore#3267](https://github.com/EventStore/EventStore/pull/3267)
- Exception in scheduled message callback crashes server [EventStore#3272](https://github.com/EventStore/EventStore/pull/3272)
- IODispatcher is now threadsafe for request tracking [EventStore#3272](https://github.com/EventStore/EventStore/pull/3272)
- Clear HeadingEventReader cache when stopping the readers [EventStore#3233](https://github.com/EventStore/EventStore/pull/3233)
- InvalidOperationException caused by reading RequestStream after completing the PersistentSubscription gRPC call [EventStore#3294](https://github.com/EventStore/EventStore/pull/3294)
- 404 error for scavenge no longer shows in UI [EventStore#3284](https://github.com/EventStore/EventStore/pull/3284)
- Replica stats show in the UI [EventStore#3284](https://github.com/EventStore/EventStore/pull/3284)
- Ensure the projections IODispatcher clears up pending requests [EventStore#3244](https://github.com/EventStore/EventStore/pull/3244)
- Use the right advertised host value in a cluster configuration [EventStore#3224](https://github.com/EventStore/EventStore/pull/3224)
- MaxAge fast path: corner cases and support for SkipIndexScanOnRead [EventStore#3330](https://github.com/EventStore/EventStore/pull/3330)
- Fix link parsing in persistent subscription service [EventStore#3328](https://github.com/EventStore/EventStore/pull/3328)
- Prevent risk of deadlock when creating a PersistentSubscriptionGroup [EventStore#3343](https://github.com/EventStore/EventStore/pull/3343)

### Changed
- Check /etc/eventstore First for Custom Configuration [EventStore#3207](https://github.com/EventStore/EventStore/pull/3207)
- Warn that projection debugging uses a different engine to the one running in Event Store [EventStore#3284](https://github.com/EventStore/EventStore/pull/3284)
- Do not download intermediate certificates from AIA URLs [EventStore#3283](https://github.com/EventStore/EventStore/pull/3283)
- Use file checkpoints on Linux and memory mapped checkpoints on Windows [EventStore#3332](https://github.com/EventStore/EventStore/pull/3332)

### Added
- LeaderElectionTimeoutMs option to allow configuring the timeout for election messages. [EventStore#3263](https://github.com/EventStore/EventStore/pull/3263)
- Support for Intermediate CA certificates [EventStore#3283](https://github.com/EventStore/EventStore/pull/3283)
- Validation of certificate chain with node's own certificate on start up [EventStore#3283](https://github.com/EventStore/EventStore/pull/3283)

## [21.10.0] - 2021-11-03

### Fixed
- HTTP port parameter in docker-compose.yaml  [EventStore#2995](https://github.com/EventStore/EventStore/pull/2995)
- WrongExpectedVersion when deleting a projection with all delete options  [EventStore#3014](https://github.com/EventStore/EventStore/pull/3014)
- Prevent emitted checkpoint streams from being deleted when not enabled. [EventStore#3030](https://github.com/EventStore/EventStore/pull/3030)
- Http authentication when a `:` is present in the user's password [EventStore#3070](https://github.com/EventStore/EventStore/pull/3070)
- Persistent subscriptions: Rename LastProcessedEventPosition to LastCheckpointedEventPosition [EventStore#3073](https://github.com/EventStore/EventStore/pull/3073)
- Potential race condition in BatchAppend [EventStore#3138](https://github.com/EventStore/EventStore/pull/3138)
- Added verification checking if a message is of NotHandled type in PersistentSubscriptions Read message handling [EventStore#3158](https://github.com/EventStore/EventStore/pull/3158)
- Issue with not handling null event data correctly in Projections [EventStore#3171](https://github.com/EventStore/EventStore/pull/3171)
- Incorrectly setting OutputState on IQuerySources when only a fold is defined in Projections [EventStore#3171](https://github.com/EventStore/EventStore/pull/3171)
- Off-by-one error in Index committer service [EventStore#3186](https://github.com/EventStore/EventStore/pull/3186)
- Node stays stuck in Leader state until next elections if a quorum never emerges [EventStore#3181](https://github.com/EventStore/EventStore/pull/3181)
- User provided metadata to be formatted correctly in Projections [EventStore#3188](https://github.com/EventStore/EventStore/pull/3188)
- Incorrect configuration file selection on linux [EventStore#3159](https://github.com/EventStore/EventStore/pull/3159)
- Prevent the EpochManager from attempting to read epochs that should have been cached [EventStore#3198](https://github.com/EventStore/EventStore/pull/3198)
- Race condition where a node becomes leader and immediately writes to streams that have recently been written to but not indexed, resulting in events with incorrect numbers in their streams. [EventStore#3201](https://github.com/EventStore/EventStore/pull/3201)
- Handling $deleted in the interpreted projections runtime. [EventStore#3216](https://github.com/EventStore/EventStore/pull/3216)
- Incorrect log message templates [EventStore#3223](https://github.com/EventStore/EventStore/pull/3223)
- LinkTo metadata not properly read in Projections [EventStore#3227](https://github.com/EventStore/EventStore/pull/3227)
- Clear HeadingEventReader cache in Projections when stopping the readers [EventStore#3233](https://github.com/EventStore/EventStore/pull/3233)
- Use the right advertised host value in a cluster configuration [EventStore#3224](https://github.com/EventStore/EventStore/pull/3224)
- Ensure the IODispatcher clears up pending requests [EventStore#3244](https://github.com/EventStore/EventStore/pull/3244)
- Replica stats page correctly shows catching up nodes [EventStore#3247](https://github.com/EventStore/EventStore/pull/3247)
- Set last checkpoint for persistent subscriptions when they are loaded [EventStore#3241](https://github.com/EventStore/EventStore/pull/3241)
- Object disposed exception in `EventCountersHelper` on shutdown [EventStore#3242](https://github.com/EventStore/EventStore/pull/3242)
- Memory Leak in Batch Append [EventStore#3253](https://github.com/EventStore/EventStore/pull/3253)
- Batch append not using requested uuid format [EventStore#3255](https://github.com/EventStore/EventStore/pull/3255)
- StreamExistenceFilter truncation corner case [EventStore#3257](https://github.com/EventStore/EventStore/pull/3257)

### Added
- Alpine Docker Image [EventStore#3069](https://github.com/EventStore/EventStore/pull/3069)
- A bloom filter to quickly check stream existence [EventStore#3078](https://github.com/EventStore/EventStore/pull/3078)
- LeaderElectionTimeoutMs option to allow configuring the timeout for election messages [EventStore#3121](https://github.com/EventStore/EventStore/pull/3121)
- Add a log message describing how to disable the stream existence filter [EventStore#3172](https://github.com/EventStore/EventStore/pull/3172)
- New options to control logging: --log-console-format, --log-file-size, --log-file-interval, --log-file-retention-count, and --disable-log-file [EventStore#3159](https://github.com/EventStore/EventStore/pull/3159)
- Event type index for LogV3 [EventStore#3114](https://github.com/EventStore/EventStore/pull/3114)
- ClientCapabilities proto for discovering which gRPC methods are available on the server [EventStore#3194](https://github.com/EventStore/EventStore/pull/3194)
- ServerFeatures proto to allow clients to discover which features are available on the server [EventStore#3251](https://github.com/EventStore/EventStore/pull/3251)
- Control message to indicate client compatibility level on reads [EventStore#3197](https://github.com/EventStore/EventStore/pull/3197)
- Bloom Filters and LRU Caches to PTables [EventStore#3161](https://github.com/EventStore/EventStore/pull/3161)
- Log level to compact json log output [EventStore#3212](https://github.com/EventStore/EventStore/pull/3212)
- Support for Intermediate CA certificates [EventStore#3176](https://github.com/EventStore/EventStore/pull/3176)
- Validation of certificate chain with node's own certificate on start up [EventStore#3176](https://github.com/EventStore/EventStore/pull/3176)

### Changed
- Added a message to timeout error on append / delete [EventStore#3054](https://github.com/EventStore/EventStore/pull/3054)
- Do not download intermediate certificates from AIA URLs [EventStore#3176](https://github.com/EventStore/EventStore/pull/3176)
- Improve stream existence bloom filter V2 initialization [EventStore#3234](https://github.com/EventStore/EventStore/pull/3234)

### Removed
- V8 projection runtime [EventStore#3193](https://github.com/EventStore/EventStore/pull/3193)

## [20.10.4] - 2021-07-22

### Added

- Configure kestrel with kestrelsettings.json [EventStore#3039](https://github.com/EventStore/EventStore/pull/3039)
- Make logconfig.json location fully configurable [EventStore#3053](https://github.com/EventStore/EventStore/pull/3053)
- Add Commit Hash to .NET Version Info At Startup [EventStore#3060](https://github.com/EventStore/EventStore/pull/3060)

### Fixed

- Improve lookup of first non-expired events in long stream with maxage [EventStore#3046](https://github.com/EventStore/EventStore/pull/3046)
- Aborted http requests are no longer logged in the authentication middleware [EventStore#3044](https://github.com/EventStore/EventStore/pull/3056)
- Fix projections getting stuck when reading from truncated streams [EventStore#3056](https://github.com/EventStore/EventStore/pull/3056)
- Prevent scavenged events from being passed to ExecuteHandler [EventStore#3055](https://github.com/EventStore/EventStore/pull/3055)

## [21.6.0] - 2021-06-24

### Added
- LogV3 abstraction points [EventStore#2907](https://github.com/EventStore/EventStore/pull/2907)
- V3 Epoch Raw Record [EventStore#2908](https://github.com/EventStore/EventStore/pull/2908)
- LogV3 PartitionType and StreamType structs and creation methods [EventStore#2918](https://github.com/EventStore/EventStore/pull/2918)
- V3 Epoch integration [EventStore#2911](https://github.com/EventStore/EventStore/pull/2911)
- EventId is now passed into projections [EventStore#2928](https://github.com/EventStore/EventStore/pull/2928)
- ISystemStreamLookup abstraction point for LogV3 [EventStore#2923](https://github.com/EventStore/EventStore/pull/2923)
- Persistent subscriptions to $all for gRPC clients [EventStore#2869](https://github.com/EventStore/EventStore/pull/2869)
- LogV3 EventType, ContentType & Partition structs and creation methods [EventStore#2931](https://github.com/EventStore/EventStore/pull/2931)
- Simple stream writes for LogV3 [EventStore#2930](https://github.com/EventStore/EventStore/pull/2930)
- TransactionStart and TransactionEnd structs for LogV3. [EventStore#2953](https://github.com/EventStore/EventStore/pull/2953)
- Implement Monitoring gRPC API. [EventStore#2932](https://github.com/EventStore/EventStore/pull/2932)
- Add the ability to configure kestrel with kestrelsettings.json [EventStore#2949](https://github.com/EventStore/EventStore/pull/2949)
- Option to switch between v2 & v3 log format [EventStore#2972](https://github.com/EventStore/EventStore/pull/2972)
- LogV3 Stream Records and Stream Name Index [EventStore#2959](https://github.com/EventStore/EventStore/pull/2959)
- Faster seek for first non-expired events in long streams with $max-age set  [EventStore#2981](https://github.com/EventStore/EventStore/pull/2981)
- auto configuration for stream cache, reader threads and worker threads. [EventStore#2902](https://github.com/EventStore/EventStore/pull/2902)
- Interpreter runtime for user projections [EventStore#2951](https://github.com/EventStore/EventStore/pull/2951)
- Options to switch user runtime back to legacy v8 [EventStore#2951](https://github.com/EventStore/EventStore/pull/2951)
- Initial creation of the LogV3 root partition. [EventStore#2982](https://github.com/EventStore/EventStore/pull/2982)
- NFIBrokerage/spear as a community gRPC client for Elixir [EventStore#2939](https://github.com/EventStore/EventStore/pull/2939)
- Make Log Configuration Path Configurable [EventStore#3002](https://github.com/EventStore/EventStore/pull/3002)

### Fixed
- Regression in TCP connection [EventStore#2834](https://github.com/EventStore/EventStore/pull/2834)
- Mutex being released on wrong thread resulting in an annoying log message on shutdown [EventStore#2838](https://github.com/EventStore/EventStore/pull/2838)
- Keep alive timeout check [EventStore#2861](https://github.com/EventStore/EventStore/pull/2861)
- TestClient not exiting after executing `--command` [EventStore#2871](https://github.com/EventStore/EventStore/pull/2871)
- Rdall for TestClient [EventStore#2892](https://github.com/EventStore/EventStore/pull/2892)
- Parsing of yaml config options specified as an array [EventStore#2906](https://github.com/EventStore/EventStore/pull/2906)
- Start projections when requested [EventStore#2929](https://github.com/EventStore/EventStore/pull/2929)
- Handle missing case for UpdatePersistentSubscriptionTo{Stream,All}Result.DoesNotExist [EventStore#2941](https://github.com/EventStore/EventStore/pull/2941)
- In gRPC projection management, disable a projection when writing a checkpoint, and abort it if not writing a checkpoint. [EventStore#2944](https://github.com/EventStore/EventStore/pull/2944)
- Parameter count mismatch when loading the dashboard in the UI [EventStore#2964](https://github.com/EventStore/EventStore/pull/2964)
- Tests failing with empty error message in `EventStore.Core.Tests.Http.Cluster.when_requesting_from_follower.*`. [EventStore#2969](https://github.com/EventStore/EventStore/pull/2969)
- Tests failing with `already exists` error because same initial values were being re-used in `EventStore.Core.Tests.ClientAPI.when_connection_drops_messages_that_have_run_out_of_retries_are_not_retried`. [EventStore#2969](https://github.com/EventStore/EventStore/pull/2969)
- Fix projections getting stuck when reading from truncated streams [EventStore#2979](https://github.com/EventStore/EventStore/pull/2979)
- Only return nodes in Follower state in tests. [EventStore#2974](https://github.com/EventStore/EventStore/pull/2974)
- Wait for node to become a leader/follower in tests. [EventStore#2974](https://github.com/EventStore/EventStore/pull/2974)
- Fix --version printing [EventStore#3004](https://github.com/EventStore/EventStore/pull/3004)
- Aborted http requests are no longer logged in the authentication middleware [EventStore#3006](https://github.com/EventStore/EventStore/pull/3006)
- Prevent scavenged events from being passed to Projections [EventStore#2966](https://github.com/EventStore/EventStore/pull/2966)
- Fix Potential Server Side Crash w/ gRPC Batch Appends [EventStore#2991](https://github.com/EventStore/EventStore/pull/2991)

### Changed
- Make Microsoft.NETFramework.ReferenceAssemblies reference private [EventStore#2859](https://github.com/EventStore/EventStore/pull/2859)
- Internal configuration system now based on `Microsoft.Extensions.Configuration` [EventStore#2833](https://github.com/EventStore/EventStore/pull/2833)
- TCP client moved from main repo to https://github.com/EventStore/EventStoreDB-Client-Dotnet-Legacy [EventStore#2863](https://github.com/EventStore/EventStore/pull/2863)
- Generalized TF and Index in preparation for LogV3 [EventStore#2889](https://github.com/EventStore/EventStore/pull/2889)
- Change the user projection runtime to use an interpreter rather than v8 [EventStore#2951](https://github.com/EventStore/EventStore/pull/2951)
- Changed Windows .dotnet prerequisite to https and changed build command [EventStore#2877](https://github.com/EventStore/EventStore/pull/2877)
- Custom kestrel default settings [EventStore#2984](https://github.com/EventStore/EventStore/pull/2984)
- Visibility and gRPC generation changes to better support testing without needing clients to be referenced [EventStore#2942](https://github.com/EventStore/EventStore/pull/2942)
- Merge sequential checks in && or || expressions [EventStore#2961](https://github.com/EventStore/EventStore/pull/2961)
- Assorted minor adjustments to V3 schema following discussions [EventStore#2958](https://github.com/EventStore/EventStore/pull/2958)
- Test names to fit the existing pattern [EventStore#2978](https://github.com/EventStore/EventStore/pull/2978)
- V3 StreamNumbers are now 32bit instead of 64bit [EventStore#2976](https://github.com/EventStore/EventStore/pull/2976)
- Allow specifying a filter when creating a persistent subscription to $all [EventStore#2970](https://github.com/EventStore/EventStore/pull/2970)

## [20.10.3] Server - 2020-04-14

### Added
- --stream-info-cache-capacity option to allow setting the cache capacity of the ReadIndex. [EventStore#2762](https://github.com/EventStore/EventStore/pull/2762)
- auto configuration for stream cache, reader threads and worker threads. [EventStore#2934](https://github.com/EventStore/EventStore/pull/2934)

### Changed
- gRPC settings [EventStore#2934](https://github.com/EventStore/EventStore/pull/2934)

## [20.10.2] Server - 2020-03-12

### Changed
- Increased the maximum chunk count to patch issue with 25 logical TB. [EventStore#2830](https://github.com/EventStore/EventStore/pull/2830)
- Updated internal dependencies and added client builds for .NET 5.0 [EventStore#2764](https://github.com/EventStore/EventStore/pull/2764)

### Fixed
- Permission Denied when performing privileged commands on a follower [EventStore#2803](https://github.com/EventStore/EventStore/pull/2803)
- --insecure has stopped working after targeting .NET 5.0 [EventStore#2779](https://github.com/EventStore/EventStore/pull/2779)
- Track retry count for persistent subscription messages after a client has lost connection. [EventStore#2797](https://github.com/EventStore/EventStore/pull/2797)
- Time out gossip discovery on the TCP client if the task does not complete [EventStore#2821](https://github.com/EventStore/EventStore/pull/2821)
- Check for old/replayed events only if the event passes the event filter in projections [EventStore#2809](https://github.com/EventStore/EventStore/pull/2809)
- Prevent a projection checkpoint from being emitted at same position twice [EventStore#2824](https://github.com/EventStore/EventStore/pull/2824)
- Proactively send heartbeat requests to the remote party if no data was sent within the last heartbeat interval [EventStore#2772](https://github.com/EventStore/EventStore/pull/2772)
- Regression in TCP connection introduced by commit: cd2aa67 from PR: #2772 [EventStore#2834][https://github.com/EventStore/EventStore/pull/2834]

### Added
- Content Type Validation to projections which will allow projections to only handle valid json events if isJson is set to true [EventStore#2812](https://github.com/EventStore/EventStore/pull/2812)

## [21.2.0] Server - 2021-02-26

### Added
- --stream-info-cache-capacity option to allow setting the cache capacity of the ReadIndex. [EventStore#2762](https://github.com/EventStore/EventStore/pull/2762)
- Parked message count is now available on persistent subscription stats [EventStore#2792](https://github.com/EventStore/EventStore/pull/2792)
- Content Type Validation to projections which will allow projections to only handle valid json events if isJson is set to true [EventStore#2812](https://github.com/EventStore/EventStore/pull/2812)
- script to check for proto changes [EventStore#2817](https://github.com/EventStore/EventStore/pull/2817)
- Server Support for gRPC Keep Alive [EventStore#2819](https://github.com/EventStore/EventStore/pull/2819)

### Changed
- Updated internal dependencies and added client builds for .NET 5.0 [EventStore#2764](https://github.com/EventStore/EventStore/pull/2764)
- GossipOnSingleNode is now on by default and the setting has been deprecated in config [EventStore#2818](https://github.com/EventStore/EventStore/pull/2818)
- Increased the maximum chunk count to patch issue with 25 logical TB.  [EventStore#2822](https://github.com/EventStore/EventStore/pull/2822)

### Fixed
- Proactively send heartbeat requests to the remote party if no data was sent within the last heartbeat interval [EventStore#2772](https://github.com/EventStore/EventStore/pull/2772)
- Linux/macOS build.sh script for .NET 5.0 [EventStore#2774](https://github.com/EventStore/EventStore/pull/2774)
- Windows build.ps1 script for .NET 5.0 [EventStore#2776](https://github.com/EventStore/EventStore/pull/2776)
- Performance counter error message on linux / macOS [EventStore#2775](https://github.com/EventStore/EventStore/pull/2775)
- --insecure has stopped working after targeting .NET 5.0 [EventStore#2779](https://github.com/EventStore/EventStore/pull/2779)
- failing test [EventStore#2788](https://github.com/EventStore/EventStore/pull/2788)
- Track retry count for persistent subscription messages after a client has lost connection. [EventStore#2797](https://github.com/EventStore/EventStore/pull/2797)
- failing test [EventStore#2800](https://github.com/EventStore/EventStore/pull/2800)
- Permission Denied when performing privileged commands on a follower [EventStore#2803](https://github.com/EventStore/EventStore/pull/2803)
- Check for old/replayed events only if the event passes the event filter [Projections] [EventStore#2809](https://github.com/EventStore/EventStore/pull/2809)
- Prevent a projection checkpoint from being emitted at same position twice [EventStore#2824](https://github.com/EventStore/EventStore/pull/2824)

## [20.10.0] - 2020-12-16

### Fixed
- Handle CORS requests first, followed by authentication provider endpoints, then legacy endpoints [EventStore#2693](https://github.com/EventStore/EventStore/pull/2693)
- Memory/disk space issues during large cascading index merges (especially when index cache depth is high) [EventStore#2700](https://github.com/EventStore/EventStore/pull/2700)
- Casting of TcpConnection when getting replication stats [EventStore#2729](https://github.com/EventStore/EventStore/pull/2729)
- Stackoverflow when sending large amounts of data over secure TCP connections [EventStore#2730](https://github.com/EventStore/EventStore/pull/2730)
- #2734 incorrectly shared operations between threads [EventStore#2747](https://github.com/EventStore/EventStore/pull/2747)
- ci.yml following set-env deprecation [EventStore#2749](https://github.com/EventStore/EventStore/pull/2749)
- EventStore/home#263 [EventStore#2745](https://github.com/EventStore/EventStore/pull/2745)
- incorrect error message in catchup subscription [EventStore#2751](https://github.com/EventStore/EventStore/pull/2751)
- Corrected `AssemblyVersion` [EventStore#2756](https://github.com/EventStore/EventStore/pull/2756)
- Incorrect error message in catchup subscription [EventStore#2751](https://github.com/EventStore/EventStore/pull/2751)
- v20 clients can discover v20.x and v5.x servers [EventStore#2719](https://github.com/EventStore/EventStore/pull/2719)

### Changed
- Read operations are now backed by System.Threading.Channels [EventStore#2712](https://github.com/EventStore/EventStore/pull/2712)
- Add the certificate subject to the log message printed when there is a certificate validation error [EventStore#2746](https://github.com/EventStore/EventStore/pull/2746)
- DNS Seeds Are No Longer Resolved to IP Addresses [EventStore#2753](https://github.com/EventStore/EventStore/pull/2753)
- Update the UI with replication stats fix [EventStore#2726](https://github.com/EventStore/EventStore/pull/2726)
- Add checkpoint based tracking of proposed epoch numbers [EventStore#2745](https://github.com/EventStore/EventStore/pull/2745)

## [20.6.1] - 2020-09-28

### Changed
- Log level from Verbose to Debug/Information for important messages [EventStore#2538](https://github.com/EventStore/EventStore/pull/2538)
- Change options that refers to disabling tls to explicitly refer to disabling tcp tls. [EventStore#2537](https://github.com/EventStore/EventStore/pull/2537)
- Adjust deprecation warning from referring to 20.02 to 20.6.0 [EventStore#2567](https://github.com/EventStore/EventStore/pull/2567)
- Do not print stack traces when an invalid configuration is encountered. [EventStore#2578](https://github.com/EventStore/EventStore/pull/2578)
- Instead of always giving system access over HTTP when running with --insecure (since no client certificate is provided), only pre-authorize the gossip and election routes with system access [EventStore#2587](https://github.com/EventStore/EventStore/pull/2587)
- Don't treat unresolved links as deleted linkTo events when checking for deleted partitions in projections. [EventStore#2586](https://github.com/EventStore/EventStore/pull/2586)
- When --dev is set, disable TLS on all interfaces instead of setting development certificates [EventStore#2581](https://github.com/EventStore/EventStore/pull/2581)
- Do not set --mem-db when --dev is set [EventStore#2581](https://github.com/EventStore/EventStore/pull/2581)
- Upgraded dotnet sdk to 3.1.301 [EventStore#2582](https://github.com/EventStore/EventStore/pull/2582)
- Enable v5 client cluster connectivity acceptance tests [EventStore#2554](https://github.com/EventStore/EventStore/pull/2554)
- Changed the default cluster gossip port from 30777 to 2113 [EventStore#2618](https://github.com/EventStore/EventStore/pull/2618)
- DisableInternalTcpTls has no effect, Insecure mode should be used to disable it [EventStore#2628](https://github.com/EventStore/EventStore/pull/2628)
- Disable authentication & authorization when --insecure is specified [EventStore#2614](https://github.com/EventStore/EventStore/pull/2614)
- gRPC reads will always try and read maxCount of events if it's not reached the end of the stream. [EventStore#2631](https://github.com/EventStore/EventStore/pull/2631)
- MessageTimeout and CheckpointAfter in persistent subscription settings are now expressed in milliseconds. [EventStore#2642](https://github.com/EventStore/EventStore/pull/2642)
- Updated startup logs to be more clear about security and interfaces. [EventStore#2656](https://github.com/EventStore/EventStore/pull/2656)
- Use AdvertiseHostToClientAs, AdvertiseHttpPortToClientAs and AdvertiseTcpPortToClientAs in the NotHandled.NotLeader response from the node. [EventStore#2665](https://github.com/EventStore/EventStore/pull/2665)
- Updated Pre-built UI to latest version [EventStore#2686](https://github.com/EventStore/EventStore/pull/2686)
- Responses to append will include expected revision / state sent from client [EventStore#2679](https://github.com/EventStore/EventStore/pull/2679)
- Update plugin version [EventStore#2690](https://github.com/EventStore/EventStore/pull/2690)

### Fixed
- Do not start other services if run is being skipped when --help or --version are specified [EventStore#2558](https://github.com/EventStore/EventStore/pull/2558)
- Prevent Stackoverflow when accepting too much data over a TCP connection on dotnet core [EventStore#2560](https://github.com/EventStore/EventStore/pull/2560)
- Improved output of CLI help [EventStore#2577](https://github.com/EventStore/EventStore/pull/2577)
- Log to default directory; args from CLI [EventStore#2574](https://github.com/EventStore/EventStore/pull/2574)
- Slow gRPC subscriptions [EventStore#2566](https://github.com/EventStore/EventStore/pull/2566)
- Handle successful link event resolution when projections emit events. [EventStore#2465](https://github.com/EventStore/EventStore/pull/2465)
- gRPC unable to read events from a truncated stream [EventStore#2631](https://github.com/EventStore/EventStore/pull/2631)
- Error on TCP operations after default user fails authentication [EventStore#2638](https://github.com/EventStore/EventStore/pull/2638)
- Wrong calculation of checkpoint interval for filtered subscriptions [EventStore#2608](https://github.com/EventStore/EventStore/pull/2608)
- Prevent gRPC errors when subscriptions are disposed [EventStore#2647](https://github.com/EventStore/EventStore/pull/2647)
- Do not do an exact check on certificate subject to match the Common Name [EventStore#2681](https://github.com/EventStore/EventStore/pull/2681)
- Removed cancellation race condition [EventStore#2682](https://github.com/EventStore/EventStore/pull/2682)
- Properly handle `LiveUntil` in `GrpcMessage.SendOverGrpc` and add a `Deadline` parameter [EventStore#2685](https://github.com/EventStore/EventStore/pull/2685)
- Requests with more than one url segment are correctly routed [EventStore#2691](https://github.com/EventStore/EventStore/pull/2691)
- Handle authentication provider endpoints first followed by legacy endpoints [EventStore#2694](https://github.com/EventStore/EventStore/pull/2694)
- Prevent clients from connecting to read only replicas which have not yet caught up [EventStore#2674](https://github.com/EventStore/EventStore/pull/2674) Thanks to @01100010011001010110010101110000 

### Added
- Option to set client certificate common name [EventStore#2572](https://github.com/EventStore/EventStore/pull/2572)
- --insecure flag to disable TLS on all interfaces (TCP & HTTP) to eliminate requirement for certificates to make it easier to run EventStoreDB [EventStore#2556](https://github.com/EventStore/EventStore/pull/2556)
- Ability to reload certificates by triggering the /admin/reloadconfig endpoint or by sending a SIGHUP signal (linux only) [EventStore#2590](https://github.com/EventStore/EventStore/pull/2590)
- Ability to load (or reload) default log level from EventStore config file [EventStore#2602](https://github.com/EventStore/EventStore/pull/2602)
- Logging around cases where the latest stream's prepare could not be read [EventStore#2613](https://github.com/EventStore/EventStore/pull/2613)
- Introduced the ability to restart the persistent subscriptions service [EventStore#2605](https://github.com/EventStore/EventStore/pull/2605)
- AdvertiseHostToClientAs, AdvertiseHttpPortToClientAs and AdvertiseTcpPortToClientAs to allow setting the gossip and TCP endpoints advertised to clients. [EventStore#2641](https://github.com/EventStore/EventStore/pull/2641)
- Docker-compose file [EventStore#2657](https://github.com/EventStore/EventStore/pull/2657)
- Allow external clients to discover supported authentication methods [EventStore#2637](https://github.com/EventStore/EventStore/pull/2637)

### Removed
- Terraform templates for generating a certificate authority and node certificates as we have an Event Store Certificate generation tool available. [EventStore#2653](https://github.com/EventStore/EventStore/pull/2653)
- Development mode [EventStore#2648](https://github.com/EventStore/EventStore/pull/2648)

## [20.6.0] - 2020-06-09

### Changed
- Update UI and submodule [EventStore#2493](https://github.com/EventStore/EventStore/pull/2493)
- gRPC Leader Not Found Exception will now return host/port in the trailers [EventStore#2491](https://github.com/EventStore/EventStore/pull/2491)
- More changes to support DNS endpoints in the Client API. [EventStore#2487](https://github.com/EventStore/EventStore/pull/2487)
- Removed UseSslConnection from the Tcp Client API Connection Settings Builder and replaced it with DisableTls and DisableServerCertificateValidation to resemble the options on the server more closely. [EventStore#2503](https://github.com/EventStore/EventStore/pull/2503)
- Set UseSslConnection=false and ValidateServer=false in connection string tests where required [EventStore#2505](https://github.com/EventStore/EventStore/pull/2505)
- Patch the version files when building the docker container so that the logs reflect that information. [EventStore#2512](https://github.com/EventStore/EventStore/pull/2512)
- Write leader's instance ID in epoch record. Pass on the epoch record's leader's instance id and each node's gossip information during elections to the leader of elections to determine more accurately if the previous leader is still alive when choosing the best leader candidate. [EventStore#2454](https://github.com/EventStore/EventStore/pull/2454)
- Updated the EventStore.Plugins version [EventStore#2521](https://github.com/EventStore/EventStore/pull/2521)
- Update Embedded Client Plugins Package to 20.6 [EventStore#2527](https://github.com/EventStore/EventStore/pull/2527)
- For gRPC use the `commit` and `prepare` positions from the `ResolvedEvent` instead of the `Transaction Position` as the `Prepare Position` and the `Log Position` in the case where the commit position is `null`. [EventStore#2522](https://github.com/EventStore/EventStore/pull/2522)
- Require IP or DNS SAN and CN=eventstoredb-node in client certificate to be assigned system role [EventStore#2513](https://github.com/EventStore/EventStore/pull/2513)
- Use CommitIndexed instead of CommitAck for the completion of the Write Request [EventStore#2529](https://github.com/EventStore/EventStore/pull/2529)
- Add the users in the $ops group the ability to restart the projection's subsystem. [EventStore#2526](https://github.com/EventStore/EventStore/pull/2526)
- Added HostStat.NET dependency to embedded client [EventStore#2534](https://github.com/EventStore/EventStore/pull/2534)
- Disable atomPub by default except when in dev mode. [EventStore#2531](https://github.com/EventStore/EventStore/pull/2531)
- Restructured stream name for future planned changes [EventStore#2530](https://github.com/EventStore/EventStore/pull/2530)

### Removed
- Internal http endpoint [EventStore#2479](https://github.com/EventStore/EventStore/pull/2479)

### Fixed
- When starting Event Store without an Index Path specified (as is the case when running in memory), the server would crash with a `NullReferenceException`. [EventStore#2502](https://github.com/EventStore/EventStore/pull/2502)
- Compiling EventStore in Debug Mode [EventStore#2509](https://github.com/EventStore/EventStore/pull/2509)
- Test client not respecting --tls-validate-server=False [EventStore#2506](https://github.com/EventStore/EventStore/pull/2506)
- Logging `Object synchronization method was called from an unsynchronized method` as a warning instead of fatal when shutting down EventStore [EventStore#2516](https://github.com/EventStore/EventStore/pull/2516)
- VNodeState in cluster.proto not matching EventStore.Core.Data.VNodeState [EventStore#2518](https://github.com/EventStore/EventStore/pull/2518)
- EventStore.Client.Embedded missing package dependencies [EventStore#2496](https://github.com/EventStore/EventStore/pull/2496)
- Correct the Java package names in protocol buffers definitions [EventStore#2535](https://github.com/EventStore/EventStore/pull/2535)

### Added
- Jwt token support [EventStore#2510](https://github.com/EventStore/EventStore/pull/2510)
- An external gRPC endpoint for gossip [EventStore#2519](https://github.com/EventStore/EventStore/pull/2519)

## [20.6.0 - Release Candidate] - 2020-05-15
### Changed
- HTTP read requests to `/streams/$scavenges/` are done via AdminController. [#2310](https://github.com/EventStore/EventStore/pull/2310)
- `/streams/$scavenges/{scavengeId}/` now maps to `/streams/$scavenges-{scavengeId}/`. [#2310](https://github.com/EventStore/EventStore/pull/2310)
- Start View Change Proof Timer on System Initialized only. [#2366](https://github.com/EventStore/EventStore/pull/2366)
- Replace byte[] with ReadOnlyMemory<byte> to reduce allocations. [#2308](https://github.com/EventStore/EventStore/pull/2308)
- Don't write $ProjectionDeleted events for queries when they're deleted [EventStore#2377](https://github.com/EventStore/EventStore/pull/2377)
- Prepared the authorization interfaces for plugin extraction [EventStore#2385](https://github.com/EventStore/EventStore/pull/2385)
- Remove unused code from MiniNode used in tests [EventStore#2401](https://github.com/EventStore/EventStore/pull/2401)
- Correct the xml documentation for the TCP Client settings builders. [EventStore#2393](https://github.com/EventStore/EventStore/pull/2393)
- Removed the constraint in AlreadyCommitted for Log Position to be positive. [EventStore#2404](https://github.com/EventStore/EventStore/pull/2404)
- Set the default for the write timeout [EventStore#2410](https://github.com/EventStore/EventStore/pull/2410)
- Replaced `UseCustomHttpClient` in the ConnectionSettingsBuilder for the TCP client with `UseCustomHttpMessageHandler` [EventStore#2419](https://github.com/EventStore/EventStore/pull/2419)
- Ensure that the leader is still in a leader state when gossip has changed. If not, start elections. [EventStore#2418](https://github.com/EventStore/EventStore/pull/2418)
- Allow a projection to be able to checkpoint regardless of whether the event filter passes. [EventStore#2428](https://github.com/EventStore/EventStore/pull/2428)
- Authorization and Authentication plugin interfaces have been removed and re-included as a nuget package from EventStore.Plugins [EventStore#2409](https://github.com/EventStore/EventStore/pull/2409)
- Authentication Provider Factory now takes a Serilog ILogger [EventStore#2409](https://github.com/EventStore/EventStore/pull/2409)
- Make read-only replicas independent of elections service and use gossip updates to determine leader changes. [EventStore#2427](https://github.com/EventStore/EventStore/pull/2427)
- Send the last replication checkpoint when a node subscribes to the leader [EventStore#2445](https://github.com/EventStore/EventStore/pull/2445)
- License information [EventStore#2439](https://github.com/EventStore/EventStore/pull/2439)
- Authorization and Authentication Plugins no longer require MEF [EventStore#2457](https://github.com/EventStore/EventStore/pull/2457)
- Appends will now return a oneof response types which currently is either a `Success` or a `WrongExpectedVersion`. [EventStore#2463](https://github.com/EventStore/EventStore/pull/2463)
- Enable the Test Client to connect to a dns or ip endpoint [EventStore#2474](https://github.com/EventStore/EventStore/pull/2474)
- `AuthToken` field and constructor added to `UserCredentials` [EventStore#2471](https://github.com/EventStore/EventStore/pull/2471)
- The connection settings for the Tcp Client now accepts Dns EndPoint as a means to connect to the nodes in the cluster. [EventStore#2480](https://github.com/EventStore/EventStore/pull/2480)
- Authenticate requests as the system user if they provide a valid client certificate with the provided trusted root certificate [EventStore#2475](https://github.com/EventStore/EventStore/pull/2475)
- Require a system user for gossip update, and for all election operations [EventStore#2475](https://github.com/EventStore/EventStore/pull/2475)
- Update UI submodule as well as the pre-built UI. [EventStore#2490](https://github.com/EventStore/EventStore/pull/2490)

### Removed
- Unused HTTP messages. [#2362](https://github.com/EventStore/EventStore/pull/2363)
- Removed better ordering option. [#2368](https://github.com/EventStore/EventStore/pull/2368)
- Application Defines [EventStore#2441](https://github.com/EventStore/EventStore/pull/2441)
- Force option [EventStore#2442](https://github.com/EventStore/EventStore/pull/2442)
- Get Gossip from the Gossip Controller as this is now done over gRPC [EventStore#2458](https://github.com/EventStore/EventStore/pull/2458)

### Added
- Supports for pull request linting and automatic changelog update [EventStore#2391](https://github.com/EventStore/EventStore/pull/2391)
- The following options have been added `AuthorizationType` and `AuthorizationConfig` to mirror that of the existing `AuthenticationType` and `AuthenticationConfig`. [EventStore#2385](https://github.com/EventStore/EventStore/pull/2385)
- A mandatory configuration parameter named: `TrustedRootCertificatesPath`. The certificate store will be expanded with the root certificates in this path before certificate validation. For server certificate validation, trust is restricted to system certificates + the specified root certificates but for client certificate validation, trust is restricted only to the specified root certificates. Before this change, all root certificates installed on the system were trusted. [EventStore#2335](https://github.com/EventStore/EventStore/pull/2335)
- Client certificate validation to the internal HTTP interface which is used for gossip/elections [EventStore#2335](https://github.com/EventStore/EventStore/pull/2335)
- Discover an existing leader through gossip updates when a read-only replica starts up instead of triggering elections. [EventStore#2417](https://github.com/EventStore/EventStore/pull/2417)
- Login endpoint (admin/login) [EventStore#2409](https://github.com/EventStore/EventStore/pull/2409)
- Features collection on the info endpoint [EventStore#2409](https://github.com/EventStore/EventStore/pull/2409)
- Operations proto contract and implementation [EventStore#2446](https://github.com/EventStore/EventStore/pull/2446)
- Max Truncation Safety Feature to avoid large unexpected truncations due to misconfiguration [EventStore#2436](https://github.com/EventStore/EventStore/pull/2436)
- New route in PersistentSubscriptionController to view parked messages /subscriptions/viewparkedmessages/{stream}/{group} [EventStore#2392](https://github.com/EventStore/EventStore/pull/2392)
- A new project `EventStore.NETCore.Compatibility` which takes the code for `System.UriTemplate` from .NET Framework 4.8 reference source (MIT-licensed) instead of depending on `SimpleSyndicate.UriTemplate` (no license) [EventStore#2439](https://github.com/EventStore/EventStore/pull/2439)
- TCP jwt authorization on the wire [EventStore#2449](https://github.com/EventStore/EventStore/pull/2449)
- Push `EventStore.ClientAPI` and `EventStore.ClientAPI.Embedded` to GitHub Package Registry [EventStore#2462](https://github.com/EventStore/EventStore/pull/2462)
- Provide the ability to specify `DnsEndPoint`s as part of gossip seeds. [EventStore#2455](https://github.com/EventStore/EventStore/pull/2455)
- Provide the ability to specify `DnsEndPoint`s as advertise information [EventStore#2455](https://github.com/EventStore/EventStore/pull/2455)
- Extended the proto contract for read responses to include Stream Not Found. [EventStore#2473](https://github.com/EventStore/EventStore/pull/2473)
- With the terminology changes we made, the class names have been changed but we just missed renaming the files. [EventStore#2383](https://github.com/EventStore/EventStore/pull/2383)
- Fix bug introduced by f87b317b78248638aba18a6173e63b809ece5d66 [EventStore#2406](https://github.com/EventStore/EventStore/pull/2406)
- When restarting each node one at a time, should keep db as the test fixture will remove the root directory. This test fails the CI quite often. [EventStore#2482](https://github.com/EventStore/EventStore/pull/2482)
- Use TrySetResult instead of SetResult so that exceptions are not thrown if setting result twice [EventStore#2406](https://github.com/EventStore/EventStore/pull/2406)

### Fixed
- Connect to existing master when a node starts up instead of triggering unnecessary elections if a quorum of nodes is already present. [EventStore#2386](https://github.com/EventStore/EventStore/pull/2386)
- Add server certificate validation when follower forwards requests over leader's external HTTP. [EventStore#2408](https://github.com/EventStore/EventStore/pull/2408)
- Fix ArgumentNullException in ByCorrelationId standard projection when the event's metadata is null. [EventStore#2430](https://github.com/EventStore/EventStore/pull/2430)
- WhatIf option will now terminate the application if set. [EventStore#2432](https://github.com/EventStore/EventStore/pull/2432)
- No longer raise an exception when reading a linked event with a bad payload. [EventStore#2424](https://github.com/EventStore/EventStore/pull/2424)
- In the TCP client, prevent the first operation from taking a huge amount of time in some situation. [EventStore#2440](https://github.com/EventStore/EventStore/pull/2440)
- Do not wait for acks/nacks after a potential subscription failure. The task might never complete. [EventStore#2437](https://github.com/EventStore/EventStore/pull/2437)
- Read-only replicas can be stuck in Subscribing to Leader mode [EventStore#2427](https://github.com/EventStore/EventStore/pull/2427)
- NodePreference.Leader is not always honored in ClusterDnsEndPointDiscoverer [EventStore#2422](https://github.com/EventStore/EventStore/pull/2422)
- Initialize a Console Logger when the application initializes [EventStore#2444](https://github.com/EventStore/EventStore/pull/2444)
- Skip emitted events during recovery if their linked event no longer exists. [EventStore#2447](https://github.com/EventStore/EventStore/pull/2447)
- Overflow bug when setting file size based on number of midpoints [EventStore#2450](https://github.com/EventStore/EventStore/pull/2450)
- Off by one in GetDepth() which can cause less midpoints to be computed when increasing IndexCacheDepth in some cases [EventStore#2450](https://github.com/EventStore/EventStore/pull/2450)
- Broken CI Build [EventStore#2466](https://github.com/EventStore/EventStore/pull/2466)
- the default ArraySegment's data is null, not an empty array. [EventStore#2486](https://github.com/EventStore/EventStore/pull/2486)

### Bug
- Event Counts in GRPC Transport no longer wrap to negative values if > int.MaxValue [EventStore#2452](https://github.com/EventStore/EventStore/pull/2452)

### Updated
- Changed the default schema on the projection and users manager to be https. [EventStore#2459](https://github.com/EventStore/EventStore/pull/2459)

## [6.0.0 - Preview 3] - 2020-03-11
The changelog below is a summary of the all of the preview releases.

### Added
- New gRPC .NET client added. 
- The ability to filter reads by regex or prefix for both stream name and event type.
- New options for certicates. Can be provided as PKCS, public/private key pair and windows store.
- Introduction of a liveness health check at `{server_address}/health/live`.
- Improvements to projection writes.
- Read-only replica. A node that will not partake in elections and is non promotable. Started with the `--read-only-replica` argument
- The ability to resign master using `{server_address}/admin/node/resign`.
- Added in the ability to set node priority with `{server_address}/admin/node/priority/{priority}`.

### Changed
- Change of cluster role names from master/slave to leader/follower.
- Both client and server have been moved over to .NET Core.

### Deprecated
- ATOM and TCP are being deprecated. These can be re-enabled with `--enable-external-tcp` and `--enable-atom-pub-over-http`.
- Clone nodes have been deprecated and replaced with read only replicas. Can be re-enabled using `--unsafe-allow-surplus-nodes`

### Removed
- Support for Event Store server on macOS.
- Undocumented projection selectors.
- The requirement for mono.

### Security
- TLS is enabled by default for internal node communication. Can be disabled by `--disable-internal-tls`.
- All external HTTP is HTTPS by default. 
