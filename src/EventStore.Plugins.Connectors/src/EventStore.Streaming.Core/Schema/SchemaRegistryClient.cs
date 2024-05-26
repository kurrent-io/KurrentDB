using System.Collections.Concurrent;
using Google.Protobuf.WellKnownTypes;

namespace EventStore.Streaming.Schema;

public interface ISchemaRegistryClient {
	Task<CreateSchema.Types.Response>         CreateSchema(CreateSchema request, CancellationToken cancellationToken);
	Task<CreateOrUpdateSchema.Types.Response> CreateOrUpdateSchema(CreateOrUpdateSchema request, CancellationToken cancellationToken);
	Task<GetLatestSchema.Types.Response>      GetLatestSchema(GetLatestSchema request, CancellationToken cancellationToken);
}

public class InMemorySchemaRegistryClient : ISchemaRegistryClient {
	// only one schema per subject and schema type pair for now until we implement the real schema registry service
	ConcurrentDictionary<(string Subject, SchemaType SchemaType), Schema> Schemas { get; } = new();

	public async Task<CreateSchema.Types.Response> CreateSchema(CreateSchema request, CancellationToken cancellationToken) {
		var schema = new Schema {
			Subject    = request.Subject,
			SchemaType = request.SchemaType,
			Revision   = new() {
				RevisionId = Guid.NewGuid().ToString(),
				Definition = request.Definition,
				Version    = 1,
				CreatedAt  = DateTimeOffset.UtcNow.ToTimestamp()
			},
			CompatibilityMode = CompatibilityMode.Full
		};

		if (!Schemas.TryAdd((request.Subject, request.SchemaType), schema))
			throw new Exception("Schema already registered");

		var response = new CreateSchema.Types.Response {
			RevisionId = schema.Revision.RevisionId,
			Version    = schema.Revision.Version,
			CreatedAt  = schema.Revision.CreatedAt
		};
		
		return response;
	}

	public Task<CreateOrUpdateSchema.Types.Response> CreateOrUpdateSchema(CreateOrUpdateSchema request, CancellationToken cancellationToken) {
		var schema = Schemas.AddOrUpdate(
			(request.Subject, request.SchemaType),
			new Schema {
				Subject    = request.Subject,
				SchemaType = request.SchemaType,
				Revision          = new() {
					RevisionId = Guid.NewGuid().ToString(),
					Definition = request.Definition,
					Version    = 1,
					CreatedAt  = DateTimeOffset.UtcNow.ToTimestamp()
				},
				CompatibilityMode = CompatibilityMode.Full
			},
			(_, existing) => {
				if (request.Definition != existing.Revision.Definition) {
					existing.Revision = new SchemaRevision {
						RevisionId = Guid.NewGuid().ToString(),
						Definition = request.Definition,
						Version    = existing.Revision.Version + 1,
						CreatedAt  = DateTimeOffset.UtcNow.ToTimestamp()
					};
				}
				
				return existing;
			}
		);
		
		var response = new CreateOrUpdateSchema.Types.Response {
			RevisionId = schema.Revision.RevisionId,
			Version    = schema.Revision.Version,
			CreatedAt  = schema.Revision.CreatedAt
		};
		
		return Task.FromResult(response);
	}

	public Task<GetLatestSchema.Types.Response> GetLatestSchema(GetLatestSchema request, CancellationToken cancellationToken) {
		var response = Schemas.TryGetValue((request.Subject, request.SchemaType), out var schema)
				? new GetLatestSchema.Types.Response {
					Schema = new SchemaRevision {
						RevisionId = schema.Revision.RevisionId,
						Definition = schema.Revision.Definition,
						Version    = schema.Revision.Version,
						CreatedAt  = schema.Revision.CreatedAt
					}
				}
				: new GetLatestSchema.Types.Response {
					Schema = null
				};

		return Task.FromResult(response);
	}
}