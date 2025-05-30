﻿// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Auth.Ldaps;

public interface ILdapsCredentialValidator : IDisposable {
	bool TryValidateCredentials(string username, string password, out LdapInfo ldapUser);
}
