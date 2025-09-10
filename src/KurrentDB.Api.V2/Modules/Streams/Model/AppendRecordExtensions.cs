// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Streams;

public static class AppendRecordExtensions {
	/// <summary>
	/// Prepares the AppendRecord by generating missing fields and enriching properties that are required for storage.
	/// This method modifies the input record and returns it for convenience.
	/// </summary>
	public static AppendRecord PrepareRecord(this AppendRecord record, TimeProvider time) {
		// generate missing fields
		if (!record.HasRecordId)
			record.RecordId = Guid.NewGuid().ToString();

		if (!record.HasTimestamp)
			record.Timestamp = time.GetUtcNow().ToUnixTimeMilliseconds();

		// enrich properties
		record.Properties.Add(Constants.Properties.RecordTimestampKey, Value.ForNumber(record.Timestamp));

		if (record.Schema.Format != Protocol.V2.Streams.SchemaFormat.Json)
			record.Properties.Add(Constants.Properties.SchemaFormatKey, Value.ForString(record.Schema.Format.ToString()));

		if (record.Schema.HasId)
			record.Properties.Add(Constants.Properties.SchemaIdKey, Value.ForString(record.Schema.Id));

		return record;
	}

	public static AppendRecord EnsureValid(this AppendRecord record) =>
		AppendRecordValidator.Instance.EnsureValid(record);

	/// <summary>
	/// Calculates the size on disk of the AppendRecord including data, properties and schema name.
	/// Also indicates if it exceeds the maximum allowed size.
	/// </summary>
	public static AppendRecord CalculateSizeOnDisk(this AppendRecord record, out (int TotalSize, int SizeExceededBy, bool RecordTooBig) result) {
		var dataSize       = record.Data.Length;
		var propsSize      = new Struct { Fields = { record.Properties } }.CalculateSize();
		var schemaNameSize = Encoding.UTF8.GetByteCount(record.Schema.Name);

		var size     = dataSize + propsSize + schemaNameSize;
		var exceeded = size - TFConsts.EffectiveMaxLogRecordSize;

		result = (size, exceeded, exceeded > 0);

		return record;
	}
}
