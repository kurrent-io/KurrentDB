// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

partial class UserIndexEngineSubscription {
	private sealed class ViewNameEqualityComparer : IEqualityComparer<ViewName>, IAlternateEqualityComparer<ReadOnlySpan<byte>, ViewName> {
		bool IEqualityComparer<ViewName>.Equals(ViewName x, ViewName y) => x.Equals(y);

		int IEqualityComparer<ViewName>.GetHashCode(ViewName obj) => obj.GetHashCode();

		bool IAlternateEqualityComparer<ReadOnlySpan<byte>, ViewName>.Equals(ReadOnlySpan<byte> alternate, ViewName other)
			=> other.Value.SequenceEqual(alternate);

		int IAlternateEqualityComparer<ReadOnlySpan<byte>, ViewName>.GetHashCode(ReadOnlySpan<byte> alternate)
			=> ViewName.GetHashCode(alternate);

		ViewName IAlternateEqualityComparer<ReadOnlySpan<byte>, ViewName>.Create(ReadOnlySpan<byte> alternate) => new(alternate);
	}
}
