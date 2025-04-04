// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Google.Protobuf;

namespace EventStore.LogV3;

// System.Guid.ToByteArray roundtrips with the ctor that takes byte[] as the argument
// if changing this be careful not to break changing between endianness
public partial class ProtoGuid {
	public static implicit operator Guid(ProtoGuid x) {
		if (x == null)
			return default;

		return new Guid(x.Bytes.ToByteArray());
	}

	public static implicit operator ProtoGuid(Guid x) {
		return new ProtoGuid {
			Bytes = ByteString.CopyFrom(x.ToByteArray()),
		};
	}
}
