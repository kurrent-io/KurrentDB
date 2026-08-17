// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities;
using Kurrent.Kontext.Modules.Entities.Data;
using Kurrent.Kontext.Modules.Entities.Extraction;
using Kurrent.Kontext.Modules.Entities.Resolution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Kontext.Tests.Unit.Modules.Entities;

/// <summary>
/// The AddKontextEntityProjector seams: the hosted service and store register, a caller-supplied
/// pipeline factory replaces the default, and pre-registered services win (first-wins, like the
/// retrieval registration).
/// </summary>
[Category("Entities")]
public class EntityProjectorRegistrationTests {
	[Test]
	public async ValueTask registers_the_projector_the_store_and_the_default_pipeline_factory() {
		var services = new ServiceCollection().AddLogging().AddKontextEntityProjector();

		// Descriptor-level assertions on purpose: instantiating the hosted service would drag in
		// the system-readiness components' bus dependencies, which belong to the real host.
		await Assert.That(services.Any(descriptor => descriptor.ServiceType == typeof(IHostedService))).IsTrue();
		await Assert.That(services.Any(descriptor => descriptor.ServiceType == typeof(EntityExtractionPipelineFactory))).IsTrue();
		await Assert.That(services.Any(descriptor => descriptor.ServiceType == typeof(KontextEntityStore))).IsTrue();

		// Resolution rides with the projector: it writes through the projector's connection, so the
		// gate and the review surface exist exactly when the loop that lends them does.
		await Assert.That(services.Any(descriptor => descriptor.ServiceType == typeof(EntityWriteGate))).IsTrue();
		await Assert.That(services.Any(descriptor => descriptor.ServiceType == typeof(KontextEntityResolutionService))).IsTrue();
	}

	[Test]
	public async ValueTask a_supplied_pipeline_factory_replaces_the_default() {
		var custom = EntityExtractionPipeline.From([new PatternEntityExtractor()]);

		var services = new ServiceCollection()
			.AddLogging()
			.AddKontextEntityProjector(pipeline: (_, _) => Task.FromResult(custom));

		await using var provider = services.BuildServiceProvider();

		var factory = provider.GetRequiredService<EntityExtractionPipelineFactory>();

		await Assert.That(await factory(provider, CancellationToken.None)).IsSameReferenceAs(custom);
	}

	[Test]
	public async ValueTask deduplication_options_land_when_configured() {
		var services = new ServiceCollection()
			.AddLogging()
			.AddKontextEntityProjector(deduplication: options => options.FlagThreshold = 0.7);

		await using var provider = services.BuildServiceProvider();

		var options = provider.GetRequiredService<EntityDeduplicationOptions>();

		await Assert.That(options.FlagThreshold).IsEqualTo(0.7);
		await Assert.That(options.AutoMergeThreshold).IsEqualTo(0.95);
	}
}
