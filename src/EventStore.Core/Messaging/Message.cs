// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using Newtonsoft.Json;

namespace EventStore.Core.Messaging;

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
	[JsonIgnore]
	public CancellationToken CancellationToken => token;
}
