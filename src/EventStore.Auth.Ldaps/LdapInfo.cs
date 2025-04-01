// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;

namespace EventStore.Auth.Ldaps;

/// <summary>
/// Represents the distinguished name of an object and an array of the distinguished names
/// of groups to which the object belongs.
/// </summary>
public class LdapInfo {
	/// <summary>
	/// The login name of the object (for example, sAMAccountName on ActiveDirectory, uid on OpenLDAP)
	/// </summary>
	public readonly string LoginName;

	/// <summary>
	/// Distinguished name of the object
	/// </summary>
	public readonly string DistinguishedName;

	/// <summary>
	/// Array of group distinguished names to which the object with the distinguished name belongs
	/// </summary>
	public readonly string[] GroupDistinguishedNames;

	/// <summary>
	/// Creates a new LdapInfo struct
	/// </summary>
	/// <param name="loginName">The login name (e.g. sAMAccountName on Windows) of the object</param>
	/// <param name="distinguishedName">The distinguished name of the object</param>
	/// <param name="groupDistinguishedNames">The distinguished names of the groups to which the object belongs</param>
	public LdapInfo(string loginName, string distinguishedName, string[] groupDistinguishedNames) {
		LoginName = loginName;
		DistinguishedName = distinguishedName;
		GroupDistinguishedNames = groupDistinguishedNames;
	}

	/// <summary>
	/// Tests whether the object is a member of the group with the given distinguished name
	/// </summary>
	/// <param name="groupDistinguishedName">The distinguished name of the group</param>
	/// <returns>True if a member, false otherwise</returns>
	public bool IsMemberOf(string groupDistinguishedName) {
		return GroupDistinguishedNames.Contains(groupDistinguishedName);
	}

	/// <summary>
	/// An empty LdapInfo (no DN, no group DNs).
	/// </summary>
	public static readonly LdapInfo Empty = new LdapInfo("", "", new string[0]);
}
