// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace EventStore.Core.Authentication.DelegatedAuthentication;

public class DelegatedClaimsIdentity(IReadOnlyDictionary<string, string> tokens) :
	ClaimsIdentity(tokens.Select(pair => new Claim(pair.Key, pair.Value)));
