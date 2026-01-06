// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Jint;
using Jint.Native;
using Jint.Native.Json;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;

namespace KurrentDB.SecondaryIndexing.Indexes.User.JavaScript;

public abstract class JsObject(Engine engine, JsonParser parser) : ObjectInstance(engine) {
	protected void SetReadOnlyProperty(string propertyName, JsValue value) {
		if (value == Undefined) {
			SetOwnProperty(propertyName, PropertyDescriptor.Undefined);
			return;
		}

		SetOwnProperty(propertyName, new PropertyDescriptor(
			value: value,
			writable: false,
			enumerable: true,
			configurable: false));
	}

	public override List<JsValue> GetOwnPropertyKeys(Types types = Types.String | Types.Symbol) {
		EnsureProperties();
		return base.GetOwnPropertyKeys(types);
	}

	public override IEnumerable<KeyValuePair<JsValue, PropertyDescriptor>> GetOwnProperties() {
		EnsureProperties();
		return base.GetOwnProperties();
	}

	protected JsValue TryParseJson(ReadOnlyMemory<byte> bytes, string propertyName) => TryParseJson(bytes, propertyName, static () => true);

	protected JsValue TryParseJson(ReadOnlyMemory<byte> bytes, string propertyName, Func<bool> checkPrerequisites) {
		if (TryGetValue(propertyName, out var value) && value is ObjectInstance objectInstance)
			return objectInstance;

		if (!checkPrerequisites())
			return Undefined;

		var parsedValue = parser.Parse(Encoding.UTF8.GetString(bytes.Span));
		SetReadOnlyProperty(propertyName, parsedValue);
		return parsedValue;
	}

	protected virtual void EnsureProperties() { }
}
