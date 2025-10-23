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
	protected static readonly object StrongAffinity = new();

	[JsonIgnore]
	public CancellationToken CancellationToken => token;

	/// <summary>
	/// Returns the synchronization affinity for the current message.
	/// </summary>
	/// <remarks>
	/// For two messages X and Y, published in that order into queue Q, the affinity governs the order in
	/// which X and Y are handled by Q.
	///
	/// 1. If either affinity is <see langword="null"/>, then X and Y may be handled in either order.
	/// 2. Else if X and Y have different affinity objects, then X and Y may be handled in either order.
	/// 3. Else if X and Y both have UnknownAffinity, the order guarantee is determined by Q.
	/// 4. Else if X and Y both have the same affinity object then X is handled before Y;
	///
	/// By default, the property returns the UnknownAffinity object whose behavior depends on Q.
	/// </remarks>
	[JsonIgnore]
	public virtual object Affinity => UnknownAffinity;
}
