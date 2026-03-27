// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ConvertIfStatementToReturnStatement

using Eventuous;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Schema.Validation;
using KurrentDB.SchemaRegistry.Protocol.Schemas.Events;
using KurrentDB.SchemaRegistry.Services.Domain;
using KurrentDB.Protocol.Registry.V2;
using KurrentDB.SchemaRegistry.Infrastructure.Eventuous;

using static KurrentDB.SchemaRegistry.SchemaRegistryConventions.Streams;

namespace KurrentDB.SchemaRegistry.Domain;

public static class DictionaryExtensions {
    /// <summary>
    /// Compares two dictionaries to check if they contain the same key-value pairs, regardless of order.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys in the dictionaries.</typeparam>
    /// <typeparam name="TValue">The type of the values in the dictionaries.</typeparam>
    /// <param name="first">The first dictionary to compare.</param>
    /// <param name="second">The second dictionary to compare.</param>
    /// <returns>True if both dictionaries contain the same key-value pairs; otherwise, false.</returns>
    public static bool DictionaryEquals<TKey, TValue>(this IDictionary<TKey, TValue> first, IDictionary<TKey, TValue> second) {
        if (first.Count != second.Count)
            return false;

        return first.All(kvp => second.TryGetValue(kvp.Key, out var value) && EqualityComparer<TValue>.Default.Equals(kvp.Value, value));
    }
}

public class SchemaNotFound() : DomainExceptions.EntityException(nameof(SchemaNotFound));

[PublicAPI]
public class SchemaApplication : EntityApplication<SchemaEntity> {
    static readonly HashSet<string> KnownPaths = new(StringComparer.OrdinalIgnoreCase) {
        "Details.Description",
        "Details.Tags",
        "Details.Compatibility",
        "Details.DataFormat"
    };

    static readonly HashSet<string> ModifiablePaths = new(StringComparer.OrdinalIgnoreCase) {
        "Details.Description",
        "Details.Tags",
        "Details.Compatibility"
    };

    protected override Func<dynamic, string> GetEntityId    => cmd => cmd.SchemaName;
    protected override StreamTemplate        StreamTemplate => SchemasStreamTemplate;

    public SchemaApplication(ISchemaCompatibilityManager compatibilityManager, LookupSchemaNameByVersionId lookupSchemaName, GetUtcNow getUtcNow, IEventStore store) : base(store) {
        OnAny<CreateSchemaRequest>((state, cmd) => {
            if (state.IsNew) {
                return [
                    new SchemaCreated {
                        SchemaName       = cmd.SchemaName,
                        Description      = cmd.Details.Description,
                        DataFormat       = cmd.Details.DataFormat,
                        Compatibility    = cmd.Details.Compatibility,
                        Tags             = { cmd.Details.Tags },
                        SchemaVersionId  = Guid.NewGuid().ToString(),
                        SchemaDefinition = cmd.SchemaDefinition,
                        VersionNumber    = 1,
                        CreatedAt        = getUtcNow().ToTimestamp()
                    }
                ];
            }

            throw new DomainExceptions.EntityAlreadyExists("Schema", cmd.SchemaName);
        });

        OnExisting<RegisterSchemaVersionRequest>(async (state, cmd, ct) => {
            state.EnsureNotDeleted();

            var lastVersion = state.LatestVersion;
            if (lastVersion.IsSameDefinition(cmd.SchemaDefinition.Memory))
                throw new DomainExceptions.EntityException("Schema definition has not changed");

            var compatibilityMode = (SchemaCompatibilityMode)state.Compatibility;
            if (compatibilityMode is not SchemaCompatibilityMode.None) {
                state.EnsureCompatibilityCheckSupported();

                var result = await compatibilityManager.CheckCompatibility(
                    cmd.SchemaDefinition.ToStringUtf8(),
                    lastVersion.SchemaDefinition,
                    compatibilityMode,
                    ct
                );

                if (!result.IsCompatible)
                    throw new DomainExceptions.EntityException(
                        $"Schema definition is not compatible with the last version. {result.Errors.FirstOrDefault()?.Details}"
                    );
            }

            return [
                new SchemaVersionRegistered {
                    SchemaVersionId  = Guid.NewGuid().ToString(),
                    SchemaDefinition = cmd.SchemaDefinition,
                    DataFormat       = (KurrentDB.Protocol.Registry.V2.SchemaDataFormat)state.DataFormat,
                    VersionNumber    = lastVersion.VersionNumber + 1,
                    SchemaName       = cmd.SchemaName,
                    RegisteredAt     = getUtcNow().ToTimestamp()
                }
            ];
        });

        OnExisting<UpdateSchemaRequest>(async (state, cmd, ct) => {
            state.EnsureNotDeleted();

            // Ensure at least one field is being updated
            if (cmd.UpdateMask.Paths.Count == 0)
                throw new DomainExceptions.EntityException("Update mask must contain at least one field");

            // Check for unknown fields first
            var unknownField = cmd.UpdateMask.Paths.FirstOrDefault(path => !KnownPaths.Contains(path));
            if (unknownField != null)
	            throw new DomainExceptions.EntityException($"Unknown field {unknownField} in update mask");

            // Check for non-modifiable fields
            var nonModifiableField = cmd.UpdateMask.Paths.FirstOrDefault(path =>
	            KnownPaths.Contains(path) && !ModifiablePaths.Contains(path));

            if (nonModifiableField != null) {
	            var fieldDisplayName = nonModifiableField switch {
		            _ when nonModifiableField.Equals("Details.DataFormat", StringComparison.OrdinalIgnoreCase) => "DataFormat",
		            _ => nonModifiableField
	            };
	            throw new DomainExceptions.EntityNotModified("Schema", state.SchemaName, $"{fieldDisplayName} is not modifiable");
            }

            var events = new List<object>();

            foreach (var path in cmd.UpdateMask.Paths) {
                if (path.Equals("Details.Description", StringComparison.OrdinalIgnoreCase)) {
                    if (state.Description.Equals(cmd.Details.Description))
                        throw new DomainExceptions.EntityNotModified("Schema", state.SchemaName, "Description has not changed");

                    events.Add(new SchemaDescriptionUpdated {
                        SchemaName  = cmd.SchemaName,
                        Description = cmd.Details.Description,
                        UpdatedAt   = getUtcNow().ToTimestamp()
                    });
                }
                else if (path.Equals("Details.Tags", StringComparison.OrdinalIgnoreCase)) {
                    if (state.Tags.DictionaryEquals(cmd.Details.Tags))
                        throw new DomainExceptions.EntityNotModified("Schema", state.SchemaName, "Tags have not changed");

                    events.Add(new SchemaTagsUpdated {
                        SchemaName = cmd.SchemaName,
                        Tags       = { cmd.Details.Tags },
                        UpdatedAt  = getUtcNow().ToTimestamp()
                    });
                }
                else if (path.Equals("Details.Compatibility", StringComparison.OrdinalIgnoreCase)) {
                    if (state.Compatibility.Equals(cmd.Details.Compatibility))
                        throw new DomainExceptions.EntityNotModified("Schema", state.SchemaName, "Compatibility has not changed");

                    var newMode = (CompatibilityMode)cmd.Details.Compatibility;
                    if (newMode is not CompatibilityMode.None)
                        state.EnsureCompatibilityCheckSupported();
                    await ValidateCompatibilityModeChange(state, newMode, ct);

                    events.Add(new SchemaCompatibilityModeChanged {
                        SchemaName    = cmd.SchemaName,
                        Compatibility = cmd.Details.Compatibility,
                        ChangedAt     = getUtcNow().ToTimestamp()
                    });
                }
            }

            return events;
        });

        OnExisting<DeleteSchemaVersionsRequest>(
           (state, cmd) => {
                state.EnsureNotDeleted();

                // Check if any requested version doesn't exist
                var existingVersionNumbers = state.Versions.Values.Select(x => x.VersionNumber).ToHashSet();
                var nonExistentVersions    = cmd.Versions.Except(existingVersionNumbers).ToList();

                if (nonExistentVersions.Any())
                    throw new DomainExceptions.EntityException($"Schema {state.SchemaName} does not have versions: {string.Join(", ", nonExistentVersions)}");

                if (state.Versions.Count == cmd.Versions.Count)
                    throw new DomainExceptions.EntityException($"Cannot delete all versions of schema {state.SchemaName}");

                var versionsToDelete = state.Versions.Values
                    .Where(x => cmd.Versions.Contains(x.VersionNumber))
                    .Select(x => x.SchemaVersionId)
                    .ToList();

                if (state.Compatibility is CompatibilityMode.Backward) {
                    var latestVersionNumber = state.LatestVersion.VersionNumber;
                    if (cmd.Versions.Contains(latestVersionNumber))
                        throw new DomainExceptions.EntityException($"Cannot delete the latest version of schema {state.SchemaName} in Backward compatibility mode");
                }
                else if (state.Compatibility is CompatibilityMode.Forward or CompatibilityMode.Full) {
                    // Prevent deletion of any versions in Forward or Full compatibility mode
                    throw new DomainExceptions.EntityException($"Cannot delete versions of schema {state.SchemaName} in {state.Compatibility} compatibility mode");
                }

                var latestVersion = state.LatestVersion;

                return [
                    new SchemaVersionsDeleted {
                        Versions                  = { versionsToDelete },
                        SchemaName                = state.SchemaName,
                        LatestSchemaVersionId     = latestVersion.SchemaVersionId,
                        LatestSchemaVersionNumber = latestVersion.VersionNumber,
                        DeletedAt                 = getUtcNow().ToTimestamp()
                    }
                ];
            }
        );

        OnExisting<DeleteSchemaRequest>((state, cmd) => {
            state.EnsureNotDeleted();

            return [
                new SchemaDeleted {
                    SchemaName = cmd.SchemaName,
                    DeletedAt  = getUtcNow().ToTimestamp()
                }
            ];
        });

        return;

        async Task ValidateCompatibilityModeChange(SchemaEntity state, CompatibilityMode newMode, CancellationToken ct) {
            var versions = state.Versions.Values.ToList();

            switch (newMode) {
                // Ensures the latest version can read data written by any older version.
                // e.g. with versions 1, 2, 3: checks 3->1 and 3->2
                case CompatibilityMode.Backward: {
                    var latestVersion = state.LatestVersion;

                    foreach (var olderVersion in versions.Where(v => v.VersionNumber < latestVersion.VersionNumber)) {
                        var result = await compatibilityManager.CheckCompatibility(
                            latestVersion.SchemaDefinition, olderVersion.SchemaDefinition,
                            SchemaCompatibilityMode.Backward, ct
                        );

                        if (!result.IsCompatible)
                            throw new DomainExceptions.EntityException(
                                $"Cannot change to Backward compatibility mode - latest version {latestVersion.VersionNumber} "
                                + $"cannot read data from version {olderVersion.VersionNumber}. {result.Errors.FirstOrDefault()?.Details}"
                            );
                    }
                    break;
                }
                // Ensures every older version can read data written by every newer version (pairwise).
                // e.g. with versions 1, 2, 3: checks 1->2, 1->3, 2->3
                case CompatibilityMode.Forward: {
                    foreach (var olderVersion in versions) {
                        foreach (var newerVersion in versions.Where(v => v.VersionNumber > olderVersion.VersionNumber)) {
                            var result = await compatibilityManager.CheckCompatibility(
                                olderVersion.SchemaDefinition, newerVersion.SchemaDefinition,
                                SchemaCompatibilityMode.Forward, ct
                            );

                            if (!result.IsCompatible)
                                throw new DomainExceptions.EntityException(
                                    $"Cannot change to Forward compatibility mode - version {olderVersion.VersionNumber} "
                                    + $"cannot read data from version {newerVersion.VersionNumber}. {result.Errors.FirstOrDefault()?.Details}"
                                );
                        }
                    }
                    break;
                }
                // Ensures every pair of versions is compatible in both directions (pairwise, bidirectional).
                // e.g. with versions 1, 2, 3: checks 1<->2, 1<->3, 2<->3
                case CompatibilityMode.Full: {
                    foreach (var olderVersion in versions) {
                        foreach (var newerVersion in versions.Where(v => v.VersionNumber > olderVersion.VersionNumber)) {
                            var result = await compatibilityManager.CheckCompatibility(
                                olderVersion.SchemaDefinition, newerVersion.SchemaDefinition,
                                SchemaCompatibilityMode.Full, ct
                            );

                            if (!result.IsCompatible)
                                throw new DomainExceptions.EntityException(
                                    $"Cannot change to Full compatibility mode - versions {olderVersion.VersionNumber} and {newerVersion.VersionNumber} "
                                    + $"are not fully compatible with each other. {result.Errors.FirstOrDefault()?.Details}"
                                );
                        }
                    }
                    break;
                }
                // Ensures each version can read data from ALL previous versions (batch check).
                // e.g. with versions 1, 2, 3: checks 3->[1,2] and 2->[1]
                case CompatibilityMode.BackwardAll: {
                    foreach (var schema in versions) {
                        var olderVersions = versions
                            .Where(v => v.VersionNumber < schema.VersionNumber)
                            .Select(v => v.SchemaDefinition)
                            .ToList();

                        if (olderVersions.Count == 0) continue;

                        var result = await compatibilityManager.CheckCompatibility(
                            schema.SchemaDefinition, olderVersions,
                            SchemaCompatibilityMode.BackwardAll, ct
                        );

                        if (!result.IsCompatible)
                            throw new DomainExceptions.EntityException(
                                $"Cannot change to BackwardAll compatibility mode - version {schema.VersionNumber} "
                                + $"is not backward compatible with all previous versions. {result.Errors.FirstOrDefault()?.Details}"
                            );
                    }
                    break;
                }
                // Ensures each version can be read by ALL newer versions (batch check).
                // e.g. with versions 1, 2, 3: checks 1->[2,3] and 2->[3]
                case CompatibilityMode.ForwardAll: {
                    foreach (var schema in versions) {
                        var newerVersions = versions .Where(v => v.VersionNumber > schema.VersionNumber)
	                        .Select(v => v.SchemaDefinition)
                            .ToList();

                        if (newerVersions.Count == 0) continue;

                        var result = await compatibilityManager.CheckCompatibility(
                            schema.SchemaDefinition, newerVersions,
                            SchemaCompatibilityMode.ForwardAll, ct
                        );

                        if (!result.IsCompatible)
                            throw new DomainExceptions.EntityException(
                                $"Cannot change to ForwardAll compatibility mode - version {schema.VersionNumber} "
                                + $"is not forward compatible with all newer versions. {result.Errors.FirstOrDefault()?.Details}"
                            );
                    }
                    break;
                }
                // Strictest mode: ensures every version is fully compatible with ALL other versions (batch, bidirectional).
                // e.g. with versions 1, 2, 3: checks 1<->[2,3], 2<->[1,3], 3<->[1,2]
                case CompatibilityMode.FullAll: {
                    foreach (var schema in versions) {
                        var otherVersions = versions
                            .Where(v => v.VersionNumber != schema.VersionNumber)
                            .Select(v => v.SchemaDefinition)
                            .ToList();

                        if (otherVersions.Count == 0) continue;

                        var result = await compatibilityManager.CheckCompatibility(
                            schema.SchemaDefinition, otherVersions,
                            SchemaCompatibilityMode.FullAll, ct
                        );

                        if (!result.IsCompatible)
                            throw new DomainExceptions.EntityException(
                                $"Cannot change to FullAll compatibility mode - version {schema.VersionNumber} "
                                + $"is not fully compatible with all other versions. {result.Errors.FirstOrDefault()?.Details}"
                            );
                    }
                    break;
                }
            }
        }

    }
}
