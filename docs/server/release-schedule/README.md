---
dir:
  text: "Release schedule"
  order: 7
---

# Release schedule

## KurrentDB versioning and release schedule

There are two categories of release for KurrentDB:
* Long term support (LTS) releases.
* Short term support (STS) releases.

The version number reflects whether a release is an LTS or STS release. LTS releases have _even_ major numbers, and STS releases have _odd_ major numbers.

### Versioning scheme

The version scheme for KurrentDB is `Major.Minor.Patch` where:

* `Major`
    * Is _even_ for LTS releases.
    * Is _odd_ for STS releases.
* `Minor`
    * Increments with scope changes or new features.
    * Is typically `0` for LTS releases, but may be incremented in rare cases.
* `Patch` for bug fixes.

As an example, the future releases of KurrentDB may look like this:

| Version  | Type    | Description |
|----------|---------|-------------|
| `25.0.0` | STS     | The first release of KurrentDB. |
| `25.1.0` | STS     | New features added. |
| `26.0.0` | LTS     | The first LTS release of KurrentDB. |
| `26.0.1` | LTS     | A patch to 26.0.0. |
| `27.0.0` | STS     | New features added. |

### Long term support releases

There will be approximately one LTS release of KurrentDB per year.

These versions will be supported for a minimum of two years, with a two month grace period for organizing upgrades when the LTS goes out of support.

LTS versions of KurrentDB start with an even major number.

### Short term support releases

STS releases will be published as new features are added to KurrentDB.

These versions will be supported until the next STS or LTS release of KurrentDB.

STS versions of KurrentDB start with an odd major number.

## Supported EventStoreDB versions

EventStoreDB had a different versioning scheme to KurrentDB, where the LTS release always had a minor version of `10`.

The LTS versions of EventStoreDB will still be supported for two years from their release date. This means the following versions of EventStoreDB are still within Kurrent's support window:

* [EventStoreDB 24.10](https://docs.kurrent.io/server/v24.10/quick-start/) supported until October 2026.
* [EventStoreDB 23.10](https://docs.kurrent.io/server/v23.10/quick-start/) supported until October 2025.
