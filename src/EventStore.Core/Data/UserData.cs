// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

namespace EventStore.Core.Data;

public record UserData {
	public UserData(string loginName, string fullName, string[]? groups, string hash, string salt, bool disabled) {
		LoginName = loginName;
		FullName = fullName;
		Groups = groups ?? [];
		Hash = hash;
		Salt = salt;
		Disabled = disabled;
	}

	public string LoginName { get; init; }
	public string FullName { get; init; }
	public string[] Groups { get; init; } = [];
	public string Hash { get; init; }
	public string Salt { get; init; }
	public bool Disabled { get; init; }

	public UserData SetFullName(string fullName) => this with { FullName = fullName };
	
	public UserData SetGroups(params string[] groups) => this with { Groups = groups };
	
	public UserData SetPassword(string hash, string salt) => this with { Hash = hash, Salt = salt };
	
	public UserData SetEnabled() => this with { Disabled = false };
	
	public UserData SetDisabled() => this with { Disabled = true };
}
