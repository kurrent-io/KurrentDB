// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Serialization;

namespace KurrentDB.Core.Authorization.AuthorizationPolicies;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AuthorizationPolicySettings))]
internal partial class AuthorizationPolicyJsonContext : JsonSerializerContext;
