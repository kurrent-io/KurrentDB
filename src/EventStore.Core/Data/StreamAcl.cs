// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Runtime.CompilerServices;
using EventStore.Common.Utils;
using EventStore.Core.Caching;

namespace EventStore.Core.Data;

public class StreamAcl {
	public readonly string[] ReadRoles;
	public readonly string[] WriteRoles;
	public readonly string[] DeleteRoles;
	public readonly string[] MetaReadRoles;
	public readonly string[] MetaWriteRoles;

	public StreamAcl(string readRole, string writeRole, string deleteRole, string metaReadRole,
		string metaWriteRole)
		: this(readRole == null ? null : new[] { readRole },
			writeRole == null ? null : new[] { writeRole },
			deleteRole == null ? null : new[] { deleteRole },
			metaReadRole == null ? null : new[] { metaReadRole },
			metaWriteRole == null ? null : new[] { metaWriteRole }) {
	}

	public StreamAcl(string[] readRoles, string[] writeRoles, string[] deleteRoles, string[] metaReadRoles,
		string[] metaWriteRoles) {
		ReadRoles = readRoles;
		WriteRoles = writeRoles;
		DeleteRoles = deleteRoles;
		MetaReadRoles = metaReadRoles;
		MetaWriteRoles = metaWriteRoles;
	}

	public override string ToString() {
		return string.Format("Read: {0}, Write: {1}, Delete: {2}, MetaRead: {3}, MetaWrite: {4}",
			ReadRoles == null ? "<null>" : "[" + string.Join(",", ReadRoles) + "]",
			WriteRoles == null ? "<null>" : "[" + string.Join(",", WriteRoles) + "]",
			DeleteRoles == null ? "<null>" : "[" + string.Join(",", DeleteRoles) + "]",
			MetaReadRoles == null ? "<null>" : "[" + string.Join(",", MetaReadRoles) + "]",
			MetaWriteRoles == null ? "<null>" : "[" + string.Join(",", MetaWriteRoles) + "]");
	}

	public int ApproximateSize {
		get {
			var size = 0;
			size += MemSizer.ObjectHeaderSize; // StreamAcl object header
			size += (Unsafe.SizeOf<string[]>() * 5) // string arrays refs
				.RoundUpToMultipleOf(IntPtr.Size); // padding
			size += MemSizer.SizeOf(ReadRoles);
			size += MemSizer.SizeOf(WriteRoles);
			size += MemSizer.SizeOf(DeleteRoles);
			size += MemSizer.SizeOf(MetaReadRoles);
			size += MemSizer.SizeOf(MetaWriteRoles);

			return size;
		}
	}
}
