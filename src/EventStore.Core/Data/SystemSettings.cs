// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using EventStore.Common.Utils;
using EventStore.Core.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EventStore.Core.Data;

public class SystemSettings {
	public static readonly SystemSettings Default = new SystemSettings(
		new StreamAcl(SystemRoles.All, SystemRoles.All, SystemRoles.All, SystemRoles.All, SystemRoles.All),
		new StreamAcl(SystemRoles.Admins, SystemRoles.Admins, SystemRoles.Admins, SystemRoles.Admins,
			SystemRoles.Admins));

	public readonly StreamAcl UserStreamAcl;
	public readonly StreamAcl SystemStreamAcl;

	public SystemSettings(StreamAcl userStreamAcl, StreamAcl systemStreamAcl) {
		UserStreamAcl = userStreamAcl;
		SystemStreamAcl = systemStreamAcl;
	}

	public override string ToString() {
		return string.Format("UserStreamAcl: ({0}), SystemStreamAcl: ({1})", UserStreamAcl, SystemStreamAcl);
	}

	public static SystemSettings FromJsonBytes(ReadOnlyMemory<byte> json) {
		using (var reader = new JsonTextReader(new StreamReader(new MemoryStream(json.ToArray())))) {
			Check(reader.Read(), reader);
			Check(JsonToken.StartObject, reader);

			StreamAcl userStreamAcl = null;
			StreamAcl systemStreamAcl = null;

			while (true) {
				Check(reader.Read(), reader);
				if (reader.TokenType == JsonToken.EndObject)
					break;
				Check(JsonToken.PropertyName, reader);
				var name = (string)reader.Value;
				switch (name) {
					case SystemMetadata.UserStreamAcl:
						userStreamAcl = StreamMetadata.ReadAcl(reader);
						break;
					case SystemMetadata.SystemStreamAcl:
						systemStreamAcl = StreamMetadata.ReadAcl(reader);
						break;
					default: {
						Check(reader.Read(), reader);
						// skip
						JToken.ReadFrom(reader);
						break;
					}
				}
			}

			return new SystemSettings(userStreamAcl, systemStreamAcl);
		}
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
		if (UserStreamAcl != null) {
			jsonWriter.WritePropertyName(SystemMetadata.UserStreamAcl);
			StreamMetadata.WriteAcl(jsonWriter, UserStreamAcl);
		}

		if (SystemStreamAcl != null) {
			jsonWriter.WritePropertyName(SystemMetadata.SystemStreamAcl);
			StreamMetadata.WriteAcl(jsonWriter, SystemStreamAcl);
		}

		jsonWriter.WriteEndObject();
	}
}
