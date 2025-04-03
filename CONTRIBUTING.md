# Contributing to KurrentDB

## Working with the Git

KurrentDB uses `master` as the main development branch. It contains all changes to the upcoming release. Older releases have dedicated feature branches with the format `release/oss-{version}`. E.g., `release/oss-v23.10`, `release/oss-v24.10`. Specific releases are tagged from the release branch commits.

We do our best to ensure a clean history. To do so, we ask contributors to squash their commits into a set or single logical commit.

**To contribute to KurrentDB**:

1. Fork the repository.
2. Create a feature branch from the `master` (or release) branch.
3. It's recommended that feature branches use a rebase strategy (see more in [Git documentation](https://git-scm.com/book/en/v2/Git-Branching-Rebasing)). We also highly recommend using clear commit messages that represent the unit of change.
4. Rebase the latest source branch from the main repository before sending PR.
5. When ready to create the Pull Request on GitHub [check to see what has previously changed](https://github.com/EventStore/EventStore/compare).

## Documentation

Documentation files are in the [`docs`](/docs) folder. They're orchestrated in the separate [documentation repository](https://github.com/EventStore/documentation). The Kurrent Documentation site is publicly accessible at https://docs.kurrent.io/.

It's recommended to have documentation changes be put together with code changes.

Kurrent supports multiple versions of the documentation. Versions are kept in:
- The main (`master`) branch: The `master` branch should contain all changes related to the upcoming release. This includes both non-released changes and enhancements to documentation for existing features.
- Specific release branches: The latest and previous releases are maintained in specific branches (e.g. `release/oss-v23.10`, `release/oss-v24.10`). We aim to keep the documentation up to date for the latest and previous LTS releases and any mid-year releases that occur in this timeline. For example, when v24.10 is released, Documentation will continue to be updated for v24.10 and v23.10 (whereas v22.10 will be updated on a "as time allows" basis). Read more on the release strategy [in our v23.10 Release notes](https://kurrent.io/blog/23.10.0-release-notes).

To update a specific database version's docs, we recommended creating a feature branch based on that versions release branch. For instance, if you want to change documentation in the `23.10` version, you would:
- Checkout `release/oss-v23.10`
- Create a new branch and add your changes
- Create a pull request targeting the `release/oss-v23.10` branch.

If you're unsure which branch to select, the safe choice is the main branch (`master`).

Multiple pull requests are not required for changes that should be reflected in multiple database version documentation. Contributors reviewing the pull request should label it (e.g., `cherry-pick:release/oss-v24.10`). KurrentDB uses the [GitHub action](/.github/workflows/cherry-pick-pr-for-label.yml) based on the labels that create pull requests with cherry-picks to the target branches. It's recommended that contributors monitor notifications and make sure that cherry-picks succeed. Read more in [action documentation](https://github.com/EventStore/Automations/tree/master/cherry-pick-pr-for-label).

Using the previous example, assume a pull request targeting the `release/oss-v23.10` was committed. The changes should also be reflected in the upcoming release and version `24.10`. The contributor should add labels to the pull request for:
- `cherry-pick:master`,
- `cherry-pick:release/oss-v24.10`.

_**Note:** Cherry-pick action requires changes to be rebased. If there is a merge commit, then cherry-pick will fail. It will also fail if there is a conflict with the target branch (so `target_branch` from label suffix). If those cases happen then, it's needed to do manual cherry-picks._

## Code style

Coding rules are described in the [.editorconfig file](/src/.editorconfig). This file is supported by all popular IDEs (e.g., Microsoft Visual Studio, Rider, and Visual Studio Code). Unless disabled manually, it should be automatically applied after opening the solution. We also recommend turning automatic formatting on saving to have all the rules applied.

## Licensing and legal rights

By contributing to KurrentDB:

1. You assert that contribution is your original work
2. You assert that you have the right to assign the copyright for the work
3. You accept the [Contributor License Agreement](https://gist.github.com/eventstore-bot/7a1e56c21e81f44a625a7462403298bf) (CLA) for your contribution
4. You accept the [License](LICENSE.md)
