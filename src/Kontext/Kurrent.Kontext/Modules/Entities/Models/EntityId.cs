// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Cryptography;
using System.Text;

namespace Kurrent.Kontext.Entities;

/// <summary>
/// Deterministic entity ids: the same (type, normalized name) always produces the same id, which
/// is what makes at-least-once delivery safe, a replay re-derives identical ids the read model's
/// keyed MERGEs fold away. Accepted flip side: one normalized name = one entity per type, telling
/// two 'John Smith's apart is the disambiguation slice's job.
/// </summary>
public static class EntityId {
    public static string For(string entityType, string name) {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{entityType}\n{Normalize(name)}"));

        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true).ToString();
    }

    /// <summary>The match key for a surface form. Entity creation and exact resolution both key on this.</summary>
    public static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
