// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using HashCode = EventStore.Core.Services.Transport.Common.HashCode;

namespace EventStore.Core.Services.Transport.Grpc;

public readonly struct Uuid : IEquatable<Uuid> {
	public static readonly Uuid Empty = new(Guid.Empty);

	private readonly long _lsb;
	private readonly long _msb;

	public static Uuid NewUuid() => new(Guid.NewGuid());
	public static Uuid FromGuid(Guid value) => new(value);
	public static Uuid Parse(string value) => new(value);
	public static Uuid FromInt64(long msb, long lsb) => new(msb, lsb);

	public static Uuid FromDto(Client.UUID dto) {
		return dto == null
			? throw new ArgumentNullException(nameof(dto))
			: dto.ValueCase switch {
				Client.UUID.ValueOneofCase.String => new(dto.String),
				Client.UUID.ValueOneofCase.Structured => new(dto.Structured.MostSignificantBits, dto.Structured.LeastSignificantBits),
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

		data[..8].Reverse();
		data[..2].Reverse();
		data.Slice(2, 2).Reverse();
		data.Slice(4, 4).Reverse();
		data[8..].Reverse();

		_msb = BitConverter.ToInt64(data);
		_lsb = BitConverter.ToInt64(data[8..]);
	}

	private Uuid(string value) : this(value != null ? Guid.Parse(value) : throw new ArgumentNullException(nameof(value))) {
	}

	private Uuid(long msb, long lsb) {
		_msb = msb;
		_lsb = lsb;
	}

	public Client.UUID ToDto() => new() {
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

	public Guid ToGuid() {
		if (!BitConverter.IsLittleEndian) {
			throw new NotSupportedException();
		}

		Span<byte> data = stackalloc byte[16];
		if (!BitConverter.TryWriteBytes(data, _msb) ||
			!BitConverter.TryWriteBytes(data[8..], _lsb)) {
			throw new InvalidOperationException();
		}

		data[..8].Reverse();
		data[..4].Reverse();
		data.Slice(4, 2).Reverse();
		data.Slice(6, 2).Reverse();
		data[8..].Reverse();

		return new(data);
	}
}
