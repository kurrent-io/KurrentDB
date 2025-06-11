// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using Newtonsoft.Json;

namespace KurrentDB.Core.Messaging;

[AttributeUsage(AttributeTargets.Class)]
public class BaseMessageAttribute : Attribute {
	public BaseMessageAttribute() {
	}
}

[AttributeUsage(AttributeTargets.Class)]
public class DerivedMessageAttribute : Attribute {
	public DerivedMessageAttribute() {
	}

	public DerivedMessageAttribute(object messageGroup) {
	}
}

[BaseMessage]
public abstract partial class Message(CancellationToken token = default) {
	internal static readonly object UnknownAffinity = new();

	[JsonIgnore]
	public CancellationToken CancellationToken => token;

	/// <summary>
	/// Returns the synchronization affinity for the current message.
	/// </summary>
	/// <remarks>
	/// The affinity changes the ordering guarantees for the two messages X and Y, if the published
	/// in order, as follows:
	/// 1. If <see langword="null"/> then there is no happens-before relationship between publish(X) and publish(Y);
	/// 2. If X and Y have the same affinity (X.Affinity == Y.Affinity) then publish(X) happens before publish(Y);
	/// 3. If X and Y have the different affinity (X.Affinity != Y.Affinity) then there is no happens-before
	/// relationship between publish(X) and publish(Y), as for the case #1;
	///
	/// By default, the property returns the affinity object which behavior depends on the handler type.
	/// </remarks>
	[JsonIgnore]
	public virtual object Affinity => UnknownAffinity;
}
