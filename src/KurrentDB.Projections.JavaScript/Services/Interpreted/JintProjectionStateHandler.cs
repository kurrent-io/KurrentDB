// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
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
	private static readonly Stopwatch _sw = Stopwatch.StartNew();
	private readonly Engine _engine;
	private readonly SourceDefinitionBuilder _definitionBuilder;
	private readonly List<EmittedEventEnvelope> _emitted;
	private readonly InterpreterRuntime _interpreterRuntime;
	private readonly JsonParser _parser;
	private readonly JsonSerializer _serializer;
	private readonly JsSerializationMeasurer _jsSerializer;

	private CheckpointTag? _currentPosition;

	private JsValue _state;
	private JsValue _sharedState;

	public JintProjectionStateHandler(string source, bool enableContentTypeValidation,
		TimeSpan compilationTimeout, TimeSpan executionTimeout,
		JsFunctionCallMeasurer jsFunctionCaller,
		JsSerializationMeasurer jsSerializer) {

		_enableContentTypeValidation = enableContentTypeValidation;
		_jsSerializer = jsSerializer;
		_definitionBuilder = new SourceDefinitionBuilder();
		_definitionBuilder.NoWhen();
		_definitionBuilder.AllEvents();
		TimeConstraint timeConstraint = new(compilationTimeout, executionTimeout);
		_engine = new Engine(opts => opts.Constraint(timeConstraint).DisableStringCompilation());
		_serializer = new JsonSerializer(_engine);
		RestoreBigIntSerialization();
		_state = JsValue.Undefined;
		_sharedState = JsValue.Undefined;
		_interpreterRuntime = new InterpreterRuntime(_engine, _definitionBuilder, jsFunctionCaller);
		_engine.Global.FastSetProperty("log", new PropertyDescriptor(new ClrFunction(_engine, "log", Log), PropertyFlag.AllForbidden));

		timeConstraint.Compiling();
		_engine.Execute(source);
		timeConstraint.Executing();
		_parser = _interpreterRuntime.SwitchToExecutionMode();


		AddGlobalFunction("emit", Emit, 4);
		AddGlobalFunction("linkTo", LinkTo, 3);
		AddGlobalFunction("linkStreamTo", LinkStreamTo, 3);
		AddGlobalFunction("copyTo", CopyTo, 3);
		_emitted = new List<EmittedEventEnvelope>();
	}

	/// <summary>
	/// Makes a BigInt in projection state serialize as a JSON string of its digits, which is what the
	/// hand-written serializer this engine used to run wrote for one.
	/// </summary>
	/// <remarks>
	/// A BigInt has no JSON representation, so JSON.stringify -- which state serialization now goes
	/// through -- throws on one. For a projection already running with a BigInt in its state that would
	/// mean faulting at serialization and halting checkpointing on upgrade, so the old rendering is
	/// restored through the hook the specification leaves open for exactly this: JSON.stringify consults
	/// toJSON before it decides a value has no representation, and quotes the digits this returns.
	/// <para>
	/// Deliberately not a replacer function passed to <c>Serialize</c>. A replacer is invoked for every
	/// node of every state document, on the per-event hot path; this costs nothing until a BigInt is
	/// actually present. It is installed only here, on the projection engines: the scripting engines
	/// never ran the string-writing serializer and have no behaviour to preserve.
	/// </para>
	/// </remarks>
	private void RestoreBigIntSerialization() {
		var bigIntPrototype = _engine.Evaluate("BigInt.prototype").AsObject();
		bigIntPrototype.FastSetProperty(
			"toJSON",
			new PropertyDescriptor(
				new ClrFunction(_engine, "toJSON", static (thisValue, _) => thisValue.ToString()),
				PropertyFlag.NonEnumerable));
	}

	// NonEnumerable is { writable: true, enumerable: false, configurable: true } -- the attribute shape a
	// host-installed global function has always had here, now named rather than spelled as three bools.
	private void AddGlobalFunction(string name, Func<JsValue, JsValue[], JsValue> func, int length) =>
		_engine.Global.FastSetProperty(name, new PropertyDescriptor(new ClrFunction(_engine, name, func, length), PropertyFlag.NonEnumerable));

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
				_state = new JsArray(_engine, new[]
				{
					JsValue.Undefined, JsValue.Undefined
				});

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
				_state = new JsArray(_engine, new[]
				{
					JsValue.Undefined, JsValue.Undefined,
				});

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

	public bool ProcessPartitionCreated(string partition, CheckpointTag createPosition, ResolvedEvent @event,
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
		if (result == JsValue.Null || result == JsValue.Undefined)
			return null;
		return Serialize(result);
	}

	public bool ProcessEvent(string partition, CheckpointTag eventPosition, string category, ResolvedEvent @event,
		out string? newState, out string? newSharedState, out EmittedEventEnvelope[]? emittedEvents) {
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
			newState = arr.TryGetValue(0, out var state)
				? ConvertToStringHandlingNulls(state)
				: "";
			newSharedState = arr.TryGetValue(1, out var sharedState)
				? ConvertToStringHandlingNulls(sharedState)
				: null;
		} else {
			newState = ConvertToStringHandlingNulls(_state);
			newSharedState = null;
		}
	}

	private string? ConvertToStringHandlingNulls(JsValue value) {
		if (value.IsNull() || value.IsUndefined())
			return null;
		return Serialize(value);
	}

	JsValue Emit(JsValue thisValue, JsValue[] parameters) {
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
			var md = parameters.At(3).AsObject();
			var d = new Dictionary<string, string?>();
			foreach (var kvp in md.GetOwnProperties()) {
				if (kvp.Value.Value.Type is Types.Empty or Types.Undefined)
					continue;
				d.Add(kvp.Key.AsString(), Serialize(kvp.Value.Value));
			}

			metadata = new ExtraMetaData(d);
		}
		_emitted.Add(new EmittedEventEnvelope(new EmittedDataEvent(stream, Guid.NewGuid(), eventType, true, data, metadata, _currentPosition, null)));
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

	string? AsString(JsValue? value, bool formatForRaw) {
		return value switch {
			JsBoolean b => b.AsBoolean() ? "true" : "false",
			JsString s => formatForRaw ? $"\"{s.AsString()}\"" : s.AsString(),
			JsNumber n => n.AsNumber().ToString(CultureInfo.InvariantCulture),
			JsNull => null,
			JsUndefined => null, { } v => Serialize(value),
			_ => null
		};
	}

	JsValue LinkTo(JsValue thisValue, JsValue[] parameters) {
		if (parameters.Length != 2 && parameters.Length != 3)
			throw new ArgumentException("wrong number of parameters");
		var stream = EnsureNonNullStringValue(parameters.At(0), "streamId");
		var @event = EnsureNonNullObjectValue(parameters.At(1), "event");

		if (!@event.TryGetValue("sequenceNumber", out var numberValue) | !@event.TryGetValue("streamId", out var sourceValue) || !numberValue.IsNumber()
			 || !sourceValue.IsString()) {
			throw new Exception($"Invalid link to event {numberValue}@{sourceValue}");
		}

		var number = (long)numberValue.AsNumber();
		var source = sourceValue.AsString();
		ExtraMetaData? metadata = null;
		if (parameters.Length == 3) {
			var md = EnsureNonNullObjectValue(parameters.At(2), "metaData");
			var d = new Dictionary<string, string?>();
			foreach (var kvp in md.GetOwnProperties()) {
				d.Add(kvp.Key.AsString(), AsString(kvp.Value.Value, true));
			}
			metadata = new ExtraMetaData(d);
		}

		_emitted.Add(new EmittedEventEnvelope(
			new EmittedDataEvent(stream, Guid.NewGuid(), SystemEventTypes.LinkTo, false, $"{number}@{source}", metadata, _currentPosition, null)));
		return JsValue.Undefined;
	}

	JsValue LinkStreamTo(JsValue thisValue, JsValue[] parameters) {

		var stream = EnsureNonNullStringValue(parameters.At(0), "streamId");
		var linkedStreamId = EnsureNonNullStringValue(parameters.At(1), "linkedStreamId");
		if (parameters.Length == 3) {

		}

		ExtraMetaData? metadata = null;
		if (parameters.Length == 3) {
			var md = parameters.At(4).AsObject();
			var d = new Dictionary<string, string?>();
			foreach (var kvp in md.GetOwnProperties()) {
				d.Add(kvp.Key.AsString(), AsString(kvp.Value.Value, true));
			}
			metadata = new ExtraMetaData(d);
		}
		_emitted.Add(new EmittedEventEnvelope(
			new EmittedDataEvent(stream, Guid.NewGuid(), SystemEventTypes.StreamReference, false, linkedStreamId, metadata, _currentPosition, null)));
		return JsValue.Undefined;
	}

	JsValue CopyTo(JsValue thisValue, JsValue[] parameters) {
		return JsValue.Undefined;
	}

	void Log(string message) {
		_logger.Debug(message, Array.Empty<object>());
	}

	private JsValue Log(JsValue thisValue, JsValue[] parameters) {
		if (parameters.Length == 0)
			return JsValue.Undefined;
		if (parameters.Length == 1) {
			var p0 = parameters.At(0);
			if (p0 != null && p0.IsPrimitive())
				Log(p0.ToString());
			if (p0 is ObjectInstance oi)
				Log(Serialize(oi));
			return JsValue.Undefined;
		}


		if (parameters.Length > 1) {
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
		}
		return JsValue.Undefined;
	}

	class TimeConstraint : Constraint {
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
		// Check() only reads a wall clock; it neither counts its own invocations nor budgets a quantity that
		// can grow unboundedly between two checks, so it is sound for the engine to check it every N
		// statements instead of before every single one. Jint's own TimeConstraint says the same about
		// itself. Without this, a user-derived Constraint lands in the engine's "exact" partition, which
		// costs a virtual Check() per statement and disarms the interpreter's tight-loop lanes for every
		// projection that folds over an array. Only detection latency changes, and it stays bounded (the
		// engine also re-checks whenever control returns from host code).
		public override bool IsAmortizable => true;

		public override void Reset() {
			_start = _sw.Elapsed;
		}

		public override void Check() {
			if (_sw.Elapsed - _start >= _timeout) {
				if (Debugger.IsAttached)
					return;
				var action = _executing ? "execute" : "compile";
				throw new TimeoutException($"Projection script took too long to {action} (took: {_sw.Elapsed - _start:c}, allowed: {_timeout:c}");
			}
		}
	}

	class InterpreterRuntime : ObjectInstance {

		private readonly Dictionary<string, ScriptFunction> _handlers;
		private readonly List<(TransformType, ScriptFunction)> _transforms;
		private readonly List<ScriptFunction> _createdHandlers;
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

		private static readonly Dictionary<string, Action<InterpreterRuntime>> _possibleProperties = new Dictionary<string, Action<InterpreterRuntime>>() {
			["when"] = i => i.AddDslProperty("when", i._whenInstance),
			["partitionBy"] = i => i.AddDslProperty("partitionBy", i._partitionByInstance),
			["outputState"] = i => i.AddDslProperty("outputState", i._outputStateInstance),
			["foreachStream"] = i => i.AddDslProperty("foreachStream", i._foreachStreamInstance),
			["transformBy"] = i => i.AddDslProperty("transformBy", i._transformByInstance),
			["filterBy"] = i => i.AddDslProperty("filterBy", i._filterByInstance),
			["outputTo"] = i => i.AddDslProperty("outputTo", i._outputToInstance),
			["$defines_state_transform"] = i => i.AddDslProperty("$defines_state_transform", i._definesStateTransformInstance),
		};

		private static readonly Dictionary<string, string[]> _availableProperties = new Dictionary<string, string[]>() {
			["fromStream"] = new[] { "when", "partitionBy", "outputState" },
			["fromAll"] = new[] { "when", "partitionBy", "outputState", "foreachStream" },
			["fromStreams"] = new[] { "when", "partitionBy", "outputState" },
			["fromCategory"] = new[] { "when", "partitionBy", "outputState", "foreachStream" },
			["when"] = new[] { "transformBy", "filterBy", "outputState", "outputTo", "$defines_state_transform" },
			["foreachStream"] = new[] { "when" },
			["outputState"] = new[] { "transformBy", "filterBy", "outputTo" },
			["partitionBy"] = new[] { "when" },
			["transformBy"] = new[] { "transformBy", "filterBy", "outputState", "outputTo" },
			["filterBy"] = new[] { "transformBy", "filterBy", "outputState", "outputTo" },
			["outputTo"] = Array.Empty<string>(),
			["execution"] = Array.Empty<string>()
		};

		private static readonly Dictionary<string, Action<SourceDefinitionBuilder, JsValue>> _setters =
			new Dictionary<string, Action<SourceDefinitionBuilder, JsValue>>(StringComparer.OrdinalIgnoreCase) {
				{"$includeLinks", (options, value) => options.SetIncludeLinks(value.IsBoolean()? value.AsBoolean() : throw new Exception("Invalid value"))},
				{"reorderEvents", (options, value) => options.SetReorderEvents(value.IsBoolean()? value.AsBoolean(): throw new Exception("Invalid value"))},
				{"processingLag", (options, value) => options.SetProcessingLag(value.IsNumber() ? (int)value.AsNumber() : throw new Exception("Invalid value"))},
				{"resultStreamName", (options, value) => options.SetResultStreamNameOption(value.IsString() ? value.AsString() : throw new Exception("Invalid value"))},
				{"biState", (options, value) => options.SetIsBiState(value.IsBoolean()? value.AsBoolean() : throw new Exception("Invalid value"))},
			};

		private readonly List<string> _definitionFunctions;

		public InterpreterRuntime(
			Engine engine,
			SourceDefinitionBuilder builder,
			JsFunctionCallMeasurer jsFunctionCaller) : base(engine) {

			_definitionBuilder = builder;
			_jsFunctionCaller = jsFunctionCaller;
			_handlers = new Dictionary<string, ScriptFunction>(StringComparer.Ordinal);
			_createdHandlers = new List<ScriptFunction>();
			_transforms = new List<(TransformType, ScriptFunction)>();
			_parser = new JsonParser(engine);
			_definitionFunctions = new List<string>();
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

		private void AddDslProperty(string name, JsValue value) =>
			FastSetProperty(name, new PropertyDescriptor(value, PropertyFlag.NonEnumerable));

		private void AddDefinitionFunction(string name, Func<JsValue, JsValue[], JsValue> func, int length) {
			_definitionFunctions.Add(name);
			_engine.Global.FastSetProperty(name, new PropertyDescriptor(new ClrFunction(_engine, name, func, length), PropertyFlag.NonEnumerable));
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
			if (parameters.Length == 0)
				return this;
			if (parameters.Length == 1 && parameters.At(0).IsArray()) {
				foreach (var cat in parameters.At(0).AsArray()) {
					if (cat is not JsString s) {
						throw new ArgumentException("categories");
					}
					_definitionBuilder.FromStream($"$ce-{s.AsString()}");
				}
			} else if (parameters.Length > 1) {
				foreach (var cat in parameters) {
					if (cat is not JsString s) {
						throw new ArgumentException("categories");
					}
					_definitionBuilder.FromStream($"$ce-{s.AsString()}");
				}
			} else {
				var p0 = parameters.At(0);
				if (p0 is not JsString s)
					throw new ArgumentException("category");
				_definitionBuilder.FromCategory(s.AsString());
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
			if (parameters.At(0) is ScriptFunction fi) {
				_definitionBuilder.SetDefinesStateTransform();
				_definitionBuilder.SetOutputState();
				_transforms.Add((TransformType.Filter, fi));
				RestrictProperties("filterBy");
				return this;
			}

			throw new ArgumentException("expected function");
		}

		private JsValue TransformBy(JsValue thisValue, JsValue[] parameters) {
			if (parameters.At(0) is ScriptFunction fi) {
				_definitionBuilder.SetDefinesStateTransform();
				_definitionBuilder.SetOutputState();
				_transforms.Add((TransformType.Transform, fi));
				RestrictProperties("transformBy");
				return this;
			}

			throw new ArgumentException("expected function");
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
				newState = _jsFunctionCaller.Call(eventEnvelope.EventType, handler, state, eventEnvelope.Value);
			} else if (_any != null) {
				newState = _jsFunctionCaller.Call("$any", _any, state, eventEnvelope.Value);
			} else {
				newState = eventEnvelope.IsJson ? eventEnvelope.Body : eventEnvelope.BodyRaw;
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

		JsValue FromAll(JsValue _, JsValue[] __) {
			_definitionBuilder.FromAll();
			RestrictProperties("fromAll");
			return this;
		}

		JsValue FromStreams(JsValue _, JsValue[] parameters) {
			IEnumerator<JsValue>? streams = null;
			try {
				streams = parameters.At(0).IsArray() ? parameters.At(0).AsArray().GetEnumerator() : parameters.AsEnumerable().GetEnumerator();
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


		JsValue SetOptions(JsValue thisValue, JsValue[] parameters) {
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

		public JsValue GetPartition(EventEnvelope envelope) {
			if (_partitionFunction != null)
				return _jsFunctionCaller.Call("partitionBy", _partitionFunction, envelope.Value);
			return Null;
		}

		public void HandleCreated(JsValue state, EventEnvelope envelope) {
			for (int i = 0; i < _createdHandlers.Count; i++) {
				_jsFunctionCaller.Call("$created", _createdHandlers[i], state, envelope.Value);
			}
		}

		enum TransformType {
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

	EventEnvelope CreateEnvelope(string partition, ResolvedEvent @event, string category) =>
		new(_engine, _parser, partition, @event, category);
	/// <summary>
	/// The per-event object handed to a projection handler, plus the host-side state behind it.
	/// <para>
	/// The JavaScript value is a plain <see cref="JsObject"/> built from a shared
	/// <see cref="JsObjectLayout"/> rather than a custom <c>ObjectInstance</c> subclass. That matters
	/// because a host subclass carries none of the engine's storage flags, so it reaches no member-read
	/// inline cache at all: every <c>e.streamId</c> was a virtual call into a property dictionary. Every
	/// envelope built from this layout in one engine shares one hidden class, so the handler's reads stay
	/// monomorphic across events even though the object itself is new each time.
	/// </para>
	/// <para>
	/// The members that must parse a JSON document are declared as lazy slots, so they exist as ordinary
	/// properties -- they answer <c>in</c>, <c>hasOwnProperty</c> and <c>Object.keys</c> -- but the
	/// document is only parsed by a read that actually observes the value. Before 4.15.1 a lazy member and
	/// a shaped object were mutually exclusive, which is why this used to be a subclass.
	/// </para>
	/// </summary>
	internal sealed class EventEnvelope {
		// Slot order is the own-key order of every envelope. It reproduces the order the previous
		// implementation ended up with: the eleven eager members in the order they were assigned, then the
		// parsed members in the order they were materialized.
		//
		// There are two variants because the previous implementation's key set was not fixed. Reconstructing
		// its conditions exactly: metadataRaw and linkMetadataRaw were assigned unconditionally, and a null
		// string converts to JS null rather than undefined, so EnsureMetadata and EnsureLinkMetadata always
		// succeeded -- metadata and linkMetadata were always present, with the value null when their document
		// was absent. EnsureBody additionally required IsJson, so body and data -- which always appeared
		// together, sharing one descriptor -- were present exactly when the event was JSON. IsJson is
		// therefore the only condition, and two layouts cover it.
		//
		// Each variant is still one shared hidden class across every envelope built from it, so the inline
		// cache win survives. A projection handling both JSON and non-JSON events sees two shapes at its read
		// sites and goes polymorphic there, which is the honest cost of the key sets genuinely differing --
		// and still far cheaper than the host-subclass path, which reached no cache at all.
		//
		// The factories are static and read everything item-specific out of the per-object state, because a
		// layout is process-shared by design and a captured engine would leak one engine's state into
		// another's objects. "body" and "data" are two names for one document, so both factories go through
		// the same memo and a projection reading each of them parses once.
		private static readonly JsObjectLayout JsonLayout = BuildLayout(withBody: true);
		private static readonly JsObjectLayout NonJsonLayout = BuildLayout(withBody: false);

		private static JsObjectLayout BuildLayout(bool withBody) {
			var builder = JsObjectLayout.CreateBuilder()
				.Add("partition")
				.Add("created")
				.Add("bodyRaw")
				.Add("metadataRaw")
				.Add("streamId")
				.Add("eventId")
				.Add("eventType")
				.Add("linkMetadataRaw")
				.Add("isJson")
				.Add("category")
				.Add("sequenceNumber");

			if (withBody) {
				builder
					.AddLazy("body", static (_, state) => ((EventEnvelope)state!).Body)
					.AddLazy("data", static (_, state) => ((EventEnvelope)state!).Body);
			}

			return builder
				.AddLazy("metadata", static (_, state) => ((EventEnvelope)state!).Metadata)
				.AddLazy("linkMetadata", static (_, state) => ((EventEnvelope)state!).LinkMetadata)
				.Build();
		}

		private readonly JsonParser _parser;
		private readonly string? _bodyRaw;
		private readonly string? _metadataRaw;
		private readonly string? _linkMetadataRaw;

		private JsValue? _body;
		private JsValue? _metadata;
		private JsValue? _linkMetadata;

		/// <summary>The value passed to the projection handler.</summary>
		public JsObject Value { get; }

		/// <summary>
		/// The event type, read straight off the CLR record. It used to be read back out of the JavaScript
		/// property table purely to key a CLR dictionary of handlers.
		/// </summary>
		public string EventType { get; }

		public bool IsJson { get; }

		public string? BodyRaw => _bodyRaw;

		public EventEnvelope(Engine engine, JsonParser parser, string partition, ResolvedEvent @event, string category) {
			_parser = parser;
			_bodyRaw = @event.Data;
			_metadataRaw = @event.Metadata;
			_linkMetadataRaw = @event.PositionMetadata;
			IsJson = @event.IsJson;

			// ResolvedEvent.EventType is null whenever the resolved event carries no event record, and its
			// declaring project has nullable reference types disabled, so the `string` annotation says
			// nothing. The removed CLR getter read the value back out of the property table through
			// AsString(...) ?? "", so "" is the dispatch key an untyped event has always produced; keeping
			// the coalesce preserves that rather than deciding something new. The JS-visible property is
			// fed the raw value below, so it stays null there exactly as it was.
			EventType = @event.EventType ?? "";

			// avoid new JsDate(engine, value) because if the user stores it in their state it will be a date
			// until the state is serialized and back, after which it will be a string, which would be a gotcha
			var created = @event.Timestamp.ToString("o");
			var eventId = @event.EventId.ToString("D");

			// The value lists are spelled out per variant rather than assembled from a shared array: the
			// trailing nulls are the lazy slots, which Create requires to be null, and building a common
			// prefix first would put an extra array allocation on the per-event path.
			Value = IsJson
				? JsObject.Create(engine, JsonLayout, [
					partition, created, _bodyRaw, _metadataRaw, @event.EventStreamId, eventId,
					@event.EventType, _linkMetadataRaw, IsJson, category, @event.EventSequenceNumber,
					null, null, null, null,
				], this)
				: JsObject.Create(engine, NonJsonLayout, [
					partition, created, _bodyRaw, _metadataRaw, @event.EventStreamId, eventId,
					@event.EventType, _linkMetadataRaw, IsJson, category, @event.EventSequenceNumber,
					null, null,
				], this);
		}

		// Only reached through the JSON layout, whose slots exist exactly when the event is JSON. A null raw
		// document parses to null -- present with the value null, which is what the previous implementation
		// produced too, and distinct from the property being absent.
		public JsValue Body => _body ??= Parse(_bodyRaw);

		private JsValue Metadata => _metadata ??= Parse(_metadataRaw);

		private JsValue LinkMetadata => _linkMetadata ??= Parse(_linkMetadataRaw);

		private JsValue Parse(string? raw) => raw is null ? JsValue.Null : _parser.Parse(raw);

	}

	// Jint's JsonSerializer is JSON.stringify's own implementation, reachable directly. The
	// hand-written serializer it replaces produced UTF-8, which this method then transcoded back to a
	// string because every consumer of the result -- PartitionState, the emitted event body -- wants
	// one. Going through the string-returning overload removes that encode and decode per event, drops
	// the 1 MB ArrayBufferWriter each handler held, and picks up the shape-mode fast arm that walks a
	// JsonParser-produced object by its hidden class instead of allocating a JsString per key.
	public string Serialize(JsValue value) => _jsSerializer.Serialize(_serializer, value);

}
