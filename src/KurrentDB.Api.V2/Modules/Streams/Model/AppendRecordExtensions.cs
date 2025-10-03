// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Text;
using Google.Protobuf.WellKnownTypes;
using KurrentDB.Api.Errors;
using KurrentDB.Api.Streams.Validators;
using KurrentDB.Protocol.V2.Streams;

namespace KurrentDB.Api.Streams;

public static class AppendRecordExtensions {
	/// <summary>
	/// Prepares and validates the AppendRecord by generating missing fields and enriching properties
	/// that are required for storage, while ensuring it meets all necessary criteria except size constraints.
	/// </summary>
	public static AppendRecord PrepareRecord(this AppendRecord record) {
		if (!record.HasRecordId)
			record.RecordId = Guid.NewGuid().ToString();

		if (record.Schema.Format != Protocol.V2.Streams.SchemaFormat.Json)
			record.Properties.Add(Constants.Properties.SchemaFormatKey, Value.ForString(record.Schema.Format.ToString()));

		if (record.Schema.HasId)
			record.Properties.Add(Constants.Properties.SchemaIdKey, Value.ForString(record.Schema.Id));

        var result = AppendRecordValidator.Instance.Validate(record);

        return !result.IsValid ? throw ApiErrors.InvalidRequest(typeof(AppendRequest), result) : record;
    }

    /// <summary>
    /// Estimates the size on disk of the AppendRecord including data, properties and schema name.
    /// Also indicates if it exceeds the maximum allowed size.
    /// <remarks>
    /// It is assumed that the record has been prepared using <see cref="PrepareRecord"/> before calling this method.
    /// This method does not account for any additional overhead that may be introduced by the storage engine.
    /// </remarks>
    /// </summary>
    public static (int TotalSize, int SizeExceededBy, bool ExceedsMax) CalculateSizeOnDisk(this AppendRecord record, int maxRecordSize) {
        Debug.Assert(maxRecordSize > 0, "maxRecordSize must be positive");

        var dataSize       = record.Data.Length;
        var propsSize      = new Struct { Fields = { record.Properties } }.CalculateSize();
        var schemaNameSize = Encoding.UTF8.GetByteCount(record.Schema.Name);

        var size = dataSize + propsSize + schemaNameSize;

        return size <= maxRecordSize ? (size, 0, false) : (size, size - maxRecordSize, true);
    }
}
