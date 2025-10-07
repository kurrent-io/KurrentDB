// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using Jint.Native.Json;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using KurrentDB.Core.Services;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Metrics;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using ILogger = Serilog.ILogger;

#nullable enable

namespace KurrentDB.Projections.Core.Services.Interpreted;

public class JintProjectionStateHandler : IProjectionStateHandler {
	private readonly ILogger _logger = Serilog.Log.ForContext<JintProjectionStateHandler>();
	private readonly bool _enableContentTypeValidation;
	private static readonly Stopwatch Watch = Stopwatch.StartNew();
	private readonly Engine _engine;
	private readonly SourceDefinitionBuilder _definitionBuilder = new();
	private readonly List<EmittedEventEnvelope> _emitted = [];
	private readonly InterpreterRuntime _interpreterRuntime;
	private readonly JsonParser _parser;
	private readonly JsSerializationMeasurer _jsSerializer;

	private CheckpointTag? _currentPosition;

	private JsValue _state = JsValue.Undefined;
	private JsValue _sharedState = JsValue.Undefined;

	public JintProjectionStateHandler(string source,
		bool enableContentTypeValidation,
		TimeSpan compilationTimeout,
		TimeSpan executionTimeout,
		JsFunctionCallMeasurer jsFunctionCaller,
		JsSerializationMeasurer jsSerializer) {
		_enableContentTypeValidation = enableContentTypeValidation;
		_jsSerializer = jsSerializer;
		_definitionBuilder.NoWhen();
		_definitionBuilder.AllEvents();
		TimeConstraint timeConstraint = new(compilationTimeout, executionTimeout);
		_engine = new(opts => opts.Constraint(timeConstraint).DisableStringCompilation());
		_interpreterRuntime = new(_engine, _definitionBuilder, jsFunctionCaller);
		_engine.Global.FastAddProperty("log", new ClrFunction(_engine, "log", Log), false, false, false);

		timeConstraint.Compiling();
		_engine.Execute(source);
		timeConstraint.Executing();
		_parser = _interpreterRuntime.SwitchToExecutionMode();

		_engine.Global.FastAddProperty("emit", new ClrFunction(_engine, "emit", Emit, 4), true, false, true);
		_engine.Global.FastAddProperty("linkTo", new ClrFunction(_engine, "linkTo", LinkTo, 3), true, false, true);
		_engine.Global.FastAddProperty("linkStreamTo", new ClrFunction(_engine, "linkStreamTo", LinkStreamTo, 3), true, false, true);
		_engine.Global.FastAddProperty("copyTo", new ClrFunction(_engine, "copyTo", CopyTo, 3), true, false, true);
	}

	public void Dispose() {
		_engine.Dispose();
	}

	public IQuerySources GetSourceDefinition() {
		_engine.Constraints.Reset();
		return _definitionBuilder.Build();
	}

	public void Load(string? state) {
		_engine.Constraints.Reset();
		if (state != null) {
			var jsValue = _parser.Parse(state);
			LoadCurrentState(jsValue);
		} else {
			LoadCurrentState(JsValue.Null);
		}
	}

	private void LoadCurrentState(JsValue jsValue) {
		if (_definitionBuilder.IsBiState) {
			if (_state == null || _state == JsValue.Undefined)
				_state = new JsArray(_engine, [JsValue.Undefined, JsValue.Undefined]);

			_state.AsArray()[0] = jsValue;
		} else {
			_state = jsValue;
		}
	}

	public void LoadShared(string? state) {
		_engine.Constraints.Reset();
		if (state != null) {
			var jsValue = _parser.Parse(state);
			LoadCurrentSharedState(jsValue);
		} else {
			LoadCurrentSharedState(JsValue.Null);
		}
	}

	private void LoadCurrentSharedState(JsValue jsValue) {
		if (_definitionBuilder.IsBiState) {
			if (_state == null || _state == JsValue.Undefined)
				_state = new JsArray(_engine, [JsValue.Undefined, JsValue.Undefined]);

			_state.AsArray()[1] = jsValue;
		} else {
			_state = jsValue;
		}
	}

	public void Initialize() {
		_engine.Constraints.Reset();
		var state = _interpreterRuntime.InitializeState();
		LoadCurrentState(state);
	}

	public void InitializeShared() {
		_engine.Constraints.Reset();
		_sharedState = _interpreterRuntime.InitializeSharedState();
		LoadCurrentSharedState(_sharedState);
	}

	public string? GetStatePartition(CheckpointTag eventPosition, string category, ResolvedEvent data) {
		_currentPosition = eventPosition;
		_engine.Constraints.Reset();
		var envelope = CreateEnvelope("", data, category);
		var partition = _interpreterRuntime.GetPartition(envelope);
		if (partition == JsValue.Null || partition == JsValue.Undefined || !(partition.IsString() || partition.IsNumber()))
			return null;

		return partition.IsNumber() ? partition.AsNumber().ToString() : partition.AsString();
	}

	public bool ProcessPartitionCreated(string partition,
		CheckpointTag createPosition,
		ResolvedEvent @event,
		out EmittedEventEnvelope[]? emittedEvents) {
		_engine.Constraints.Reset();
		_currentPosition = createPosition;
		var envelope = CreateEnvelope(partition, @event, "");
		_interpreterRuntime.HandleCreated(_state, envelope);

		emittedEvents = _emitted.Count > 0 ? _emitted.ToArray() : null;
		_emitted.Clear();
		return true;
	}

	public bool ProcessPartitionDeleted(string partition, CheckpointTag deletePosition, out string? newState) {
		_engine.Constraints.Reset();
		_currentPosition = deletePosition;

		_interpreterRuntime.HandleDeleted(_state, partition, false);
		newState = ConvertToStringHandlingNulls(_state);
		return true;
	}

	public string? TransformStateToResult() {
		_engine.Constraints.Reset();
		var result = _interpreterRuntime.TransformStateToResult(_state);
		return result == JsValue.Null || result == JsValue.Undefined ? null : Serialize(result);
	}

	public bool ProcessEvent(string partition,
		CheckpointTag eventPosition,
		string category,
		ResolvedEvent @event,
		out string? newState,
		out string? newSharedState,
		out EmittedEventEnvelope[]? emittedEvents) {
		_currentPosition = eventPosition;
		_engine.Constraints.Reset();
		if ((@event.IsJson && string.IsNullOrWhiteSpace(@event.Data)) ||
		    (!_enableContentTypeValidation && !@event.IsJson && string.IsNullOrEmpty(@event.Data))) {
			PrepareOutput(out newState, out newSharedState, out emittedEvents);
			return true;
		}

		var envelope = CreateEnvelope(partition, @event, category);
		_state = _interpreterRuntime.Handle(_state, envelope);
		PrepareOutput(out newState, out newSharedState, out emittedEvents);
		return true;
	}

	private void PrepareOutput(out string? newState, out string? newSharedState, out EmittedEventEnvelope[]? emittedEvents) {
		emittedEvents = _emitted.Count > 0 ? _emitted.ToArray() : null;
		_emitted.Clear();
		if (_definitionBuilder.IsBiState && _state.IsArray()) {
			var arr = _state.AsArray();
			if (arr.TryGetValue(0, out var state)) {
				newState = _state.IsString() ? _state.AsString() : ConvertToStringHandlingNulls(state);
			} else {
				newState = "";
			}

			newSharedState = arr.TryGetValue(1, out var sharedState) ? ConvertToStringHandlingNulls(sharedState) : null;
		} else if (_state.IsString()) {
			newState = _state.AsString();
			newSharedState = null;
		} else {
			newState = ConvertToStringHandlingNulls(_state);
			newSharedState = null;
		}
	}

	private string? ConvertToStringHandlingNulls(JsValue value) => value.IsNull() || value.IsUndefined() ? null : Serialize(value);

	private ExtraMetaData ToExtraMetaData(ObjectInstance md) {
		var meta = EnsureNonNullObjectValue(md, "metaData");
		var d = meta.GetOwnProperties()
			.Where(kvp => kvp.Value.Value.Type is not (Types.Empty or Types.Undefined))
			.ToDictionary(kvp => kvp.Key.AsString(), kvp => Serialize(kvp.Value.Value));

		return new(d);
	}

	private JsValue Emit(JsValue thisValue, JsValue[] parameters) {
		if (parameters.Length < 3)
			throw new ArgumentException("invalid number of parameters");

		string stream = EnsureNonNullStringValue(parameters.At(0), "streamId");
		var eventType = EnsureNonNullStringValue(parameters.At(1), "eventName");
		var eventBody = EnsureNonNullObjectValue(parameters.At(2), "eventBody");

		if (parameters.Length == 4 && !parameters.At(3).IsObject())
#pragma warning disable CA2208 // ReSharper disable once NotResolvedInText
			throw new ArgumentException("object expected", "metadata");
#pragma warning restore CA2208

		var data = Serialize(eventBody);
		ExtraMetaData? metadata = null;
		if (parameters.Length == 4) {
			metadata = ToExtraMetaData(parameters.At(3).AsObject());
		}

		_emitted.Add(new(new EmittedDataEvent(stream, Guid.NewGuid(), eventType, true, data, metadata, _currentPosition, null)));
		return JsValue.Undefined;
	}

	private static ObjectInstance EnsureNonNullObjectValue(JsValue parameter, string parameterName) {
		if (parameter == JsValue.Null || parameter == JsValue.Undefined)
			throw new ArgumentNullException(parameterName);
		if (!parameter.IsObject())
			throw new ArgumentException("object expected", parameterName);
		return parameter.AsObject();
	}

	private static string EnsureNonNullStringValue(JsValue parameter, string parameterName) {
		if (parameter != JsValue.Null &&
		    parameter.IsString() &&
		    (parameter.AsString() is { } value &&
		     !string.IsNullOrWhiteSpace(value)))
			return value;

		if (parameter == JsValue.Null || parameter == JsValue.Undefined || parameter.IsString())
			throw new ArgumentNullException(parameterName);

		throw new ArgumentException("string expected", parameterName);
	}

	private string? AsString(JsValue? value, bool formatForRaw) {
		return value switch {
			JsBoolean b => b.AsBoolean() ? "true" : "false",
			JsString s => formatForRaw ? $"\"{s.AsString()}\"" : s.AsString(),
			JsNumber n => n.AsNumber().ToString(CultureInfo.InvariantCulture),
			JsNull => null,
			JsUndefined => null,
			not null => Serialize(value),
			_ => null
		};
	}

	private JsValue LinkTo(JsValue thisValue, JsValue[] parameters) {
		if (parameters.Length != 2 && parameters.Length != 3)
			throw new ArgumentException("wrong number of parameters");
		var stream = EnsureNonNullStringValue(parameters.At(0), "streamId");
		var @event = EnsureNonNullObjectValue(parameters.At(1), "event");

		if (!@event.TryGetValue("sequenceNumber", out var numberValue) | !@event.TryGetValue("streamId", out var sourceValue) ||
		    !numberValue.IsNumber() || !sourceValue.IsString()) {
			throw new Exception($"Invalid link to event {numberValue}@{sourceValue}");
		}

		var number = (long)numberValue.AsNumber();
		var source = sourceValue.AsString();
		ExtraMetaData? metadata = parameters.Length switch {
			3 => ToExtraMetaData(parameters.At(2).AsObject()),
			_ => null
		};

		_emitted.Add(new(new EmittedDataEvent(stream, Guid.NewGuid(), SystemEventTypes.LinkTo, false, $"{number}@{source}", metadata,
			_currentPosition, null)));
		return JsValue.Undefined;
	}

	private JsValue LinkStreamTo(JsValue thisValue, JsValue[] parameters) {
		var stream = EnsureNonNullStringValue(parameters.At(0), "streamId");
		var linkedStreamId = EnsureNonNullStringValue(parameters.At(1), "linkedStreamId");

		ExtraMetaData? metadata = parameters.Length switch {
			3 => ToExtraMetaData(parameters.At(2).AsObject()),
			_ => null
		};

		_emitted.Add(new(
			new EmittedDataEvent(stream, Guid.NewGuid(), SystemEventTypes.StreamReference, false, linkedStreamId, metadata,
				_currentPosition, null)));
		return JsValue.Undefined;
	}

	private JsValue CopyTo(JsValue thisValue, JsValue[] parameters) => JsValue.Undefined;

	private void Log(string message) {
		_logger.Debug(message);
	}

	private JsValue Log(JsValue thisValue, JsValue[] parameters) {
		switch (parameters.Length) {
			case 0:
				break;
			case 1: {
				var p0 = parameters.At(0);
				if (p0 != null && p0.IsPrimitive())
					Log(p0.ToString());
				if (p0 is ObjectInstance oi)
					Log(Serialize(oi));
				break;
			}
			case > 1: {
				var sb = new StringBuilder();
				for (int i = 0; i < parameters.Length; i++) {
					if (i > 1)
						sb.Append(" ,");
					var p = parameters.At(i);
					if (p != null && p.IsPrimitive())
						Log(p.ToString());
					if (p is ObjectInstance oi)
						sb.Append(Serialize(oi));
				}

				Log(sb.ToString());
				break;
			}
		}

		return JsValue.Undefined;
	}

	private class TimeConstraint : Constraint {
		private readonly TimeSpan _compilationTimeout;
		private readonly TimeSpan _executionTimeout;
		private TimeSpan _start;
		private TimeSpan _timeout;
		private bool _executing;

		public TimeConstraint(TimeSpan compilationTimeout, TimeSpan executionTimeout) {
			_compilationTimeout = compilationTimeout;
			_executionTimeout = executionTimeout;
			_timeout = _compilationTimeout;
		}

		public void Compiling() {
			_timeout = _compilationTimeout;
			_executing = false;
		}

		public void Executing() {
			_timeout = _executionTimeout;
			_executing = true;
		}

		public override void Reset() {
			_start = Watch.Elapsed;
		}

		public override void Check() {
			if (Watch.Elapsed - _start >= _timeout) {
				if (Debugger.IsAttached)
					return;
				var action = _executing ? "execute" : "compile";
				throw new TimeoutException(
					$"Projection script took too long to {action} (took: {Watch.Elapsed - _start:c}, allowed: {_timeout:c}");
			}
		}
	}

	private class InterpreterRuntime : ObjectInstance {
		private readonly Dictionary<string, ScriptFunction> _handlers = new(StringComparer.Ordinal);
		private readonly List<(TransformType, ScriptFunction)> _transforms = [];
		private readonly List<ScriptFunction> _createdHandlers = [];
		private ScriptFunction? _init;
		private ScriptFunction? _initShared;
		private ScriptFunction? _any;
		private ScriptFunction? _deleted;
		private ScriptFunction? _partitionFunction;

		private readonly JsValue _whenInstance;
		private readonly JsValue _partitionByInstance;
		private readonly JsValue _outputStateInstance;
		private readonly JsValue _foreachStreamInstance;
		private readonly JsValue _transformByInstance;
		private readonly JsValue _filterByInstance;
		private readonly JsValue _outputToInstance;
		private readonly JsValue _definesStateTransformInstance;

		private readonly SourceDefinitionBuilder _definitionBuilder;
		private readonly JsFunctionCallMeasurer _jsFunctionCaller;
		private readonly JsonParser _parser;

		private static readonly Dictionary<string, Action<InterpreterRuntime>> _possibleProperties =
			new() {
				["when"] = i => i.FastAddProperty("when", i._whenInstance, true, false, true),
				["partitionBy"] = i => i.FastAddProperty("partitionBy", i._partitionByInstance, true, false, true),
				["outputState"] = i => i.FastAddProperty("outputState", i._outputStateInstance, true, false, true),
				["foreachStream"] = i => i.FastAddProperty("foreachStream", i._foreachStreamInstance, true, false, true),
				["transformBy"] = i => i.FastAddProperty("transformBy", i._transformByInstance, true, false, true),
				["filterBy"] = i => i.FastAddProperty("filterBy", i._filterByInstance, true, false, true),
				["outputTo"] = i => i.FastAddProperty("outputTo", i._outputToInstance, true, false, true),
				["$defines_state_transform"] = i
					=> i.FastAddProperty("$defines_state_transform", i._definesStateTransformInstance, true, false, true),
			};

		private static readonly Dictionary<string, string[]> _availableProperties = new() {
			["fromStream"] = ["when", "partitionBy", "outputState"],
			["fromAll"] = ["when", "partitionBy", "outputState", "foreachStream"],
			["fromStreams"] = ["when", "partitionBy", "outputState"],
			["fromCategory"] = ["when", "partitionBy", "outputState", "foreachStream"],
			["when"] = ["transformBy", "filterBy", "outputState", "outputTo", "$defines_state_transform"],
			["foreachStream"] = ["when"],
			["outputState"] = ["transformBy", "filterBy", "outputTo"],
			["partitionBy"] = ["when"],
			["transformBy"] = ["transformBy", "filterBy", "outputState", "outputTo"],
			["filterBy"] = ["transformBy", "filterBy", "outputState", "outputTo"],
			["outputTo"] = [],
			["execution"] = []
		};

		private static readonly Dictionary<string, Action<SourceDefinitionBuilder, JsValue>> _setters =
			new(StringComparer.OrdinalIgnoreCase) {
				["$includeLinks"] = (options, value)
					=> options.SetIncludeLinks(value.IsBoolean() ? value.AsBoolean() : throw new("Invalid value")),
				["reorderEvents"] = (options, value)
					=> options.SetReorderEvents(value.IsBoolean() ? value.AsBoolean() : throw new("Invalid value")),
				["processingLag"] = (options, value)
					=> options.SetProcessingLag(value.IsNumber() ? (int)value.AsNumber() : throw new("Invalid value")),
				["resultStreamName"] = (options, value)
					=> options.SetResultStreamNameOption(value.IsString() ? value.AsString() : throw new("Invalid value")),
				["biState"] = (options, value) => options.SetIsBiState(value.IsBoolean() ? value.AsBoolean() : throw new("Invalid value"))
			};

		private readonly List<string> _definitionFunctions = [];

		public InterpreterRuntime(
			Engine engine,
			SourceDefinitionBuilder builder,
			JsFunctionCallMeasurer jsFunctionCaller) : base(engine) {
			_definitionBuilder = builder;
			_jsFunctionCaller = jsFunctionCaller;
			_parser = new JsonParser(engine);
			AddDefinitionFunction("options", SetOptions, 1);
			AddDefinitionFunction("fromStream", FromStream, 1);
			AddDefinitionFunction("fromCategory", FromCategory, 4);
			AddDefinitionFunction("fromCategories", FromCategory, 4);
			AddDefinitionFunction("fromAll", FromAll, 0);
			AddDefinitionFunction("fromStreams", FromStreams, 1);
			AddDefinitionFunction("on_event", OnEvent, 1);
			AddDefinitionFunction("on_any", OnAny, 1);
			_whenInstance = new ClrFunction(engine, "when", When, 1);
			_partitionByInstance = new ClrFunction(engine, "partitionBy", PartitionBy, 1);
			_outputStateInstance = new ClrFunction(engine, "outputState", OutputState, 1);
			_foreachStreamInstance = new ClrFunction(engine, "foreachStream", ForEachStream, 1);
			_transformByInstance = new ClrFunction(engine, "transformBy", TransformBy, 1);
			_filterByInstance = new ClrFunction(engine, "filterBy", FilterBy, 1);
			_outputToInstance = new ClrFunction(engine, "outputTo", OutputTo, 1);
			_definesStateTransformInstance = new ClrFunction(engine, "$defines_state_transform", DefinesStateTransform);
		}

		private void AddDefinitionFunction(string name, Func<JsValue, JsValue[], JsValue> func, int length) {
			_definitionFunctions.Add(name);
			_engine.Global.FastAddProperty(name, new ClrFunction(_engine, name, func, length), true, false, true);
		}

		private JsValue FromStream(JsValue _, JsValue[] parameters) {
			var stream = parameters.At(0);
			if (stream is not JsString)
				throw new ArgumentException("stream");
			_definitionBuilder.FromStream(stream.AsString());
			RestrictProperties("fromStream");

			return this;
		}

		private JsValue FromCategory(JsValue thisValue, JsValue[] parameters) {
			switch (parameters.Length) {
				case 0:
					return this;
				case 1 when parameters.At(0).IsArray(): {
					foreach (var cat in parameters.At(0).AsArray()) {
						if (cat is not JsString s) {
							throw new ArgumentException("categories");
						}

						_definitionBuilder.FromStream($"$ce-{s.AsString()}");
					}

					break;
				}
				case > 1: {
					foreach (var cat in parameters) {
						if (cat is not JsString s) {
							throw new ArgumentException("categories");
						}

						_definitionBuilder.FromStream($"$ce-{s.AsString()}");
					}

					break;
				}
				default: {
					var p0 = parameters.At(0);
					if (p0 is not JsString s)
						throw new ArgumentException("category");
					_definitionBuilder.FromCategory(s.AsString());
					break;
				}
			}

			RestrictProperties("fromCategory");

			return this;
		}

		private JsValue When(JsValue thisValue, JsValue[] parameters) {
			if (parameters.At(0) is ObjectInstance handlers) {
				foreach (var kvp in handlers.GetOwnProperties()) {
					if (kvp.Key.IsString() && kvp.Value.Value is ScriptFunction) {
						var key = kvp.Key.AsString();
						AddHandler(key, (ScriptFunction)kvp.Value.Value);
					}
				}
			}

			_definitionBuilder.SetDefinesFold();
			RestrictProperties("when");
			return this;
		}

		private JsValue PartitionBy(JsValue thisValue, JsValue[] parameters) {
			if (parameters.At(0) is ScriptFunction partitionFunction) {
				_definitionBuilder.SetByCustomPartitions();
				_partitionFunction = partitionFunction;
				RestrictProperties("partitionBy");
				return this;
			}

			throw new ArgumentException("partitionBy");
		}

		private JsValue ForEachStream(JsValue thisValue, JsValue[] parameters) {
			_definitionBuilder.SetByStream();
			RestrictProperties("foreachStream");
			return this;
		}

		private JsValue OutputState(JsValue thisValue, JsValue[] parameters) {
			RestrictProperties("outputState");
			_definitionBuilder.SetOutputState();
			return this;
		}

		private JsValue OutputTo(JsValue thisValue, JsValue[] parameters) {
			if (parameters.Length != 1 && parameters.Length != 2)
				throw new ArgumentException("invalid number of parameters");
			if (!parameters.At(0).IsString())
#pragma warning disable CA2208 // ReSharper disable NotResolvedInText
				throw new ArgumentException("expected string value", "resultStream");
			if (parameters.Length == 2 && !parameters.At(1).IsString())
				throw new ArgumentException("expected string value", "partitionResultStreamPattern");
#pragma warning restore CA2208 // ReSharper restore NotResolvedInText
			_definitionBuilder.SetResultStreamNameOption(parameters.At(0).AsString());
			if (parameters.Length == 2)
				_definitionBuilder.SetPartitionResultStreamNamePatternOption(parameters.At(1).AsString());
			RestrictProperties("outputTo");
			return this;
		}

		private JsValue DefinesStateTransform(JsValue thisValue, JsValue[] parameters) {
			_definitionBuilder.SetDefinesStateTransform();
			_definitionBuilder.SetOutputState();
			return Undefined;
		}

		private JsValue FilterBy(JsValue thisValue, JsValue[] parameters) {
			if (parameters.At(0) is not ScriptFunction fi) throw new ArgumentException("expected function");

			_definitionBuilder.SetDefinesStateTransform();
			_definitionBuilder.SetOutputState();
			_transforms.Add((TransformType.Filter, fi));
			RestrictProperties("filterBy");
			return this;
		}

		private JsValue TransformBy(JsValue thisValue, JsValue[] parameters) {
			if (parameters.At(0) is not ScriptFunction fi) throw new ArgumentException("expected function");

			_definitionBuilder.SetDefinesStateTransform();
			_definitionBuilder.SetOutputState();
			_transforms.Add((TransformType.Transform, fi));
			RestrictProperties("transformBy");
			return this;
		}

		private JsValue OnEvent(JsValue thisValue, JsValue[] parameters) {
			if (parameters.Length != 2)
				throw new ArgumentException("invalid number of parameters");
			var eventName = parameters.At(0);
			var handler = parameters.At(1);
			if (!eventName.IsString())
				throw new ArgumentException("eventName");
			if (handler is not ScriptFunction fi)
				throw new ArgumentException("eventHandler");
			AddHandler(eventName.AsString(), fi);
			return Undefined;
		}

		private JsValue OnAny(JsValue thisValue, JsValue[] parameters) {
			if (parameters.Length != 1)
				throw new ArgumentException("invalid number of parameters");
			if (parameters.At(0) is not ScriptFunction fi)
				throw new ArgumentException("eventHandler");
			AddHandler("$any", fi);
			return Undefined;
		}

		private void AddHandler(string name, ScriptFunction handler) {
			switch (name) {
				case "$init":
					_init = handler;
					break;
				case "$initShared":
					_definitionBuilder.SetIsBiState(true);
					_initShared = handler;
					break;
				case "$any":
					_any = handler;
					_definitionBuilder.AllEvents();
					break;
				case "$created":
					_createdHandlers.Add(handler);
					break;
				case "$deleted" when !_definitionBuilder.IsBiState:
					_definitionBuilder.SetHandlesStreamDeletedNotifications();
					_deleted = handler;
					break;
				case "$deleted" when _definitionBuilder.IsBiState:
					throw new Exception("Cannot handle deletes in bi-state projections");
				default:
					_definitionBuilder.NotAllEvents();
					_definitionBuilder.IncludeEvent(name);
					_handlers.Add(name, handler);
					break;
			}
		}

		private void RestrictProperties(string state) {
			var allowed = _availableProperties[state];
			var current = GetOwnPropertyKeys();
			foreach (var p in current) {
				if (!allowed.Contains(p.AsString())) {
					RemoveOwnProperty(p);
				}
			}

			foreach (var p in allowed) {
				if (!HasOwnProperty(p)) {
					_possibleProperties[p](this);
				}
			}
		}

		public JsValue InitializeState() {
			return _init == null ? new JsObject(Engine) : _jsFunctionCaller.Call("$init", _init);
		}

		public JsValue InitializeSharedState() {
			return _initShared == null ? new JsObject(Engine) : _jsFunctionCaller.Call("$initShared", _initShared);
		}

		public JsValue Handle(JsValue state, EventEnvelope eventEnvelope) {
			JsValue newState;
			if (_handlers.TryGetValue(eventEnvelope.EventType, out var handler)) {
				newState = _jsFunctionCaller.Call(eventEnvelope.EventType, handler, state, FromObject(Engine, eventEnvelope));
			} else if (_any != null) {
				newState = _jsFunctionCaller.Call("$any", _any, state, FromObject(Engine, eventEnvelope));
			} else {
				newState = eventEnvelope.BodyRaw;
			}

			return newState == Undefined ? state : newState;
		}

		public JsValue TransformStateToResult(JsValue state) {
			foreach (var (type, transform) in _transforms) {
				switch (type) {
					case TransformType.Transform:
						state = _jsFunctionCaller.Call("transformBy", transform, state);
						break;
					case TransformType.Filter: {
						var result = _jsFunctionCaller.Call("filterBy", transform, state);
						if (!(result.IsBoolean() && result.AsBoolean()) || result == Null || result == Undefined) {
							return Null;
						}

						break;
					}
					case TransformType.None:
						throw new InvalidOperationException("Unknown transform type");
				}

				if (state == Null || state == Undefined)
					return Null;
			}

			return state;
		}

		private JsValue FromAll(JsValue _, JsValue[] __) {
			_definitionBuilder.FromAll();
			RestrictProperties("fromAll");
			return this;
		}

		private JsValue FromStreams(JsValue _, JsValue[] parameters) {
			IEnumerator<JsValue>? streams = null;
			try {
				streams = parameters.At(0).IsArray()
					? parameters.At(0).AsArray().GetEnumerator()
					: parameters.AsEnumerable().GetEnumerator();
				while (streams.MoveNext()) {
					if (!streams.Current.IsString())
						throw new ArgumentException("streams");
					_definitionBuilder.FromStream(streams.Current.AsString());
				}
			} finally {
				streams?.Dispose();
			}

			RestrictProperties("fromStreams");
			return this;
		}

		private JsValue SetOptions(JsValue thisValue, JsValue[] parameters) {
			var p0 = parameters.At(0);
			if (p0 is ObjectInstance opts) {
				foreach (var kvp in opts.GetOwnProperties()) {
					if (_setters.TryGetValue(kvp.Key.AsString(), out var setter)) {
						setter(_definitionBuilder, kvp.Value.Value);
					} else {
						throw new Exception($"Unrecognized option: {kvp.Key}");
					}
				}
			}

			return Undefined;
		}

		public JsValue GetPartition(EventEnvelope envelope)
			=> _partitionFunction != null ? _jsFunctionCaller.Call("partitionBy", _partitionFunction, envelope) : Null;

		public void HandleCreated(JsValue state, EventEnvelope envelope) {
			for (int i = 0; i < _createdHandlers.Count; i++) {
				_jsFunctionCaller.Call("$created", _createdHandlers[i], state, envelope);
			}
		}

		private enum TransformType {
			None,
			Filter,
			Transform
		}

		public JsonParser SwitchToExecutionMode() {
			RestrictProperties("execution");
			foreach (var globalProp in _definitionFunctions) {
				_engine.Global.RemoveOwnProperty(globalProp);
			}

			return _parser;
		}

		public void HandleDeleted(JsValue state, string partition, bool isSoftDelete) {
			if (_deleted != null) {
				_jsFunctionCaller.Call("$deleted", _deleted, state, Null, partition, isSoftDelete);
			}
		}
	}

	private EventEnvelope CreateEnvelope(string partition, ResolvedEvent @event, string category)
		=> new(_engine, _parser, this) {
			Partition = partition,
			BodyRaw = @event.Data,
			MetadataRaw = @event.Metadata,
			StreamId = @event.EventStreamId,
			EventId = @event.EventId.ToString("D"),
			EventType = @event.EventType,
			LinkMetadataRaw = @event.PositionMetadata,
			IsJson = @event.IsJson,
			Category = category,
			SequenceNumber = @event.EventSequenceNumber
		};

	private sealed class EventEnvelope(Engine engine, JsonParser parser, JintProjectionStateHandler parent)
		: ObjectInstance(engine) {
		public string StreamId {
			set => SetOwnProperty("streamId", new(value, false, true, false));
		}

		public long SequenceNumber {
			set => SetOwnProperty("sequenceNumber", new(value, false, true, false));
		}

		public string EventType {
			get => parent.AsString(Get("eventType"), false) ?? "";
			set => SetOwnProperty("eventType", new(value, false, true, false));
		}

		private JsValue Body
			=> TryGetValue("body", out var value) && value is ObjectInstance oi
				? oi
				: EnsureBody(out JsValue objectInstance)
					? objectInstance
					: Undefined;

		private bool EnsureBody(out JsValue objectInstance) {
			if (IsJson && TryGetValue("bodyRaw", out var raw) && raw is not JsUndefined) {
				var body = raw.IsNull() ? raw : parser.Parse(raw.AsString());
				var pd = new PropertyDescriptor(body, false, true, false);
				SetOwnProperty("body", pd);
				SetOwnProperty("data", pd);
				objectInstance = (ObjectInstance)body;
				return true;
			}

			objectInstance = Undefined;
			return false;
		}

		public bool IsJson {
			get => Get("isJson").AsBoolean();
			set => SetOwnProperty("isJson", new(value, false, true, false));
		}

		public string? BodyRaw {
			get => parent.AsString(Get("bodyRaw"), false);
			set => SetOwnProperty("bodyRaw", new(value, false, true, false));
		}

		private JsValue Metadata
			=> TryGetValue("metadata", out var value) && value is ObjectInstance oi
				? oi
				: EnsureMetadata(out value)
					? value
					: Undefined;

		private bool EnsureMetadata(out JsValue value) {
			if (TryGetValue("metadataRaw", out var raw) && raw is not JsUndefined) {
				var metadata = raw.IsNull() ? raw : parser.Parse(raw.AsString());
				SetOwnProperty("metadata", new(metadata, false, true, false));
				value = metadata;
				return true;
			}

			value = Undefined;
			return false;
		}

		public string MetadataRaw {
			set => FastSetProperty("metadataRaw", new(value, false, true, false));
		}

		private JsValue LinkMetadata
			=> TryGetValue("linkMetadata", out var value) && value is ObjectInstance oi
				? oi
				: EnsureLinkMetadata(out value)
					? value
					: Undefined;

		private bool EnsureLinkMetadata(out JsValue value) {
			if (TryGetValue("linkMetadataRaw", out var raw) && raw is not JsUndefined) {
				var metadata = raw.IsNull() ? raw : parser.Parse(raw.AsString());
				SetOwnProperty("linkMetadata", new(metadata, false, true, false));
				value = metadata;
				return true;
			}

			value = Undefined;
			return false;
		}

		public string LinkMetadataRaw {
			set => SetOwnProperty("linkMetadataRaw", new(value, false, true, false));
		}

		public string Partition {
			set => SetOwnProperty("partition", new(value, false, true, false));
		}

		public string Category {
			set => SetOwnProperty("category", new(value, false, true, false));
		}

		public string EventId {
			set => SetOwnProperty("eventId", new(value, false, true, false));
		}

		public override JsValue Get(JsValue property, JsValue receiver) {
			return property.AsString() switch {
				"body" or "data" => Body,
				"metadata" => Metadata,
				"linkMetadata" => LinkMetadata,
				_ => base.Get(property, receiver)
			};
		}

		public override List<JsValue> GetOwnPropertyKeys(Types types = Types.String | Types.Symbol) {
			var list = base.GetOwnPropertyKeys(types);
			return list;
		}

		public override IEnumerable<KeyValuePair<JsValue, PropertyDescriptor>> GetOwnProperties() {
			if (!HasOwnProperty("body")) {
				EnsureBody(out _);
			}

			if (!HasOwnProperty("metadata")) {
				EnsureMetadata(out _);
			}

			if (!HasOwnProperty("linkMetadata")) {
				EnsureLinkMetadata(out _);
			}

			return base.GetOwnProperties();
		}
	}

	public string Serialize(JsValue value) {
		var serialized = _jsSerializer.Serialize(value);
		return Encoding.UTF8.GetString(serialized);
	}

	public class Serializer {
		private readonly WriteState[] _iterators = new WriteState[64];
		private readonly ArrayBufferWriter<byte> _bufferWriter = new(1024 * 1024);
		private readonly Utf8JsonWriter _writer;
		private readonly Dictionary<string, JsonEncodedText> _knownPropertyNames = new();
		private int _depth;

		public Serializer() {
			_writer = new Utf8JsonWriter(
				_bufferWriter,
				new JsonWriterOptions {
					Indented = false,
					SkipValidation = true,
					Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
				});
		}

		public ReadOnlyMemory<byte> Serialize(JsValue value) {
			_depth = 0;
			_bufferWriter.Clear();
			_writer.Reset();

			_iterators[_depth] = value switch {
				JsArray array => new(array),
				ObjectInstance oi => new(oi),
				_ => new(value)
			};

			ref var current = ref _iterators[0];

			while (current.Write(_writer, ref _depth, _iterators, _knownPropertyNames)) {
				current = ref _iterators[_depth];
			}

			_writer.Flush();
			return _bufferWriter.WrittenMemory;
		}

		private struct WriteState {
			private enum Type {
				Complete,
				Array,
				Object,
				Primitive,
			}

			private static readonly IEnumerator<KeyValuePair<JsValue, PropertyDescriptor>> _emptyIterator = new NoopIterator();

			private class NoopIterator : IEnumerator<KeyValuePair<JsValue, PropertyDescriptor>> {
				public KeyValuePair<JsValue, PropertyDescriptor> Current => default;

				object? IEnumerator.Current => default;

				public void Dispose() {
				}

				public bool MoveNext() => false;

				public void Reset() {
				}
			}

			public WriteState(JsArray instance) {
				_position = -1;
				_length = (int)instance.Length;
				_instance = instance;
				_type = Type.Array;
				_started = false;
				_iterator = _emptyIterator;
			}

			public WriteState(ObjectInstance instance) {
				_position = -1;
				_length = -1;
				_instance = JsValue.Null;
				_type = Type.Object;
				_started = false;
				_iterator = instance.GetOwnProperties().GetEnumerator();
			}

			public WriteState(JsValue instance) {
				if (instance.Type == Types.Object)
					throw new ArgumentException("Primitive overload called for object instance");
				_position = -1;
				_length = -1;
				_instance = instance;
				_type = Type.Primitive;
				_started = false;
				_iterator = _emptyIterator;
			}

			private readonly JsValue _instance;
			private readonly IEnumerator<KeyValuePair<JsValue, PropertyDescriptor>> _iterator;
			private readonly Type _type;
			private readonly int _length;
			private int _position;
			private bool _started;

			[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
			public bool Write(
				Utf8JsonWriter writer,
				ref int depth,
				WriteState[] writeStates,
				Dictionary<string, JsonEncodedText> knownPropertyNames) {
				switch (_type) {
					case Type.Array:
						if (_position == -1) {
							writer.WriteStartArray();
							_position++;
						}

						var instance = (JsArray)_instance;
						for (; _position < _length; _position++) {
							var value = instance[(uint)_position];
							if (value.Type == Types.Object) {
								writeStates[++depth] = value is JsArray ai
									? new(ai)
									: new(value.AsObject());

								_position++;
								return true;
							}

							SerializePrimitive(value, writer);
						}

						writer.WriteEndArray();
						break;
					case Type.Object:
						if (!_started) {
							writer.WriteStartObject();
							_started = true;
						}

						while (_iterator.MoveNext()) {
							var (name, propertyDescriptor) = _iterator.Current;
							var value = propertyDescriptor.Value;
							if (value.Type == Types.Undefined)
								continue;

							WriteMaybeCachedPropertyName(name.AsString(), knownPropertyNames, writer);
							if (value.Type == Types.Object) {
								writeStates[++depth] = value is JsArray ai
									? new(ai)
									: new(value.AsObject());

								_position++;
								return true;
							}

							SerializePrimitive(value, writer);
						}

						writer.WriteEndObject();
						break;
					case Type.Primitive:
						SerializePrimitive(_instance, writer);
						break;
				}

				writeStates[depth] = default;
				depth--;
				return depth >= 0;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
		private static void WriteMaybeCachedPropertyName(string name,
			Dictionary<string, JsonEncodedText> knownPropertyNames,
			Utf8JsonWriter writer) {
			if (!knownPropertyNames.TryGetValue(name, out var propertyName)) {
				propertyName = JsonEncodedText.Encode(name);
				if (knownPropertyNames.Count < 1000) {
					knownPropertyNames.Add(name, propertyName);
				}
			}

			writer.WritePropertyName(propertyName);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
		private static void SerializePrimitive(JsValue value, Utf8JsonWriter writer) {
			switch (value.Type) {
				case Types.Null:
				case Types.Undefined:
				case Types.Empty:
					writer.WriteNullValue();
					break;
				case Types.Boolean:
					writer.WriteBooleanValue(!ReferenceEquals(value, JsBoolean.False));
					break;
				case Types.Number:
					writer.WriteNumberValue(value.AsNumber());
					break;
				case Types.BigInt:
					writer.WriteStringValue(value.ToString());
					break;
				case Types.String:
					writer.WriteStringValue(value.AsString());
					break;
				default:
					throw new Exception($"Cannot serialize {value.Type} as primitive");
			}
		}
	}
}

internal static class ObjectInstanceExtensions {
	public static void FastAddProperty(this ObjectInstance target,
		string name,
		JsValue value,
		bool writable,
		bool enumerable,
		bool configurable) {
		target.FastSetProperty(name, new(value, writable, enumerable, configurable));
	}
}
