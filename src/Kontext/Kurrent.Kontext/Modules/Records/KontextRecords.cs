// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using System.Text;
using DotNext.Buffers;
using EventStore.Plugins.Authorization;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Kontext.Infrastructure.Validation;
using Kurrent.Kontext.Records.Data;
using Kurrent.Kontext.Retrieval;
using Kurrent.Quack;
using KurrentDB.Core.Services;
using KurrentDB.SecondaryIndexing.Query;
using Value = Google.Protobuf.WellKnownTypes.Value;

using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;

namespace Kurrent.Kontext.Records;

/// <summary>Searches the indexed records.</summary>
public sealed class KontextRecords(
	KontextRecordsStore store,
	EmbeddingGenerator embeddings,
	RequestValidationService validation,
	IQueryEngine queries,
	IAuthorizationProvider authorizer
) : IKontextRecords {
	const int DefaultSearchLimit = 10;
	const int DefaultQueryRows   = 100;
	const int MaxQueryRows       = 1_000;

	static readonly Operation ReadAllOperation = new Operation(Operations.Streams.Read)
		.WithParameter(Operations.Streams.Parameters.StreamId(SystemStreams.AllStream));

	public async ValueTask<Contracts.SearchResponse> SearchAsync(Contracts.SearchRequest request, CancellationToken ct = default) {
		validation.Validate(request);

		// Flags the text as a QUERY, for models that encode queries and documents differently.
		var embedding = await embeddings.EmbedQueryAsync(request.Query, ct).ConfigureAwait(false);

		var options = new HybridOptions {
			Query          = request.Query,
			QueryEmbedding = embedding,
			K              = request.Limit > 0 ? request.Limit : DefaultSearchLimit,
			Stream         = request.ScopeCase is Contracts.SearchRequest.ScopeOneofCase.Stream ? request.Stream : null,
			Category       = request.ScopeCase is Contracts.SearchRequest.ScopeOneofCase.Category ? request.Category : null,
			SchemaName     = request.SchemaCase is Contracts.SearchRequest.SchemaOneofCase.SchemaName ? request.SchemaName : null,
			SchemaId       = request.SchemaCase is Contracts.SearchRequest.SchemaOneofCase.SchemaId ? request.SchemaId : null,
			SchemaFormat   = request.SchemaCase is Contracts.SearchRequest.SchemaOneofCase.SchemaFormat ? request.SchemaFormat : null,
		};

		var response = new Contracts.SearchResponse();

		await foreach (var hit in store.SearchAsync(options, ct).ConfigureAwait(false)) {
			if (hit.Score < request.MinScore)
				continue;

			response.Hits.Add(new Contracts.SearchResponse.Types.RecordHit {
				Score  = hit.Score,
				Record = ToContract(hit.Record),
			});
		}

		return response;
	}

	public async ValueTask<Contracts.QueryResponse> QueryAsync(
		Contracts.QueryRequest request, ClaimsPrincipal principal, CancellationToken ct = default
	) {
		validation.Validate(request);

		// The engine expands payloads as the system account, so this check is the only thing standing
		// between a caller and every record in the database.
		if (!await authorizer.CheckAccessAsync(principal, ReadAllOperation, ct).ConfigureAwait(false))
			throw new UnauthorizedAccessException("Reading $all is required to query records.");

		var limit = request.Limit switch {
			<= 0                 => DefaultQueryRows,
			> MaxQueryRows       => MaxQueryRows,
			var requested        => requested,
		};

		// to_json collapses any result shape into ONE varchar per row, so nothing here decodes column
		// types. The rewriter descends into the subquery and applies its own FROM-clause allowlist, and
		// to_json is a scalar function, so the ban on table functions is untouched.
		// One row past the limit, to tell "exactly full" from "cut short".
		var sql = $"SELECT to_json(q) FROM ({request.Sql}) q LIMIT {limit + 1}";

		var prepared = default(MemoryOwner<byte>);
		var consumer = new JsonRowConsumer(limit);

		try {
			// Unsigned: the signature protects a prepared handle that travels through an untrusted
			// client. This one never leaves the method.
			prepared = queries.PrepareQuery(Encoding.UTF8.GetBytes(sql), new() { UseDigitalSignature = false });

			await queries.ExecuteAsync(prepared.Memory, consumer, new() { CheckIntegrity = false }, ct).ConfigureAwait(false);
		} finally {
			prepared.Dispose();
		}

		var response = new Contracts.QueryResponse { Truncated = consumer.Truncated };
		response.Rows.AddRange(consumer.Rows);

		return response;
	}

	sealed class JsonRowConsumer(int limit) : IQueryResultConsumer {
		public List<Struct> Rows { get; } = new(limit);

		public bool Truncated { get; private set; }

		public ValueTask ConsumeAsync(IQueryResultReader reader, CancellationToken token) {
			while (reader.TryRead()) {
				// One column, because the query is wrapped in to_json.
				foreach (ref readonly var row in reader.Chunk[0].BlobRows) {
					if (Rows.Count == limit) {
						Truncated = true;
						return ValueTask.CompletedTask;
					}

					Rows.Add(Struct.Parser.ParseJson(Encoding.UTF8.GetString(row.AsSpan())));
				}

				token.ThrowIfCancellationRequested();
			}

			return ValueTask.CompletedTask;
		}
	}

	static Contracts.Record ToContract(StoredRecord record) {
		var contract = new Contracts.Record {
			RecordId    = record.RecordId.ToString(),
			Stream      = record.Stream,
			Category    = record.Category,
			LogPosition = record.LogPosition,
			Data        = record.Data ?? "",
			CreatedAt   = Timestamp.FromDateTimeOffset(record.CreatedAt),
			Schema = new() {
				Format = record.SchemaFormat,
				Name   = record.SchemaName,
				Id     = record.SchemaId ?? "",
			},
		};

		foreach (var (key, value) in ReadProperties(record.Properties))
			contract.Properties[key] = value;

		return contract;
	}

	// A corrupt value reads as no properties rather than failing the page and hiding every good row.
	static IEnumerable<KeyValuePair<string, Value>> ReadProperties(string? json) {
		if (string.IsNullOrEmpty(json))
			return [];

		try {
			return Struct.Parser.ParseJson(json).Fields;
		} catch (InvalidJsonException) {
			return [];
		} catch (InvalidProtocolBufferException) {
			return [];
		}
	}
}
