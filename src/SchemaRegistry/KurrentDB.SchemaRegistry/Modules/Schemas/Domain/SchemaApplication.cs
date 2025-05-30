using Eventuous;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Schema.Validation;
using KurrentDB.Common.Utils;
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

        return first.All(kvp => second.TryGetValue(kvp.Key, out var value)
                             && EqualityComparer<TValue>.Default.Equals(kvp.Value, value));
    }
}

public class SchemaNotFound() : DomainExceptions.EntityException(nameof(SchemaNotFound));

[PublicAPI]
public class SchemaApplication : EntityApplication<SchemaEntity> {
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

        OnExisting<RegisterSchemaVersionRequest>((state, cmd) => {
            state.EnsureNotDeleted();

            var lastVersion = state.LatestVersion;
            if (lastVersion.IsSameDefinition(cmd.SchemaDefinition.Memory))
                throw new DomainExceptions.EntityException("Schema definition has not changed");

            // check last version because if it is the same we should throw an error

            // validate
            // if (cmd.Details.DataFormat == Protocol.V2.SchemaFormat.Json) {
            //     var result =  NJsonSchemaCompatibility.CheckCompatibility(
            //         lastVersion.SchemaDefinition,
            //         cmd.SchemaDefinition.ToStringUtf8(),
            //         state.Compatibility
            //     ).GetAwaiter().GetResult();
            //
            //     if (!result.IsCompatible) {
            //         throw new DomainExceptions.EntityException($"Schema definition is not compatible with the last version. {result}");
            //     }
            //
            //     // we first validate it
            //     // then register if validation is successful
            // }

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

        OnExisting<UpdateSchemaRequest>((state, cmd) => {
            state.EnsureNotDeleted();

            // Ensure at least one field is being updated
            if (cmd.UpdateMask.Paths.Count == 0)
                throw new DomainExceptions.EntityException("Update mask must contain at least one field");

            var knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
	            "Details.Description",
	            "Details.Tags",
	            "Details.Compatibility",
	            "Details.DataFormat"
            };

            var modifiablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
	            "Details.Description",
	            "Details.Tags"
            };

            // Check for unknown fields first
            var unknownField = cmd.UpdateMask.Paths.FirstOrDefault(path => !knownPaths.Contains(path));
            if (unknownField != null)
	            throw new DomainExceptions.EntityException($"Unknown field {unknownField} in update mask");

            // Check for non-modifiable fields
            var nonModifiableField = cmd.UpdateMask.Paths.FirstOrDefault(path =>
	            knownPaths.Contains(path) && !modifiablePaths.Contains(path));

            if (nonModifiableField != null) {
	            var fieldDisplayName = nonModifiableField switch {
		            var p when p.Equals("Details.Compatibility", StringComparison.OrdinalIgnoreCase) => "Compatibility mode",
		            var p when p.Equals("Details.DataFormat", StringComparison.OrdinalIgnoreCase) => "DataFormat",
		            _ => nonModifiableField
	            };
	            throw new DomainExceptions.EntityNotModified("Schema", state.SchemaName, $"{fieldDisplayName} is not modifiable");
            }

            return cmd.UpdateMask.Paths.Aggregate(new List<object>(), (seed, path) => {
                if (path.Equals("Details.Description", StringComparison.OrdinalIgnoreCase)) {
                    if (state.Description.Equals(cmd.Details.Description))
                        throw new DomainExceptions.EntityNotModified("Schema", state.SchemaName, "Description has not changed");

                    seed.Add(new SchemaDescriptionUpdated {
                        SchemaName  = cmd.SchemaName,
                        Description = cmd.Details.Description,
                        UpdatedAt   = getUtcNow().ToTimestamp()
                    });
                }
                else if (path.Equals("Details.Tags", StringComparison.OrdinalIgnoreCase)) {
                    if (state.Tags.DictionaryEquals(cmd.Details.Tags))
                        throw new DomainExceptions.EntityNotModified("Schema", state.SchemaName, "Tags have not changed");

                    seed.Add(new SchemaTagsUpdated {
                        SchemaName = cmd.SchemaName,
                        Tags       = { cmd.Details.Tags },
                        UpdatedAt  = getUtcNow().ToTimestamp()
                    });
                }
                else if (path.Equals("Details.Compatibility", StringComparison.OrdinalIgnoreCase)) {
                    if (state.Compatibility.Equals(cmd.Details.Compatibility))
                        throw new DomainExceptions.EntityNotModified("Schema", state.SchemaName, "Compatibility have not changed");

                    // Validate compatibility requirements based on the new mode
                    var newMode = (CompatibilityMode)cmd.Details.Compatibility;

                    // Full compatibility mode validation
                    if (newMode == CompatibilityMode.Full) {
                        foreach (var olderVersion in state.Versions.Values) {
                            foreach (var newerVersion in state.Versions.Values.Where(v => v.VersionNumber > olderVersion.VersionNumber)) {
                                var olderDefinition = olderVersion.SchemaDefinition;
                                var newerDefinition = newerVersion.SchemaDefinition;

                                // Check full compatibility (bidirectional)
                                var result = compatibilityManager.CheckCompatibility(olderDefinition, newerDefinition, SchemaCompatibilityMode.Full).AsTask().GetAwaiter().GetResult();

                                if (!result.IsCompatible) {
                                    throw new DomainExceptions.EntityException(
                                        $"Cannot change to Full compatibility mode - versions {olderVersion.VersionNumber} and {newerVersion.VersionNumber} "
                                        + $"are not fully compatible with each other. {result.Errors.FirstOrDefault()?.Details}"
                                    );
                                }
                            }
                        }
                    }
                    // Forward compatibility mode validation
                    else if (newMode == CompatibilityMode.Forward) {
                        foreach (var olderVersion in state.Versions.Values) {
                            foreach (var newerVersion in state.Versions.Values.Where(v => v.VersionNumber > olderVersion.VersionNumber)) {
                                var olderDefinition = olderVersion.SchemaDefinition;
                                var newerDefinition = newerVersion.SchemaDefinition;

                                // Check forward compatibility (older can read newer)
                                var result = compatibilityManager.CheckCompatibility(olderDefinition, newerDefinition, SchemaCompatibilityMode.Forward).AsTask().GetAwaiter().GetResult();

                                if (!result.IsCompatible) {
                                    throw new DomainExceptions.EntityException(
                                        $"Cannot change to Forward compatibility mode - version {olderVersion.VersionNumber} "
                                        + $"cannot read data from version {newerVersion.VersionNumber}. {result.Errors.FirstOrDefault()?.Details}"
                                    );
                                }
                            }
                        }
                    }
                    // Backward compatibility mode validation
                    else if (newMode == CompatibilityMode.Backward) {
                        var latestVersion = state.LatestVersion;

                        foreach (var olderVersion in state.Versions.Values.Where(v => v.VersionNumber < latestVersion.VersionNumber)) {
                            var olderDefinition  = olderVersion.SchemaDefinition;
                            var latestDefinition = latestVersion.SchemaDefinition;

                            // Check backward compatibility (newer can read older)
                            var result = compatibilityManager.CheckCompatibility(latestDefinition, olderDefinition, SchemaCompatibilityMode.Backward).AsTask().GetAwaiter().GetResult();

                            if (!result.IsCompatible) {
                                throw new DomainExceptions.EntityException(
                                    $"Cannot change to Backward compatibility mode - latest version {latestVersion.VersionNumber} "
                                    + $"cannot read data from version {olderVersion.VersionNumber}. {result.Errors.FirstOrDefault()?.Details}"
                                );
                            }
                        }
                    }
                    // BackwardAll compatibility mode validation
                    else if (newMode == CompatibilityMode.BackwardAll) {
                        var versions = state.Versions.Values.ToList();
                        foreach (var schema in versions) {
	                        var olderVersions = versions
		                        .Where(v => v.VersionNumber < schema.VersionNumber)
		                        .Select(v => v.SchemaDefinition)
		                        .ToList();

	                        if (olderVersions.IsEmpty()) continue;

	                        var result = compatibilityManager
		                        .CheckCompatibility(schema.SchemaDefinition, olderVersions, SchemaCompatibilityMode.BackwardAll).AsTask().GetAwaiter()
		                        .GetResult();

	                        if (!result.IsCompatible) {
		                        throw new DomainExceptions.EntityException(
			                        $"Cannot change to BackwardAll compatibility mode - version {schema.VersionNumber} " +
			                        $"is not backward compatible with all previous versions. {result.Errors.FirstOrDefault()?.Details}"
		                        );
	                        }
                        }
                    }
                    // ForwardAll compatibility mode validation
                    else if (newMode == CompatibilityMode.ForwardAll) {
                        var versions = state.Versions.Values.ToList();
                            foreach (var schema in versions) {
                                var newerVersions = versions
                                    .Where(v => v.VersionNumber > schema.VersionNumber)
                                    .Select(v => v.SchemaDefinition)
                                    .ToList();

                                if (newerVersions.IsEmpty()) continue;

                                var result = compatibilityManager
	                                .CheckCompatibility(schema.SchemaDefinition, newerVersions, SchemaCompatibilityMode.ForwardAll).AsTask().GetAwaiter()
	                                .GetResult();

                                if (!result.IsCompatible) {
	                                throw new DomainExceptions.EntityException(
		                                $"Cannot change to ForwardAll compatibility mode - version {schema.VersionNumber} " +
		                                $"is not forward compatible with all newer versions. {result.Errors.FirstOrDefault()?.Details}"
	                                );
                                }
                            }
                    }
                    // FullAll compatibility mode validation
                    else if (newMode == CompatibilityMode.FullAll) {
                        var versions = state.Versions.Values.ToList();
                        foreach (var schema in versions) {
	                        var otherVersions = versions
		                        .Where(v => v.VersionNumber != schema.VersionNumber)
		                        .Select(v => v.SchemaDefinition)
		                        .ToList();

	                        if (otherVersions.IsEmpty()) continue;

	                        var result = compatibilityManager.CheckCompatibility(schema.SchemaDefinition, otherVersions, SchemaCompatibilityMode.FullAll)
		                        .AsTask().GetAwaiter().GetResult();

	                        if (!result.IsCompatible) {
		                        throw new DomainExceptions.EntityException(
			                        $"Cannot change to FullAll compatibility mode - version {schema.VersionNumber} " +
			                        $"is not fully compatible with all other versions. {result.Errors.FirstOrDefault()?.Details}"
		                        );
	                        }
                        }
                    }

                    seed.Add(new SchemaCompatibilityModeChanged {
                        SchemaName    = cmd.SchemaName,
                        Compatibility = cmd.Details.Compatibility,
                        ChangedAt     = getUtcNow().ToTimestamp()
                    });
                }

                return seed;
            });
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

                if (state.Compatibility == CompatibilityMode.Backward) {
                    var latestVersionNumber = state.LatestVersion.VersionNumber;
                    if (cmd.Versions.Contains(latestVersionNumber))
                        throw new DomainExceptions.EntityException($"Cannot delete the latest version of schema {state.SchemaName} in Backward compatibility mode");
                }
                else if (state.Compatibility == CompatibilityMode.Forward || state.Compatibility == CompatibilityMode.Full) {
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
    }
}
