// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using System.Text.Json;
using EventStore.Plugins.Authorization;

namespace EventStore.Auth.StreamPolicyPlugin.Tests;

internal static class PolicyTestHelpers {
	public static readonly string[] ValidActions = ["read", "write", "delete", "metadataRead", "metadataWrite"];

	public static OperationDefinition ActionToOperationDefinition(string action) =>
		action switch {
			"read" => Operations.Streams.Read,
			"write" => Operations.Streams.Write,
			"delete" => Operations.Streams.Delete,
			"metadataRead" => Operations.Streams.MetadataRead,
			"metadataWrite" => Operations.Streams.MetadataWrite,
			_ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
		};

	public static ClaimsPrincipal CreateUser(string username, string[] roles) {
		var claims = new List<Claim>();
		if (roles.Length != 0) {
			claims.AddRange(roles.Select(x => new Claim(ClaimTypes.Role, x)).ToList());
		}
		claims.Add(new Claim(ClaimTypes.Name, username));

		return new(new[] {
			new ClaimsIdentity(claims, authenticationType: "basic")
		});
	}

	public static byte[] SerializePolicy(Schema.Policy policy) =>
		JsonSerializer.SerializeToUtf8Bytes(policy, new JsonSerializerOptions {
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		});

	public static Schema.AccessPolicy CreateAccessPolicyDtoForAction(string allowedAction, string allowedUser) =>
		allowedAction switch {
			"read" => new Schema.AccessPolicy{ Readers = [allowedUser], Writers = [], Deleters = [], MetadataReaders = [], MetadataWriters = [] },
			"write" => new Schema.AccessPolicy{ Writers = [allowedUser], Readers = [], Deleters = [], MetadataReaders = [], MetadataWriters = [] },
			"delete" => new Schema.AccessPolicy{ Deleters = [allowedUser], Readers = [], Writers = [], MetadataReaders = [], MetadataWriters = [] },
			"metadataRead" => new Schema.AccessPolicy{ MetadataReaders = [allowedUser], Readers = [], Writers = [], Deleters = [], MetadataWriters = [] },
			"metadataWrite" => new Schema.AccessPolicy{ MetadataWriters = [allowedUser], Readers = [], Writers = [], Deleters = [], MetadataReaders = [] },
			_ => new Schema.AccessPolicy()
		};

	public static AccessPolicy CreateAccessPolicyForAction(string allowedAction, string allowedUser) {
		var dto = CreateAccessPolicyDtoForAction(allowedAction, allowedUser);
		return new AccessPolicy(
			dto.Readers,
			dto.Writers,
			dto.Deleters,
			dto.MetadataReaders,
			dto.MetadataWriters
		);
	}
}
