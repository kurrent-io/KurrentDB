// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Common.Utils;
using KurrentDB.Components.Projections;
using KurrentDB.Components.Tests.TestUtilities;
using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using Xunit;

namespace KurrentDB.Components.Tests;

// The UI stamps the shared projection tool-metadata convention onto its Create/UpdateQuery writes so other
// tools can see a projection was authored here. Metadata rides as a protobuf Struct on the command's byte[]
// (the engine stamps it onto the $ProjectionUpdated event). These assert the keys/values, that update vs
// create are distinguished, that the acting identity is recorded as actor, and that actor is omitted when
// the principal is anonymous.
public class ProjectionToolMetadataTests {
	static readonly ClaimsPrincipal SomeUser = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], "test"));

	// Grants access and immediately replies Updated so the awaiting service call completes, capturing the
	// command the UI publishes.
	static (ProjectionsService Service, List<Message> Published) NewService() {
		var published = new List<Message>();
		var publisher = new ReplyPublisher(msg => {
			published.Add(msg);
			switch (msg) {
				case ProjectionManagementMessage.Command.Post p:
					p.Envelope.ReplyWith(new ProjectionManagementMessage.Updated(p.Name));
					break;
				case ProjectionManagementMessage.Command.UpdateQuery u:
					u.Envelope.ReplyWith(new ProjectionManagementMessage.Updated(u.Name));
					break;
			}
		});
		return (new ProjectionsService(publisher, new RecordingAuthorizationProvider(grant: true)), published);
	}

	static Dictionary<string, string> Decode(byte[] metadata) =>
		Struct.Parser.ParseFrom(metadata).Fields.ToDictionary(kv => kv.Key, kv => kv.Value.StringValue);

	[Fact]
	public async Task Create_stamps_tool_metadata() {
		var (service, published) = NewService();

		await service.CreateAsync(SomeUser, "p", "fromAll()", ProjectionMode.Continuous, false, false, false, CancellationToken.None);

		var post = Assert.IsType<ProjectionManagementMessage.Command.Post>(Assert.Single(published));
		var props = Decode(post.Metadata);
		Assert.Equal("KurrentDB Embedded UI", props["tool"]);
		Assert.Equal("create", props["operation"]);
		Assert.Equal(VersionInfo.Version, props["tool_version"]);
		Assert.Equal("tester", props["actor"]);
		// Pin the closed key set: actor is the acting identity; no revision (no source control for a UI edit).
		Assert.Equal(["actor", "operation", "tool", "tool_version"], props.Keys.OrderBy(k => k));
	}

	[Fact]
	public async Task UpdateQuery_stamps_tool_metadata_as_update() {
		var (service, published) = NewService();

		await service.UpdateQueryAsync(SomeUser, "p", "fromAll()", null, CancellationToken.None);

		var update = Assert.IsType<ProjectionManagementMessage.Command.UpdateQuery>(Assert.Single(published));
		var props = Decode(update.Metadata);
		Assert.Equal("KurrentDB Embedded UI", props["tool"]);
		Assert.Equal("update", props["operation"]);
		Assert.Equal(VersionInfo.Version, props["tool_version"]);
		Assert.Equal("tester", props["actor"]);
		Assert.Equal(["actor", "operation", "tool", "tool_version"], props.Keys.OrderBy(k => k));
	}

	[Fact]
	public async Task Create_omits_actor_when_principal_is_anonymous() {
		var (service, published) = NewService();
		var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

		await service.CreateAsync(anonymous, "p", "fromAll()", ProjectionMode.Continuous, false, false, false, CancellationToken.None);

		var post = Assert.IsType<ProjectionManagementMessage.Command.Post>(Assert.Single(published));
		var props = Decode(post.Metadata);
		Assert.False(props.ContainsKey("actor"));
		Assert.Equal(["operation", "tool", "tool_version"], props.Keys.OrderBy(k => k));
	}

	[Fact]
	public void Builder_keys_are_flat_and_not_dollar_prefixed() {
		foreach (var key in Decode(ProjectionToolMetadata.ForCreate("tester")).Keys)
			Assert.False(key.StartsWith('$'), $"tool metadata key '{key}' must not be $-prefixed (server-reserved namespace)");
	}
}
