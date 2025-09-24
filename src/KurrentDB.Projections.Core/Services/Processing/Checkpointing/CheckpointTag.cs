// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KurrentDB.Projections.Core.Services.Processing.Checkpointing;

public class CheckpointTag : IComparable<CheckpointTag> {
	public readonly int Phase;
	public readonly TFPos Position;

	//TODO: rename to StreamsOrEventTypes or just Positions
	public readonly Dictionary<string, long> Streams;

	public readonly string CatalogStream;
	public readonly string DataStream;
	public readonly long CatalogPosition;
	public readonly long DataPosition;

	internal enum Mode {
		Phase,
		Position,
		Stream,
		MultiStream,
		EventTypeIndex,
		PreparePosition,
		ByStream
	}

	private CheckpointTag(int phase, bool completed) {
		Phase = phase;
		Position = completed ? new TFPos(long.MaxValue, long.MaxValue) : new TFPos(long.MinValue, long.MinValue);
		Streams = null;
		Mode_ = CalculateMode();
	}

	private CheckpointTag(int phase, TFPos position, Dictionary<string, long> streams) {
		Phase = phase;
		Position = position;
		Streams = streams;
		Mode_ = CalculateMode();
	}

	private CheckpointTag(int phase, long preparePosition) {
		Phase = phase;
		Position = new TFPos(long.MinValue, preparePosition);
		Mode_ = CalculateMode();
	}

	private CheckpointTag(int phase, TFPos position) {
		Phase = phase;
		Position = position;
		Mode_ = CalculateMode();
	}

	private CheckpointTag(int phase, IDictionary<string, long> streams) {
		Phase = phase;
		foreach (var stream in streams) {
			if (stream.Key == "")
				throw new ArgumentException("Empty stream name", nameof(streams));
			if (stream.Value < 0 && stream.Value != ExpectedVersion.NoStream)
				throw new ArgumentException("Invalid sequence number", nameof(streams));
		}

		Streams = new(streams); // clone
		Position = new TFPos(long.MinValue, long.MinValue);
		Mode_ = CalculateMode();
	}

	private CheckpointTag(int phase, IDictionary<string, long> eventTypes, TFPos position) {
		Phase = phase;
		Position = position;
		foreach (var stream in eventTypes) {
			if (stream.Key == "")
				throw new ArgumentException("Empty stream name", nameof(eventTypes));
			if (stream.Value < 0 && stream.Value != ExpectedVersion.NoStream)
				throw new ArgumentException("Invalid sequence number", nameof(eventTypes));
		}

		Streams = new(eventTypes); // clone
		Mode_ = CalculateMode();
	}

	private CheckpointTag(int phase, string stream, long sequenceNumber) {
		Phase = phase;
		ArgumentNullException.ThrowIfNull(stream);
		ArgumentException.ThrowIfNullOrEmpty(stream);
		if (sequenceNumber < 0 && sequenceNumber != ExpectedVersion.NoStream)
			throw new ArgumentException(null, nameof(sequenceNumber));
		Position = new TFPos(long.MinValue, long.MinValue);
		Streams = new() { { stream, sequenceNumber } };
		Mode_ = CalculateMode();
	}

	private CheckpointTag(int phase,
		string catalogStream,
		long catalogPosition,
		string dataStream,
		long dataPosition,
		long commitPosition) {
		Phase = phase;
		CatalogStream = catalogStream;
		CatalogPosition = catalogPosition;
		DataStream = dataStream;
		DataPosition = dataPosition;
		Position = new TFPos(commitPosition, long.MinValue);
		Mode_ = Mode.ByStream;
	}

	private Mode CalculateMode() {
		if (Streams == null || Streams.Count == 0)
			switch (Position.CommitPosition) {
				case long.MinValue when Position.PreparePosition == long.MinValue:
				case long.MaxValue when Position.PreparePosition == long.MaxValue:
					return Mode.Phase;
				case long.MinValue when Position.PreparePosition != long.MinValue:
					return Mode.PreparePosition;
				default:
					return Mode.Position;
			}

		return Position != new TFPos(long.MinValue, long.MinValue)
			? Mode.EventTypeIndex
			: Streams.Count == 1
				? Mode.Stream
				: Mode.MultiStream;
	}

	public static bool operator >(CheckpointTag left, CheckpointTag right) {
		if (ReferenceEquals(left, right))
			return false;
		if (!ReferenceEquals(left, null) && ReferenceEquals(right, null))
			return true;
		if (ReferenceEquals(left, null) && !ReferenceEquals(right, null))
			return false;
		if (left.Phase > right.Phase)
			return true;
		if (left.Phase < right.Phase)
			return false;
		var leftMode = left.Mode_;
		var rightMode = right.Mode_;
		UpgradeModes(ref leftMode, ref rightMode);
		if (leftMode != rightMode)
			throw new NotSupportedException("Cannot compare checkpoint tags in different modes");
		switch (leftMode) {
			case Mode.ByStream:
				CheckCatalogCompatibility(left, right);
				return left.CatalogPosition > right.CatalogPosition
				       || (left.CatalogPosition == right.CatalogPosition && left.DataPosition > right.DataPosition);
			case Mode.Phase:
			case Mode.Position:
			case Mode.EventTypeIndex:
				return left.Position > right.Position;
			case Mode.PreparePosition:
				return left.PreparePosition > right.PreparePosition;
			case Mode.Stream:
				if (left.Streams.Keys.First() != right.Streams.Keys.First())
					throw new InvalidOperationException("Cannot compare checkpoint tags across different streams");
				var result = left.Streams.Values.First() > right.Streams.Values.First();
				return result;
			case Mode.MultiStream:
				bool anyLeftGreater = left.Streams.Any(l => !right.Streams.TryGetValue(l.Key, out var rvalue) || l.Value > rvalue);
				bool anyRightGreater = right.Streams.Any(r => !left.Streams.TryGetValue(r.Key, out var lvalue) || r.Value > lvalue);
				if (anyLeftGreater && anyRightGreater)
					ThrowIncomparable(left, right);
				return anyLeftGreater;
			default:
				throw new NotSupportedException("Checkpoint tag mode is not supported in comparison");
		}
	}

	private static void CheckCatalogCompatibility(CheckpointTag left, CheckpointTag right) {
		if (left.CatalogStream != right.CatalogStream)
			throw new Exception("Cannot compare tags with different catalog streams");
	}

	private static void ThrowIncomparable(CheckpointTag left, CheckpointTag right) {
		throw new InvalidOperationException($"Incomparable multi-stream checkpoint tags. '{left}' and '{right}'");
	}

	public static bool operator >=(CheckpointTag left, CheckpointTag right) {
		if (ReferenceEquals(left, right))
			return true;
		if (!ReferenceEquals(left, null) && ReferenceEquals(right, null))
			return true;
		if (ReferenceEquals(left, null) && !ReferenceEquals(right, null))
			return false;
		if (left.Phase > right.Phase)
			return true;
		if (left.Phase < right.Phase)
			return false;
		var leftMode = left.Mode_;
		var rightMode = right.Mode_;
		UpgradeModes(ref leftMode, ref rightMode);
		if (leftMode != rightMode)
			throw new NotSupportedException("Cannot compare checkpoint tags in different modes");
		switch (leftMode) {
			case Mode.ByStream:
				CheckCatalogCompatibility(left, right);
				return left.CatalogPosition > right.CatalogPosition
				       || (left.CatalogPosition == right.CatalogPosition &&
				           left.DataPosition >= right.DataPosition);
			case Mode.Phase:
			case Mode.Position:
			case Mode.EventTypeIndex:
				return left.Position >= right.Position;
			case Mode.PreparePosition:
				return left.PreparePosition >= right.PreparePosition;
			case Mode.Stream:
				if (left.Streams.Keys.First() != right.Streams.Keys.First())
					throw new InvalidOperationException("Cannot compare checkpoint tags across different streams");
				var result = left.Streams.Values.First() >= right.Streams.Values.First();
				return result;
			case Mode.MultiStream:
				bool anyLeftGreater = left.Streams.Any(l => !right.Streams.TryGetValue(l.Key, out var rvalue) || l.Value > rvalue);
				bool anyRightGreater = right.Streams.Any(r => !left.Streams.TryGetValue(r.Key, out var lvalue) || r.Value > lvalue);

				if (anyLeftGreater && anyRightGreater)
					ThrowIncomparable(left, right);
				return !anyRightGreater;
			default:
				throw new NotSupportedException("Checkpoint tag mode is not supported in comparison");
		}
	}

	public static bool operator <(CheckpointTag left, CheckpointTag right) => !(left >= right);

	public static bool operator <=(CheckpointTag left, CheckpointTag right) => !(left > right);

	public static bool operator ==(CheckpointTag left, CheckpointTag right) => Equals(left, right);

	public static bool operator !=(CheckpointTag left, CheckpointTag right) => !(left == right);

	private bool Equals(CheckpointTag other) {
		if (Phase != other.Phase)
			return false;
		var leftMode = Mode_;
		var rightMode = other.Mode_;
		if (leftMode != rightMode)
			return false;
		UpgradeModes(ref leftMode, ref rightMode);
		switch (leftMode) {
			case Mode.ByStream:
				return CatalogStream == other.CatalogStream
				       && CatalogPosition == other.CatalogPosition
				       && DataStream == other.DataStream &&
				       DataPosition == other.DataPosition
				       && CommitPosition == other.CommitPosition;
			case Mode.Phase:
				return Position == other.Position;
			case Mode.EventTypeIndex:
				// NOTE: we ignore stream positions as they are only suggestion on
				//       where to start to gain better performance
				goto case Mode.Position;
			case Mode.Position:
				return Position == other.Position;
			case Mode.PreparePosition:
				return PreparePosition == other.PreparePosition;
			case Mode.Stream:
				if (Streams.Keys.First() != other.Streams.Keys.First())
					return false;
				var result = Streams.Values.First() == other.Streams.Values.First();
				return result;
			case Mode.MultiStream:
				return Streams.Count == other.Streams.Count
				       && Streams.All(l => other.Streams.TryGetValue(l.Key, out var rvalue) && l.Value == rvalue);
			default:
				throw new NotSupportedException("Checkpoint tag mode is not supported in comparison");
		}
	}

	public override bool Equals(object obj) {
		if (ReferenceEquals(null, obj))
			return false;
		if (ReferenceEquals(this, obj))
			return true;
		if (obj.GetType() != GetType())
			return false;
		return Equals((CheckpointTag)obj);
	}

	public override int GetHashCode() => Position.GetHashCode();

	public long? CommitPosition {
		get {
			var commitPosition = Position.CommitPosition;
			return Mode_ switch {
				Mode.ByStream => commitPosition == long.MinValue ? null : commitPosition,
				Mode.Position or Mode.EventTypeIndex => commitPosition,
				_ => null
			};
		}
	}

	public long? PreparePosition {
		get {
			return Mode_ switch {
				Mode.Position or Mode.PreparePosition or Mode.EventTypeIndex => Position.PreparePosition,
				_ => null
			};
		}
	}

	public static CheckpointTag Empty { get; } = new(-1, false);

	internal readonly Mode Mode_;

	public static CheckpointTag FromPhase(int phase, bool completed) => new(phase, completed);

	public static CheckpointTag FromPosition(int phase, long commitPosition, long preparePosition)
		=> new(phase, new TFPos(commitPosition, preparePosition));

	public static CheckpointTag FromPosition(int phase, TFPos position) => new(phase, position);

	public static CheckpointTag FromPreparePosition(int phase, long preparePosition) => new(phase, preparePosition);

	public static CheckpointTag FromStreamPosition(int phase, string stream, long sequenceNumber) => new(phase, stream, sequenceNumber);

	// streams cloned inside
	public static CheckpointTag FromStreamPositions(int phase, IDictionary<string, long> streams) => new(phase, streams);

	public static CheckpointTag FromEventTypeIndexPositions(int phase, TFPos position, IDictionary<string, long> streams) =>
		// streams cloned inside
		new(phase, streams, position);

	public static CheckpointTag FromByStreamPosition(
		int phase,
		string catalogStream,
		long catalogPosition,
		string dataStream,
		long dataPosition,
		long commitPosition)
		=> new(phase, catalogStream, catalogPosition, dataStream, dataPosition, commitPosition);

	public int CompareTo(CheckpointTag other) => this < other ? -1 : this > other ? 1 : 0;

	public override string ToString() {
		string result;
		switch (Mode_) {
			case Mode.Phase:
				return $"Phase: {Phase}{(Completed ? " (completed)" : "")}";
			case Mode.Position:
				result = Position.ToString();
				break;
			case Mode.PreparePosition:
				result = PreparePosition.ToString();
				break;
			case Mode.Stream:
				result = $"{Streams.Keys.First()}: {Streams.Values.First()}";
				break;
			case Mode.MultiStream:
			case Mode.EventTypeIndex:
				var sb = new StringBuilder();
				if (Mode_ == Mode.EventTypeIndex) {
					sb.Append(Position.ToString());
					sb.Append("; ");
				}

				foreach (var stream in Streams) {
					sb.Append($"{stream.Key}: {stream.Value}; ");
				}

				result = sb.ToString();
				break;
			case Mode.ByStream:
				result = $"{CatalogStream}:{CatalogPosition}/{DataStream}:{DataPosition}/{CommitPosition}";
				break;
			default:
				return $"Unsupported mode: {Mode_}";
		}

		return Phase == 0 ? result : $"({Phase}) {result}";
	}

	private bool Completed => Position.CommitPosition == long.MaxValue;

	private static void UpgradeModes(ref Mode leftMode, ref Mode rightMode) {
		switch (leftMode) {
			case Mode.Stream when rightMode == Mode.MultiStream:
				leftMode = Mode.MultiStream;
				return;
			case Mode.MultiStream when rightMode == Mode.Stream:
				rightMode = Mode.MultiStream;
				return;
			case Mode.Position when rightMode == Mode.EventTypeIndex:
				leftMode = Mode.EventTypeIndex;
				return;
			case Mode.EventTypeIndex when rightMode == Mode.Position:
				rightMode = Mode.EventTypeIndex;
				return;
		}
	}

	public CheckpointTag UpdateStreamPosition(string streamId, long eventSequenceNumber) {
		if (Mode_ != Mode.MultiStream)
			throw new ArgumentException("Invalid tag mode", "tag");
		var resultDictionary = PatchStreamsDictionary(streamId, eventSequenceNumber);
		return FromStreamPositions(Phase, resultDictionary);
	}

	public CheckpointTag UpdateEventTypeIndexPosition(TFPos position, string eventType, long eventSequenceNumber) {
		if (Mode_ != Mode.EventTypeIndex)
			throw new ArgumentException("Invalid tag mode", "tag");
		var resultDictionary = PatchStreamsDictionary(eventType, eventSequenceNumber);
		return FromEventTypeIndexPositions(Phase, position, resultDictionary);
	}

	public CheckpointTag UpdateEventTypeIndexPosition(TFPos position) {
		return Mode_ == Mode.EventTypeIndex
			? FromEventTypeIndexPositions(Phase, position, Streams)
			: throw new ArgumentException("Invalid tag mode", "tag");
	}

	private Dictionary<string, long> PatchStreamsDictionary(string streamId, long eventSequenceNumber) {
		var resultDictionary = new Dictionary<string, long>();
		var was = false;
		foreach (var stream in Streams) {
			if (stream.Key == streamId) {
				was = true;
				resultDictionary.Add(stream.Key, eventSequenceNumber < stream.Value ? stream.Value : eventSequenceNumber);
			} else {
				resultDictionary.Add(stream.Key, stream.Value);
			}
		}

		if (!was)
			throw new ArgumentException($"Key not found: {streamId}", nameof(streamId));
		if (resultDictionary.Count < Streams.Count)
			resultDictionary.Add(streamId, eventSequenceNumber);
		return resultDictionary;
	}

	public byte[] ToJsonBytes(ProjectionVersion projectionVersion, IEnumerable<KeyValuePair<string, JToken>> extraMetaData = null) {
		if (projectionVersion.ProjectionId == -1)
			throw new ArgumentException("projectionId is required", nameof(projectionVersion));

		using var memoryStream = new MemoryStream();
		using (var textWriter = new StreamWriter(memoryStream, Helper.UTF8NoBom))
		using (var jsonWriter = new JsonTextWriter(textWriter)) {
			WriteTo(projectionVersion, extraMetaData, jsonWriter);
		}

		return memoryStream.ToArray();
	}

	public string ToJsonString(ProjectionVersion projectionVersion, IEnumerable<KeyValuePair<string, JToken>> extraMetaData = null) {
		if (projectionVersion.ProjectionId == -1)
			throw new ArgumentException("projectionId is required", nameof(projectionVersion));

		using var textWriter = new StringWriter();
		using (var jsonWriter = new JsonTextWriter(textWriter)) {
			WriteTo(projectionVersion, extraMetaData, jsonWriter);
		}

		return textWriter.ToString();
	}

	public string ToJsonString(IEnumerable<KeyValuePair<string, JToken>> extraMetaData = null) {
		using var textWriter = new StringWriter();
		using (var jsonWriter = new JsonTextWriter(textWriter)) {
			WriteTo(default, extraMetaData, jsonWriter);
		}

		return textWriter.ToString();
	}

	public JRaw ToJsonRaw(IEnumerable<KeyValuePair<string, JToken>> extraMetaData = null) {
		using var textWriter = new StringWriter();
		using (var jsonWriter = new JsonTextWriter(textWriter)) {
			WriteTo(default, extraMetaData, jsonWriter);
		}

		return new(textWriter.ToString());
	}

	private void WriteTo(ProjectionVersion projectionVersion,
		IEnumerable<KeyValuePair<string, JToken>> extraMetaData,
		JsonWriter jsonWriter) {
		jsonWriter.WriteStartObject();
		if (projectionVersion.ProjectionId > 0) {
			jsonWriter.WritePropertyName("$v");
			WriteVersion(projectionVersion, jsonWriter);
		}

		if (Phase != 0) {
			jsonWriter.WritePropertyName("$ph");
			jsonWriter.WriteValue(Phase);
		}

		switch (Mode_) {
			case Mode.Phase:
				jsonWriter.WritePropertyName("$cp");
				jsonWriter.WriteValue(Completed);
				break;
			case Mode.Position:
			case Mode.EventTypeIndex:
				jsonWriter.WritePropertyName("$c");
				jsonWriter.WriteValue(CommitPosition.GetValueOrDefault());
				jsonWriter.WritePropertyName("$p");
				jsonWriter.WriteValue(PreparePosition.GetValueOrDefault());
				if (Mode_ == Mode.EventTypeIndex)
					goto case Mode.MultiStream;
				break;
			case Mode.PreparePosition:
				jsonWriter.WritePropertyName("$p");
				jsonWriter.WriteValue(PreparePosition.GetValueOrDefault());
				break;
			case Mode.Stream:
			case Mode.MultiStream:
				jsonWriter.WritePropertyName("$s");
				jsonWriter.WriteStartObject();
				foreach (var stream in Streams) {
					jsonWriter.WritePropertyName(stream.Key);
					jsonWriter.WriteValue(stream.Value);
				}

				jsonWriter.WriteEndObject();
				break;
			case Mode.ByStream:
				jsonWriter.WritePropertyName("$m");
				jsonWriter.WriteValue("bs");
				jsonWriter.WritePropertyName("$c");
				jsonWriter.WriteValue(CommitPosition.GetValueOrDefault());
				jsonWriter.WritePropertyName("$s");
				jsonWriter.WriteStartArray();
				jsonWriter.WriteStartObject();
				jsonWriter.WritePropertyName(CatalogStream);
				jsonWriter.WriteValue(CatalogPosition);
				jsonWriter.WriteEndObject();
				if (!string.IsNullOrEmpty(DataStream)) {
					jsonWriter.WriteStartObject();
					jsonWriter.WritePropertyName(DataStream);
					jsonWriter.WriteValue(DataPosition);
					jsonWriter.WriteEndObject();
				}

				jsonWriter.WriteEndArray();
				break;
		}

		if (extraMetaData != null) {
			foreach (var pair in extraMetaData) {
				jsonWriter.WritePropertyName(pair.Key);
				pair.Value.WriteTo(jsonWriter);
			}
		}

		jsonWriter.WriteEndObject();
	}

	private static void WriteVersion(ProjectionVersion projectionVersion, JsonWriter jsonWriter) {
		jsonWriter.WriteValue(
			$"{projectionVersion.ProjectionId}:{projectionVersion.Epoch}:{projectionVersion.Version}:{ProjectionsSubsystem.VERSION}");
	}

	public static CheckpointTagVersion FromJson(JsonReader reader,
		ProjectionVersion current,
		bool skipStartObject = false) {
		if (!skipStartObject)
			Check(reader.Read());
		Check(JsonToken.StartObject, reader);
		long? commitPosition = null;
		long? preparePosition = null;
		string catalogStream = null;
		string dataStream = null;
		long? catalogPosition = null;
		long? dataPosition = null;
		bool byStreamMode = false;
		Dictionary<string, long> streams = null;
		Dictionary<string, JToken> extra = null;
		var projectionId = current.ProjectionId;
		var projectionEpoch = 0;
		int projectionVersion = 0;
		var projectionSystemVersion = 0;
		int projectionPhase = 0;
		while (true) {
			Check(reader.Read());
			if (reader.TokenType == JsonToken.EndObject)
				break;
			Check(JsonToken.PropertyName, reader);
			var name = (string)reader.Value;
			switch (name) {
				case "$cp":
					Check(reader.Read());
					var completed = (bool)reader.Value;
					commitPosition = completed ? long.MaxValue : long.MinValue;
					preparePosition = completed ? long.MaxValue : long.MinValue;
					break;
				case "$v":
				case "v":
					Check(reader.Read());
					if (reader.ValueType == typeof(long)) {
						var v = (int)(long)reader.Value;
						if (v > 0) // TODO: remove this if with time
							projectionVersion = v;
					} else {
						//TODO: better handle errors
						var v = (string)reader.Value;
						string[] parts = v.Split(':');
						if (parts.Length == 2) {
							projectionVersion = int.Parse(parts[1]);
						} else {
							projectionId = int.Parse(parts[0]);
							projectionEpoch = int.Parse(parts[1]);
							projectionVersion = int.Parse(parts[2]);
							if (parts.Length >= 4)
								projectionSystemVersion = int.Parse(parts[3]);
						}
					}

					break;
				case "$c":
				case "c":
				case "commitPosition":
					Check(reader.Read());
					commitPosition = (long)reader.Value;
					break;
				case "$p":
				case "p":
				case "preparePosition":
					Check(reader.Read());
					preparePosition = (long)reader.Value;
					break;
				case "$s":
				case "s":
				case "streams":
					Check(reader.Read());
					if (reader.TokenType == JsonToken.StartArray) {
						Check(reader.Read());
						Check(JsonToken.StartObject, reader);
						Check(reader.Read());
						Check(JsonToken.PropertyName, reader);
						catalogStream = (string)reader.Value;
						Check(reader.Read());
						catalogPosition = (long)reader.Value;
						Check(reader.Read());
						Check(JsonToken.EndObject, reader);

						Check(reader.Read());
						if (reader.TokenType == JsonToken.StartObject) {
							Check(reader.Read());
							Check(JsonToken.PropertyName, reader);
							dataStream = (string)reader.Value;
							Check(reader.Read());
							dataPosition = (long)reader.Value;
							Check(reader.Read());
							Check(JsonToken.EndObject, reader);
							Check(reader.Read());
						}

						Check(JsonToken.EndArray, reader);
					} else {
						Check(JsonToken.StartObject, reader);
						streams = new();
						while (true) {
							Check(reader.Read());
							if (reader.TokenType == JsonToken.EndObject)
								break;
							Check(JsonToken.PropertyName, reader);
							var streamName = (string)reader.Value;
							Check(reader.Read());
							long position = (long)reader.Value;
							streams.Add(streamName, position);
						}
					}

					break;
				case "$ph":
					Check(reader.Read());
					projectionPhase = (int)(long)reader.Value;
					break;
				case "$m":
					Check(reader.Read());
					var readMode = (string)reader.Value;
					if (readMode != "bs")
						throw new ApplicationException($"Unknown checkpoint tag mode: {readMode}");
					byStreamMode = true;
					break;
				default:
					extra ??= new();
					Check(reader.Read());
					var jToken = JToken.ReadFrom(reader);
					extra.Add(name, jToken);
					break;
			}
		}

		return new() {
			Tag = byStreamMode
				? new CheckpointTag(
					projectionPhase, catalogStream, catalogPosition.GetValueOrDefault(), dataStream,
					dataPosition ?? -1, commitPosition.GetValueOrDefault())
				: new(projectionPhase, new TFPos(commitPosition ?? long.MinValue, preparePosition ?? long.MinValue), streams),
			Version = new(projectionId, projectionEpoch, projectionVersion),
			SystemVersion = projectionSystemVersion,
			ExtraMetadata = extra
		};
	}

	private static void Check(JsonToken type, JsonReader reader) {
		if (reader.TokenType != type)
			throw new Exception("Invalid JSON");
	}

	public static void Check(bool read) {
		if (!read)
			throw new Exception("Invalid JSON");
	}
}
