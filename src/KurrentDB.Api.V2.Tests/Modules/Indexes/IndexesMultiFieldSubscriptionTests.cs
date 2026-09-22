// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using KurrentDB.Protocol.V2.Indexes;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Tests.Modules.Indexes;

public class IndexesMultiFieldSubscriptionTests {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	IndexesService.IndexesServiceClient IndexesClient => KurrentContext.IndexesClient;
	StreamsService.StreamsServiceClient StreamsWriteClient => KurrentContext.StreamsV2Client;
	EventStore.Client.Streams.Streams.StreamsClient StreamsReadClient => KurrentContext.StreamsClient;

	static readonly Guid CorrelationId = Guid.NewGuid();

	[Test]
	public async ValueTask constrained_subscription_only_receives_events_matching_all_fields(CancellationToken ct) {
		var indexName = $"sub-constrained-{CorrelationId}";
		var eventType = $"OrderPlaced-constrained-{CorrelationId}";
		var stream = $"stream-constrained-{CorrelationId}";

		await IndexesClient.CreateAsync(
			new() {
				Name = indexName,
				Filter = $"rec => rec.schema.name == '{eventType}'",
				Fields = {
					new IndexField { Name = "country", Selector = "rec => rec.value.country", Type = IndexFieldType.String },
					new IndexField { Name = "amount", Selector = "rec => rec.value.amount", Type = IndexFieldType.Int32 },
				},
			},
			cancellationToken: ct);

		var wholeIndex = $"$idx-user-{indexName}";
		var constrained = $"{wholeIndex}:country=\"Mauritius\";amount=100";

		// seed one matching event and wait for the index (and the constrained query) to become available
		await StreamsWriteClient.AppendEvent(stream, eventType, """{ "orderId": "seed", "country": "Mauritius", "amount": 100 }""", ct);
		await StreamsReadClient.WaitForIndexEvents(wholeIndex, 1, ct);
		await StreamsReadClient.WaitForIndexEvents(constrained, 1, ct);

		await using var wholeEnumerator = StreamsReadClient.SubscribeToAllFiltered(wholeIndex, ct).GetAsyncEnumerator(ct);
		await using var constrainedEnumerator = StreamsReadClient.SubscribeToAllFiltered(constrained, ct).GetAsyncEnumerator(ct);

		// both subscriptions catch up on the seed event
		await Assert.That((await wholeEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "seed", """);
		await Assert.That((await constrainedEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "seed", """);

		// live events: one matches both constraints, the others differ on exactly one field
		await StreamsWriteClient.AppendEvent(stream, eventType, """{ "orderId": "match", "country": "Mauritius", "amount": 100 }""", ct);
		await StreamsWriteClient.AppendEvent(stream, eventType, """{ "orderId": "wrong-country", "country": "France", "amount": 100 }""", ct);
		await StreamsWriteClient.AppendEvent(stream, eventType, """{ "orderId": "wrong-amount", "country": "Mauritius", "amount": 200 }""", ct);

		// the whole-index subscription receives every indexed event
		await Assert.That((await wholeEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "match", """);
		await Assert.That((await wholeEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "wrong-country", """);
		await Assert.That((await wholeEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "wrong-amount", """);

		// the constrained subscription only receives the event satisfying both field constraints
		await Assert.That((await constrainedEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "match", """);

		// deleting the index drops its live subscriptions (both the whole-index and the constrained one, keyed by the base stream)
		await IndexesClient.DeleteAsync(new() { Name = indexName }, cancellationToken: ct);

		var wholeEx = await Assert.That(async () => await wholeEnumerator.ConsumeNext()).Throws<RpcException>();
		await Assert.That(wholeEx!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);

		var constrainedEx = await Assert.That(async () => await constrainedEnumerator.ConsumeNext()).Throws<RpcException>();
		await Assert.That(constrainedEx!.Status.StatusCode).IsEqualTo(StatusCode.NotFound);
	}

	[Test]
	public async ValueTask single_field_subscriptions_respect_subsets_and_null_fields(CancellationToken ct) {
		var indexName = $"sub-subset-{CorrelationId}";
		var eventType = $"OrderPlaced-subset-{CorrelationId}";
		var stream = $"stream-subset-{CorrelationId}";

		await IndexesClient.CreateAsync(
			new() {
				Name = indexName,
				Filter = $"rec => rec.schema.name == '{eventType}'",
				Fields = {
					new IndexField { Name = "country", Selector = "rec => rec.value.country", Type = IndexFieldType.String },
					new IndexField { Name = "amount", Selector = "rec => rec.value.amount", Type = IndexFieldType.Int32 },
				},
			},
			cancellationToken: ct);

		var wholeIndex = $"$idx-user-{indexName}";
		var byCountry = $"{wholeIndex}:country=\"Mauritius\"";
		var byAmount = $"{wholeIndex}:amount=100";

		// seed an event matching both fields and wait for each constrained query to become available
		await StreamsWriteClient.AppendEvent(stream, eventType, """{ "orderId": "seed", "country": "Mauritius", "amount": 100 }""", ct);
		await StreamsReadClient.WaitForIndexEvents(byCountry, 1, ct);
		await StreamsReadClient.WaitForIndexEvents(byAmount, 1, ct);

		await using var byCountryEnumerator = StreamsReadClient.SubscribeToAllFiltered(byCountry, ct).GetAsyncEnumerator(ct);
		await using var byAmountEnumerator = StreamsReadClient.SubscribeToAllFiltered(byAmount, ct).GetAsyncEnumerator(ct);

		// both single-field subscriptions catch up on the seed (it matches both)
		await Assert.That((await byCountryEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "seed", """);
		await Assert.That((await byAmountEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "seed", """);

		// "no-amount": country present, amount NULL  -> matches country=Mauritius, but amount=100 (NULL omitted -> unsatisfied)
		await StreamsWriteClient.AppendEvent(stream, eventType, """{ "orderId": "no-amount", "country": "Mauritius" }""", ct);
		// "fr-100": amount matches, country differs -> matches amount=100, but not country=Mauritius
		await StreamsWriteClient.AppendEvent(stream, eventType, """{ "orderId": "fr-100", "country": "France", "amount": 100 }""", ct);

		// country subscription receives "no-amount" (amount is unconstrained) and skips "fr-100"
		await Assert.That((await byCountryEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "no-amount", """);
		// amount subscription skips "no-amount" (its amount is NULL) and receives "fr-100"
		await Assert.That((await byAmountEnumerator.ConsumeNext()).Data.ToStringUtf8()).Contains(""" "orderId": "fr-100", """);

		await IndexesClient.DeleteAsync(new() { Name = indexName }, cancellationToken: ct);
	}

	[Test]
	public async ValueTask subscribing_with_invalid_constraints_is_rejected(CancellationToken ct) {
		var indexName = $"sub-invalid-{CorrelationId}";
		var eventType = $"OrderPlaced-invalid-{CorrelationId}";
		var stream = $"stream-invalid-{CorrelationId}";

		await IndexesClient.CreateAsync(
			new() {
				Name = indexName,
				Filter = $"rec => rec.schema.name == '{eventType}'",
				Fields = {
					new IndexField { Name = "amount", Selector = "rec => rec.value.amount", Type = IndexFieldType.Int32 },
				},
			},
			cancellationToken: ct);

		var wholeIndex = $"$idx-user-{indexName}";

		// seed an event and wait for it so the index is created and queryable before we subscribe
		await StreamsWriteClient.AppendEvent(stream, eventType, """{ "orderId": "seed", "amount": 5 }""", ct);
		await StreamsReadClient.WaitForIndexEvents(wholeIndex, 1, ct);

		var ex = await Assert
			.That(async () => {
				await StreamsReadClient
					.SubscribeToAllFiltered($"{wholeIndex}:amount=notanumber", ct)
					.ToArrayAsync(ct);
			})
			.Throws<RpcException>();

		await Assert.That(ex!.Status.StatusCode).IsEqualTo(StatusCode.InvalidArgument);

		await IndexesClient.DeleteAsync(new() { Name = indexName }, cancellationToken: ct);
	}
}
