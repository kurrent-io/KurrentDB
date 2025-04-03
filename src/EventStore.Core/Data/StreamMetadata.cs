// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using EventStore.Common.Utils;
using EventStore.Core.Caching;
using EventStore.Core.Services;
using EventStore.LogCommon;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EventStore.Core.Data;

public class StreamMetadata {
	public static readonly StreamMetadata Empty = new StreamMetadata();

	public readonly long? MaxCount;
	public readonly TimeSpan? MaxAge;

	public readonly long? TruncateBefore;
	public readonly bool? TempStream;

	public readonly TimeSpan? CacheControl;
	public readonly StreamAcl Acl;

	public StreamMetadata(long? maxCount = null, TimeSpan? maxAge = null,
		long? truncateBefore = null, bool? tempStream = null,
		TimeSpan? cacheControl = null, StreamAcl acl = null) {
		if (maxCount <= 0)
			throw new ArgumentOutOfRangeException(
				"maxCount", string.Format("{0} should be positive value.", SystemMetadata.MaxCount));
		if (maxAge <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(
				"maxAge", string.Format("{0} should be positive time span.", SystemMetadata.MaxAge));
		if (truncateBefore < 0)
			throw new ArgumentOutOfRangeException(
				"truncateBefore",
				string.Format("{0} should be non-negative value.", SystemMetadata.TruncateBefore));
		if (cacheControl <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(
				"cacheControl", string.Format("{0} should be positive time span.", SystemMetadata.CacheControl));

		MaxCount = maxCount;
		MaxAge = maxAge;
		TruncateBefore = truncateBefore;
		TempStream = tempStream;
		CacheControl = cacheControl;
		Acl = acl;
	}

	public override string ToString() {
		return string.Format(
			"MaxCount: {0}, MaxAge: {1}, TruncateBefore: {2}, TempStream: {3}, CacheControl: {4}, Acl: {5}",
			MaxCount, MaxAge, TruncateBefore, TempStream, CacheControl, Acl);
	}

	public static StreamMetadata TryFromJsonBytes(byte prepareVersion, ReadOnlyMemory<byte> bytes) =>
		TryFromJsonBytes(prepareVersion, bytes.Span);

	public static StreamMetadata TryFromJsonBytes(byte prepareVersion, ReadOnlySpan<byte> json) {
		try {
			var metadata = FromJsonBytes(json);
			metadata = UpgradeMetadata(prepareVersion, metadata);
			return metadata;
		} catch {
			// this can happen if the json is malformed, or if any of the things that we expect to be
			// longs are bigger than longs which can happen if you try to set something to
			// long.maxvalue in the webui because javascript translates it into a number that is
			// bigger than long.max
			return Empty;
		}
	}

	public static StreamMetadata UpgradeMetadata(byte prepareVersion, StreamMetadata metadata) {
		if (prepareVersion == LogRecordVersion.LogRecordV0 && metadata.TruncateBefore == int.MaxValue) {
			metadata = new StreamMetadata(
				maxCount: metadata.MaxCount,
				maxAge: metadata.MaxAge,
				truncateBefore: EventNumber.DeletedStream,
				tempStream: metadata.TempStream,
				cacheControl: metadata.CacheControl,
				acl: metadata.Acl);
		}

		return metadata;
	}

	public static StreamMetadata FromJsonBytes(ReadOnlyMemory<byte> json) => FromJsonBytes(json.Span);

	public static StreamMetadata FromJsonBytes(ReadOnlySpan<byte> json) {
		using (var reader = new JsonTextReader(new StreamReader(new MemoryStream(json.ToArray())))) {
			return FromJsonReader(reader);
		}
	}

	public static StreamMetadata FromJson(string json) {
		using (var reader = new JsonTextReader(new StringReader(json))) {
			return FromJsonReader(reader);
		}
	}

	public static StreamMetadata FromJsonReader(JsonTextReader reader) {
		Check(reader.Read(), reader);
		Check(JsonToken.StartObject, reader);

		long? maxCount = null;
		TimeSpan? maxAge = null;
		long? truncateBefore = null;
		bool? tempStream = null;
		TimeSpan? cacheControl = null;
		StreamAcl acl = null;

		while (true) {
			Check(reader.Read(), reader);
			if (reader.TokenType == JsonToken.EndObject)
				break;
			Check(JsonToken.PropertyName, reader);
			var name = (string)reader.Value;
			switch (name) {
				case SystemMetadata.MaxCount: {
					Check(reader.Read(), reader);
					Check(JsonToken.Integer, reader);
					maxCount = (long)reader.Value;
					break;
				}
				case SystemMetadata.MaxAge: {
					Check(reader.Read(), reader);
					Check(JsonToken.Integer, reader);
					maxAge = TimeSpan.FromSeconds((long)reader.Value);
					break;
				}
				case SystemMetadata.TruncateBefore: {
					Check(reader.Read(), reader);
					Check(JsonToken.Integer, reader);
					truncateBefore = (long)reader.Value;
					break;
				}
				case SystemMetadata.TempStream: {
					Check(reader.Read(), reader);
					Check(JsonToken.Boolean, reader);
					tempStream = (bool)reader.Value;
					break;
				}
				case SystemMetadata.CacheControl: {
					Check(reader.Read(), reader);
					Check(JsonToken.Integer, reader);
					cacheControl = TimeSpan.FromSeconds((long)reader.Value);
					break;
				}
				case SystemMetadata.Acl: {
					acl = ReadAcl(reader);
					break;
				}
				default: {
					Check(reader.Read(), reader);
					// skip
					JToken.ReadFrom(reader);
					break;
				}
			}
		}

		return new StreamMetadata(
			maxCount > 0 ? maxCount : null, maxAge > TimeSpan.Zero ? maxAge : null,
			truncateBefore >= 0 ? truncateBefore : null, tempStream,
			cacheControl > TimeSpan.Zero ? cacheControl : null, acl);
	}

	internal static StreamAcl ReadAcl(JsonTextReader reader) {
		Check(reader.Read(), reader);
		Check(JsonToken.StartObject, reader);

		string[] read = null;
		string[] write = null;
		string[] delete = null;
		string[] metaRead = null;
		string[] metaWrite = null;

		while (true) {
			Check(reader.Read(), reader);
			if (reader.TokenType == JsonToken.EndObject)
				break;
			Check(JsonToken.PropertyName, reader);
			var name = (string)reader.Value;
			switch (name) {
				case SystemMetadata.AclRead:
					read = ReadRoles(reader);
					break;
				case SystemMetadata.AclWrite:
					write = ReadRoles(reader);
					break;
				case SystemMetadata.AclDelete:
					delete = ReadRoles(reader);
					break;
				case SystemMetadata.AclMetaRead:
					metaRead = ReadRoles(reader);
					break;
				case SystemMetadata.AclMetaWrite:
					metaWrite = ReadRoles(reader);
					break;
			}
		}

		return new StreamAcl(read, write, delete, metaRead, metaWrite);
	}

	private static string[] ReadRoles(JsonTextReader reader) {
		Check(reader.Read(), reader);
		if (reader.TokenType == JsonToken.String)
			return new[] {(string)reader.Value};

		if (reader.TokenType == JsonToken.StartArray) {
			var roles = new List<string>();
			while (true) {
				Check(reader.Read(), reader);
				if (reader.TokenType == JsonToken.EndArray)
					break;
				Check(JsonToken.String, reader);
				roles.Add((string)reader.Value);
			}

			return roles.ToArray();
		}

		throw new Exception("Invalid JSON");
	}

	private static void Check(JsonToken type, JsonTextReader reader) {
		if (reader.TokenType != type)
			throw new Exception("Invalid JSON");
	}

	private static void Check(bool read, JsonTextReader reader) {
		if (!read)
			throw new Exception("Invalid JSON");
	}

	public byte[] ToJsonBytes() {
		using (var memoryStream = new MemoryStream()) {
			using (var jsonWriter = new JsonTextWriter(new StreamWriter(memoryStream, Helper.UTF8NoBom))) {
				WriteAsJson(jsonWriter);
			}

			return memoryStream.ToArray();
		}
	}

	public string ToJsonString() {
		using (var stringWriter = new StringWriter()) {
			using (var jsonWriter = new JsonTextWriter(stringWriter)) {
				WriteAsJson(jsonWriter);
			}

			return stringWriter.ToString();
		}
	}

	private void WriteAsJson(JsonTextWriter jsonWriter) {
		jsonWriter.WriteStartObject();
		if (MaxCount.HasValue) {
			jsonWriter.WritePropertyName(SystemMetadata.MaxCount);
			jsonWriter.WriteValue(MaxCount.Value);
		}

		if (MaxAge.HasValue) {
			jsonWriter.WritePropertyName(SystemMetadata.MaxAge);
			jsonWriter.WriteValue((long)MaxAge.Value.TotalSeconds);
		}

		if (TruncateBefore.HasValue) {
			jsonWriter.WritePropertyName(SystemMetadata.TruncateBefore);
			jsonWriter.WriteValue(TruncateBefore.Value);
		}

		if (TempStream.HasValue) {
			jsonWriter.WritePropertyName(SystemMetadata.TempStream);
			jsonWriter.WriteValue(TempStream.Value);
		}

		if (CacheControl.HasValue) {
			jsonWriter.WritePropertyName(SystemMetadata.CacheControl);
			jsonWriter.WriteValue((long)CacheControl.Value.TotalSeconds);
		}

		if (Acl != null) {
			jsonWriter.WritePropertyName(SystemMetadata.Acl);
			WriteAcl(jsonWriter, Acl);
		}

		jsonWriter.WriteEndObject();
	}

	internal static void WriteAcl(JsonTextWriter jsonWriter, StreamAcl acl) {
		jsonWriter.WriteStartObject();
		WriteAclRoles(jsonWriter, SystemMetadata.AclRead, acl.ReadRoles);
		WriteAclRoles(jsonWriter, SystemMetadata.AclWrite, acl.WriteRoles);
		WriteAclRoles(jsonWriter, SystemMetadata.AclDelete, acl.DeleteRoles);
		WriteAclRoles(jsonWriter, SystemMetadata.AclMetaRead, acl.MetaReadRoles);
		WriteAclRoles(jsonWriter, SystemMetadata.AclMetaWrite, acl.MetaWriteRoles);
		jsonWriter.WriteEndObject();
	}

	private static void WriteAclRoles(JsonTextWriter jsonWriter, string propertyName, string[] roles) {
		if (roles == null)
			return;
		jsonWriter.WritePropertyName(propertyName);
		if (roles.Length == 1) {
			jsonWriter.WriteValue(roles[0]);
		} else {
			jsonWriter.WriteStartArray();
			Array.ForEach(roles, jsonWriter.WriteValue);
			jsonWriter.WriteEndArray();
		}
	}

	public int ApproximateSize =>
		MemSizer.ObjectHeaderSize // StreamMetadata object header
		+ (
			Unsafe.SizeOf<long?>() // MaxCount
			+ Unsafe.SizeOf<TimeSpan?>() // MaxAge
			+ Unsafe.SizeOf<long?>() // TruncateBefore
			+ Unsafe.SizeOf<bool?>() // TempStream
			+ Unsafe.SizeOf<TimeSpan?>() // CacheControl
			+ Unsafe.SizeOf<StreamAcl>()
		).RoundUpToMultipleOf(IntPtr.Size) // padding
		+ (Acl?.ApproximateSize ?? 0); // Acl
}
