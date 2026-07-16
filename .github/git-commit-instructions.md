# Git Conventional Commits

Expert guidance for creating commit messages that follow the Conventional Commits v1.0.0 specification.

## Contents

- [Commit Message Structure](#commit-message-structure)
- [Primary Commit Types](#primary-commit-types)
- [Common Additional Types](#common-additional-types)
- [Breaking Changes](#breaking-changes)
- [Scope](#scope)
- [Writing Effective Descriptions](#writing-effective-descriptions)
- [When to Use Body](#when-to-use-body)
- [Multi-Concern Commits](#multi-concern-commits)
- [Semantic Versioning Relationships](#semantic-versioning-relationships)
- [Common Pitfalls to Avoid](#common-pitfalls-to-avoid)

## Commit Message Structure

```
<type>[optional scope]: <description>

[optional body]
```

## Primary Commit Types

**feat:** Introduces a new feature (correlates with MINOR in SemVer)
**fix:** Patches a bug (correlates with PATCH in SemVer)

## Common Additional Types

While not mandated by the specification, these types are widely adopted:

- **build:** Build system, project files, build scripts
- **chore:** Routine tasks, maintenance (no production code change)
- **ci:** CI/CD pipeline changes
- **deps:** Dependency updates (version bumps, added/removed packages)
- **docs:** Documentation changes only
- **infra:** Infrastructure, Docker, Terraform, Kubernetes, deployment
- **perf:** Performance improvements
- **refactor:** Code changes that neither fix bugs nor add features
- **revert:** Reverts a previous commit
- **security:** Vulnerability patches, dependency CVEs, auth hardening
- **style:** Code style changes (formatting, missing semicolons, etc.)
- **test:** Adding or modifying tests

## Breaking Changes

Append `!` after the type or scope to indicate a breaking change:

- `feat!:` or `feat(api)!:` or `fix!:` or `security!:`

Breaking changes correlate with MAJOR version bumps in SemVer. Always mark them clearly.

## Scope

Optional contextual information in parentheses after the type. Must be lowercase when provided.
Use dot notation for module-level precision within a package: `type(package.module):`.

## Writing Effective Descriptions

- Use past tense: "added", "fixed", "updated" (not "add", "fix", "update")
- Start with lowercase
- No period at the end
- Be specific but concise
- Limit to 50–72 characters when possible

## When to Use Body

When in doubt, include a body. Prefer more context over less.

Always use bullet points (`-`) in the body for readability.

A body is especially useful when:

- The description alone doesn't explain why the change was made
- Multiple related changes were made
- Implementation details help future understanding

## Multi-Concern Commits

When a single commit includes multiple types of changes (features, fixes, chores), use the most
significant change as the type and list the rest in the body:

```
feat: added user dashboard with profile settings

- fixed sidebar navigation overlap
- updated dependency versions
- refactored auth middleware for reuse
```

The type in the header reflects the primary change. Each additional concern gets a bullet in the
body with a lowercase prefix indicating its nature (fixed, updated, refactored, removed, etc.).

## Semantic Versioning Relationships

- **MAJOR (X.0.0):** Commits with BREAKING CHANGE (any type) or type with `!`
- **MINOR (0.X.0):** Commits with type `feat`
- **PATCH (0.0.X):** Commits with type `fix` or `perf` or `refactor` that doesn't introduce a new feature
- **No version change:** Other types (docs, chore, etc.)

## Common Pitfalls to Avoid

1. **Mixing changes:** Prefer atomic commits, but when unavoidable use the multi-concern format above
2. **Vague descriptions:** "fix stuff" or "update code" are not helpful
3. **Present tense:** "add feature" should be "added feature"
4. **Missing type:** All commits must have a type prefix
5. **Inconsistent scopes:** Use established scope names
6. **Forgetting breaking changes:** Always mark API-breaking changes
7. **Excessive scope:** Commits should be atomic and focused
