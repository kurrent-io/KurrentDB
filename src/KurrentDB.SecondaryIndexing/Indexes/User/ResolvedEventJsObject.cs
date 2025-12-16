// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Jint;
using Jint.Native;
using Jint.Native.Json;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;

namespace KurrentDB.SecondaryIndexing.Indexes.User;

internal sealed class ResolvedEventJsObject: ObjectInstance {
	private readonly JsonParser _parser;

	public ResolvedEventJsObject(Engine engine) : base(engine) {
		_parser = new(engine);
	}

	public override JsValue Get(JsValue property, JsValue receiver) {
		return property.AsString() switch {
			"rawData" => DataRaw,
			"data" => DataJson,
			"rawMetadata" => MetadataRaw,
			"metadata" => MetadataJson,
			_ => base.Get(property, receiver)
		};
	}

	public string StreamId {
		set => SetOwnProperty("stream", new PropertyDescriptor(
			value: value,
			writable: false,
			enumerable: true,
			configurable: false));
	}

	public long EventNumber {
		set => SetOwnProperty("number", new PropertyDescriptor(
			value: value,
			writable: false,
			enumerable: true,
			configurable: false));
	}

	public string EventType {
		set => SetOwnProperty("type", new PropertyDescriptor(
			value: value,
			writable: false,
			enumerable: true,
			configurable: false));
	}

	public string EventId {
		set => SetOwnProperty("id", new PropertyDescriptor(
			value: value,
			writable: false,
			enumerable: true,
			configurable: false));
	}

	public bool IsJson {
		set => SetOwnProperty("isJson", new PropertyDescriptor(
			value: value,
			writable: false,
			enumerable: true,
			configurable: false));
	}

	public ReadOnlyMemory<byte> Data { private get; set; }
	public ReadOnlyMemory<byte> Metadata { private get; set; }

	private JsValue DataRaw => ConvertToArrayBuffer(Data, "rawData");
	private JsValue MetadataRaw => ConvertToArrayBuffer(Metadata, "rawMetadata");

	private JsValue DataJson => TryParseJson(Data, "data", () => TryGetValue("isJson", out var isJsonValue) && isJsonValue.AsBoolean());
	private JsValue MetadataJson => TryParseJson(Metadata, "metadata");

	private JsValue TryParseJson(ReadOnlyMemory<byte> rawBytes, string propertyName) => TryParseJson(rawBytes, propertyName, static () => true);

	private JsValue TryParseJson(ReadOnlyMemory<byte> rawBytes, string propertyName, Func<bool> checkPrerequisites) {
		if (TryGetValue(propertyName, out var value) && value is ObjectInstance objectInstance)
			return objectInstance;

		if (!checkPrerequisites())
			return Undefined;

		var parsedValue = _parser.Parse(Encoding.UTF8.GetString(rawBytes.Span));
		SetOwnProperty(propertyName, new PropertyDescriptor(
			value: parsedValue,
			writable: false,
			enumerable: true,
			configurable: false));
		return parsedValue;
	}

	private JsValue ConvertToArrayBuffer(ReadOnlyMemory<byte> rawBytes, string propertyName) {
		if (TryGetValue(propertyName, out var value) && value is ObjectInstance objectInstance)
			return objectInstance;

		var arrayBuffer = FromObject(_engine, rawBytes.ToArray());
		SetOwnProperty(propertyName, new PropertyDescriptor(
			value: arrayBuffer,
			writable: false,
			enumerable: true,
			configurable: false));
		return arrayBuffer;
	}

	private void EnsureProperties() {
		_ = DataRaw;
		_ = DataJson;
		_ = MetadataRaw;
		_ = MetadataJson;
	}

	public override List<JsValue> GetOwnPropertyKeys(Types types = Types.String | Types.Symbol) {
		EnsureProperties();
		return base.GetOwnPropertyKeys(types);
	}

	public override IEnumerable<KeyValuePair<JsValue, PropertyDescriptor>> GetOwnProperties() {
		EnsureProperties();
		return base.GetOwnProperties();
	}

	public void Reset() {
		SetOwnProperty("data", PropertyDescriptor.Undefined);
		SetOwnProperty("metadata", PropertyDescriptor.Undefined);
		SetOwnProperty("rawData", PropertyDescriptor.Undefined);
		SetOwnProperty("rawMetadata", PropertyDescriptor.Undefined);
	}
}
