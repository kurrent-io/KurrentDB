// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Modules.Entities.Data;
using Kurrent.Kontext.Modules.Entities.Resolution;

namespace Kurrent.Kontext.Tests.Integration.Modules.Entities;

/// <summary>
/// The public human-correction surface: the review queue as a reviewer reads it, and the verdict
/// applied through the projector's write turn. The gate stands in for the projector here — binding
/// a connection is exactly what its loop does.
/// </summary>
[Category("Integration")]
[Category("Entities")]
[Timeout(30_000)]
public class KontextEntityResolutionServiceTests {

	static readonly DateTimeOffset Base = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);

	[Test]
	public async ValueTask the_queue_lists_oldest_first_with_both_entries_and_the_survivor_it_would_pick(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-emily", "Emily Chen", "PERSON", Base) { MentionCount = 10 },
			new EntitySeed("ent-emilia", "Emilia Chen", "PERSON", Base.AddHours(1)) { MentionCount = 2 },
			new EntitySeed("ent-kurrent", "Kurrent", "ORGANIZATION", Base) { MentionCount = 4 });

		EntitySeeding.Insert(dataSources,
			new LinkSeed("ent-emilia", "ent-emily", Base.AddHours(2)) { Confidence = 0.84, Method = "semantic" },
			new LinkSeed("ent-gone", "ent-kurrent", Base.AddHours(1)) { Confidence = 0.87, Method = "fuzzy" });

		using var connection  = dataSources.OpenLanceWriter();

		var service = NewService(connection, out var binding);

		using (binding) {
			// Act
			var pending = await service.ListPendingAsync(ct: cancellationToken);

			// Assert — oldest first, both entries attached, the default survivor previewed.
			await Assert.That(pending.Select(link => link.Link.SourceEntityId).ToList())
				.IsEquivalentTo(["ent-gone", "ent-emilia"], TUnit.Assertions.Enums.CollectionOrdering.Matching);

			var chen = pending[1];

			await Assert.That(chen.Source!.Name).IsEqualTo("Emilia Chen");
			await Assert.That(chen.Target!.Name).IsEqualTo("Emily Chen");
			await Assert.That(chen.Link.Confidence).IsEqualTo(0.84);
			await Assert.That(chen.DefaultSurvivorEntityId).IsEqualTo("ent-emily");

			// A swept endpoint is reported as missing rather than hiding the row from review.
			await Assert.That(pending[0].Source).IsNull();
			await Assert.That(pending[0].Target!.Name).IsEqualTo("Kurrent");
			await Assert.That(pending[0].DefaultSurvivorEntityId).IsEqualTo("ent-kurrent");
		}
	}

	[Test]
	public async ValueTask resolving_through_the_service_empties_the_queue(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		EntitySeeding.Insert(dataSources,
			new EntitySeed("ent-emily", "Emily Chen", "PERSON", Base) { MentionCount = 2 },
			new EntitySeed("ent-emilia", "Emilia Chen", "PERSON", Base.AddHours(1)) { MentionCount = 1 });

		EntitySeeding.Insert(dataSources,
			new MentionSeed("ent-emily", "m1", "Emily Chen", Base),
			new MentionSeed("ent-emily", "m2", "Emily", Base.AddMinutes(1)),
			new MentionSeed("ent-emilia", "m3", "Emilia Chen", Base.AddHours(1)));

		EntitySeeding.Insert(dataSources, new LinkSeed("ent-emilia", "ent-emily", Base.AddHours(1)) { Confidence = 0.84 });

		using var connection  = dataSources.OpenLanceWriter();

		var store   = new KontextEntityStore(connection);
		var service = NewService(connection, out var binding);

		using (binding) {
			// Act — the human verdict: Emilia is a typo.
			var resolution = await service.ResolveAsync("ent-emilia", "ent-emily", EntityLinkVerdict.SameEntity, ct: cancellationToken);

			// Assert
			await Assert.That(resolution.SurvivorEntityId).IsEqualTo("ent-emily");
			await Assert.That(resolution.SurvivorMentionCount).IsEqualTo(3);
			await Assert.That(await service.ListPendingAsync(ct: cancellationToken)).IsEmpty();
		}

		await Assert.That(await store.GetAsync("ent-emilia")).IsNull();
		await Assert.That((await store.GetAsync("ent-emily"))!.Aliases).Contains("emilia chen");
	}

	[Test]
	public async ValueTask the_surface_is_unavailable_while_no_projector_runs(CancellationToken cancellationToken) {
		// Arrange — no binding: the entity read model has no write surface at all.
		using var dir         = new TempDir();
		using var dataSources = MemorySeeding.NewDataSources(dir.Path);
		await EntitySeeding.CreateSchema(dataSources);

		var service = new KontextEntityResolutionService(new EntityWriteGate());

		// Act + Assert
		await Assert.That(async () => await service.ListPendingAsync(ct: cancellationToken)).Throws<InvalidOperationException>();

		await Assert.That(async () => await service.ResolveAsync("ent-a", "ent-b", EntityLinkVerdict.SameEntity, ct: cancellationToken))
			.Throws<InvalidOperationException>();
	}

	static KontextEntityResolutionService NewService(Kurrent.Quack.DuckDBAdvancedConnection connection, out IDisposable binding) {
		var gate = new EntityWriteGate();

		binding = gate.Bind(connection);

		return new(gate);
	}
}
