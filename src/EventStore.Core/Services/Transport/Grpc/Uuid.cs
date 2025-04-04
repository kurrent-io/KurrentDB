// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using HashCode = EventStore.Core.Services.Transport.Common.HashCode;

namespace EventStore.Core.Services.Transport.Grpc;

public struct Uuid : IEquatable<Uuid> {
	public static readonly Uuid Empty = new Uuid(Guid.Empty);

	private readonly long _lsb;
	private readonly long _msb;

	public static Uuid NewUuid() => new Uuid(Guid.NewGuid());
	public static Uuid FromGuid(Guid value) => new Uuid(value);
	public static Uuid Parse(string value) => new Uuid(value);
	public static Uuid FromInt64(long msb, long lsb) => new Uuid(msb, lsb);

	public static Uuid FromDto(Client.UUID dto) {
		if (dto == null)
			throw new ArgumentNullException(nameof(dto));
		return dto.ValueCase switch {
			Client.UUID.ValueOneofCase.String => new Uuid(dto.String),
			Client.UUID.ValueOneofCase.Structured => new Uuid(dto.Structured.MostSignificantBits,
				dto.Structured.LeastSignificantBits),
			_ => throw new ArgumentException($"Invalid argument: {dto.ValueCase}", nameof(dto))
		};
	}
	private Uuid(Guid value) {
		if (!BitConverter.IsLittleEndian) {
			throw new NotSupportedException();
		}

		Span<byte> data = stackalloc byte[16];

		if (!value.TryWriteBytes(data)) {
			throw new InvalidOperationException();
		}

		data.Slice(0, 8).Reverse();
		data.Slice(0, 2).Reverse();
		data.Slice(2, 2).Reverse();
		data.Slice(4, 4).Reverse();
		data.Slice(8).Reverse();

		_msb = BitConverter.ToInt64(data);
		_lsb = BitConverter.ToInt64(data.Slice(8));
	}

	private Uuid(string value) : this(value != null
		? Guid.Parse(value)
		: throw new ArgumentNullException(nameof(value))) {
	}

	private Uuid(long msb, long lsb) {
		_msb = msb;
		_lsb = lsb;
	}

	public readonly Client.UUID ToDto() => new() {
		Structured = new() {
			LeastSignificantBits = _lsb,
			MostSignificantBits = _msb
		}
	};

	public bool Equals(Uuid other) => _lsb == other._lsb && _msb == other._msb;
	public override bool Equals(object obj) => obj is Uuid other && Equals(other);
	public override int GetHashCode() => HashCode.Hash.Combine(_lsb).Combine(_msb);
	public static bool operator ==(Uuid left, Uuid right) => left.Equals(right);
	public static bool operator !=(Uuid left, Uuid right) => !left.Equals(right);
	public override string ToString() => ToGuid().ToString();
	public string ToString(string format) => ToGuid().ToString(format);

	public readonly Guid ToGuid() {
		if (!BitConverter.IsLittleEndian) {
			throw new NotSupportedException();
		}

		Span<byte> data = stackalloc byte[16];
		if (!BitConverter.TryWriteBytes(data, _msb) ||
			!BitConverter.TryWriteBytes(data.Slice(8), _lsb)) {
			throw new InvalidOperationException();
		}

		data.Slice(0, 8).Reverse();
		data.Slice(0, 4).Reverse();
		data.Slice(4, 2).Reverse();
		data.Slice(6, 2).Reverse();
		data.Slice(8).Reverse();

		return new Guid(data);
	}
}
