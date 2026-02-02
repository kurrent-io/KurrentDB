---
dir:
  text: "Release schedule"
  order: 7
---

# Release schedule

KurrentDB production release version numbers are of the form `Major.Minor.Patch`.

* `Major`
    * Corresponds to the year in which a release was released.
* `Minor`
    * Indicates whether the release is an [LTS](#long-term-support-releases) or an [STS](#short-term-support-releases).
    * Starting with `26.0`, LTS releases have minor number `0`. Previously they had minor number `10` (e.g. `23.10`, `24.10`).
* `Patch`
    * Is incremented for bug fixes and occasionally small features.

Note that the version scheme is not `SemVer`.

Examples:

| Version  | Type | Description             |
|----------|------|-------------------------|
| `26.0.0` | LTS  | 2026 LTS release.       |
| `26.0.1` | LTS  | A patch to 26.0.0.      |
| `26.1.0` | STS  | First 2026 STS release  |
| `26.2.0` | STS  | Second 2026 STS release |
| `27.0.0` | LTS  | 2027 LTS release.       |

## Long term support releases

There will be one LTS release of KurrentDB per year, released in or around January.

These versions will be supported for a minimum of two years, with a two month grace period for organizing upgrades when the LTS goes out of support.

## Short term support releases

STS releases will be published as new features are added to KurrentDB.

These versions will be supported until the next STS or LTS release of KurrentDB.

There are typically one or two STS releases per year.
