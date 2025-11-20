# Ammeter

_Ammeter tests the Kurrent_

Ammeter runs integration tests against a KurrentDB server. The server can be hosted in three ways:

- Embedded
  - Ammeter hosts the server in-process.
  - Easiest debugging.
  - Useful for integration testing the source code.

- Container
  - Ammeter provisions and cleans up the specified server container.
  - Useful for testing container images.
  - Useful for regression testing.

- External
  - Ammeter connects to an existing deployment.
  - Useful for testing deployments of packages created during the release process.
  - Useful for testing new versions deployed in KurrentCloud.

Ammeter runs the same TUnit integration tests that are run during the normal CI process, so there is no duplication of test code across any of these use cases. Filter the tests to appropriate subset accordingly.

Configuration is in `testconfig.json`, and can also be passed on the command line.

The tests can run secure and insecure against any of the hosting modes.

Currently Embedded and Container spin up single nodes.

## Examples

The TUnit --treenode-filter format is /Assembly/NameSpace/Class/Test and accepts wildcards.

dotnet run --treenode-filter /*/*/*/*[KurrentCloud=true]

.\KurrentDB.Ammeter.exe --output detailed --no-progress --treenode-filter /*/*/Version*/*
