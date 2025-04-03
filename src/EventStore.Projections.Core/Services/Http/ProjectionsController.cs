// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using EventStore.Core.Services;
using EventStore.Core.Services.Transport.Http;
using EventStore.Core.Services.Transport.Http.Controllers;
using EventStore.Core.Util;
using EventStore.Plugins.Authorization;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Messages.EventReaders.Feeds;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;
using EventStore.Transport.Http.EntityManagement;
using Newtonsoft.Json.Linq;
using ILogger = Serilog.ILogger;

namespace EventStore.Projections.Core.Services.Http;

public class ProjectionsController : CommunicationController {
	private static readonly ILogger Log = Serilog.Log.ForContext<ProjectionsController>();

	private static readonly ICodec[] SupportedCodecs = {Codec.Json};

	private readonly MiniWeb _clusterNodeJs;
	private readonly MiniWeb _miniWebPrelude;
	private readonly IHttpForwarder _httpForwarder;
	private readonly IPublisher _networkSendQueue;

	public ProjectionsController(IHttpForwarder httpForwarder, IPublisher publisher, IPublisher networkSendQueue)
		: base(publisher) {
		_httpForwarder = httpForwarder;

		_clusterNodeJs = new MiniWeb("/web/es/js/projections", Locations.ProjectionsDirectory);

		_networkSendQueue = networkSendQueue;
		_miniWebPrelude = new MiniWeb("/web/es/js/projections/v8/Prelude", Locations.PreludeDirectory);
	}

	protected override void SubscribeCore(IHttpService service) {
		_clusterNodeJs.RegisterControllerActions(service);

		_miniWebPrelude.RegisterControllerActions(service);

		HttpHelpers.RegisterRedirectAction(service, "/web/projections", "/web/projections.htm");

		Register(service, "/projections",
			HttpMethod.Get, OnProjections, Codec.NoCodecs, new ICodec[] {Codec.ManualEncoding}, new Operation(Operations.Projections.List));
		Register(service, "/projections/restart",
			HttpMethod.Post, OnProjectionsRestart, new ICodec[] { Codec.ManualEncoding}, SupportedCodecs, new Operation(Operations.Projections.Restart));
		Register(service, "/projections/any",
			HttpMethod.Get, OnProjectionsGetAny, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.List));
		Register(service, "/projections/all-non-transient",
			HttpMethod.Get, OnProjectionsGetAllNonTransient, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.List));
		Register(service, "/projections/transient",
			HttpMethod.Get, OnProjectionsGetTransient, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.List));
		Register(service, "/projections/onetime",
			HttpMethod.Get, OnProjectionsGetOneTime, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.List));
		Register(service, "/projections/continuous",
			HttpMethod.Get, OnProjectionsGetContinuous, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.List));
		Register(service, "/projections/transient?name={name}&type={type}&enabled={enabled}",
			HttpMethod.Post, OnProjectionsPostTransient, new ICodec[] {Codec.ManualEncoding}, SupportedCodecs, new Operation(Operations.Projections.Create).WithParameter(Operations.Projections.Parameters.Query));
		Register(service,
			"/projections/onetime?name={name}&type={type}&enabled={enabled}&checkpoints={checkpoints}&emit={emit}&trackemittedstreams={trackemittedstreams}",
			HttpMethod.Post, OnProjectionsPostOneTime, new ICodec[] {Codec.ManualEncoding}, SupportedCodecs, new Operation(Operations.Projections.Create).WithParameter(Operations.Projections.Parameters.OneTime));
		Register(service,
			"/projections/continuous?name={name}&type={type}&enabled={enabled}&emit={emit}&trackemittedstreams={trackemittedstreams}",
			HttpMethod.Post, OnProjectionsPostContinuous, new ICodec[] {Codec.ManualEncoding}, SupportedCodecs, new Operation(Operations.Projections.Create).WithParameter(Operations.Projections.Parameters.Continuous));
		Register(service, "/projection/{name}/query?config={config}",
			HttpMethod.Get, OnProjectionQueryGet, Codec.NoCodecs, new ICodec[] {Codec.ManualEncoding}, new Operation(Operations.Projections.Read));
		Register(service, "/projection/{name}/query?type={type}&emit={emit}",
			HttpMethod.Put, OnProjectionQueryPut, new ICodec[] {Codec.ManualEncoding}, SupportedCodecs, new Operation(Operations.Projections.Update)); /* source of transient projections can be set by a normal user. Authorization checks are done internally for non-transient projections. */
		Register(service, "/projection/{name}",
			HttpMethod.Get, OnProjectionStatusGet, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.Status));
		Register(service,
			"/projection/{name}?deleteStateStream={deleteStateStream}&deleteCheckpointStream={deleteCheckpointStream}&deleteEmittedStreams={deleteEmittedStreams}",
			HttpMethod.Delete, OnProjectionDelete, Codec.NoCodecs, SupportedCodecs,new Operation(Operations.Projections.Delete));
		Register(service, "/projection/{name}/statistics",
			HttpMethod.Get, OnProjectionStatisticsGet, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.Statistics));
		Register(service, "/projections/read-events",
			HttpMethod.Post, OnProjectionsReadEvents, SupportedCodecs, SupportedCodecs, new Operation(Operations.Projections.DebugProjection));
		Register(service, "/projection/{name}/state?partition={partition}",
			HttpMethod.Get, OnProjectionStateGet, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.State));
		Register(service, "/projection/{name}/result?partition={partition}",
			HttpMethod.Get, OnProjectionResultGet, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.Result));
		Register(service, "/projection/{name}/command/disable?enableRunAs={enableRunAs}",
			HttpMethod.Post, OnProjectionCommandDisable, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.Disable)); /* transient projections can be stopped by a normal user. Authorization checks are done internally for non-transient projections.*/
		Register(service, "/projection/{name}/command/enable?enableRunAs={enableRunAs}",
			HttpMethod.Post, OnProjectionCommandEnable, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.Enable)); /* transient projections can be enabled by a normal user. Authorization checks are done internally for non-transient projections.*/
		Register(service, "/projection/{name}/command/reset?enableRunAs={enableRunAs}",
			HttpMethod.Post, OnProjectionCommandReset, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.Reset)); /* transient projections can be reset by a normal user (when debugging). Authorization checks are done internally for non-transient projections.*/
		Register(service, "/projection/{name}/command/abort?enableRunAs={enableRunAs}",
			HttpMethod.Post, OnProjectionCommandAbort, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.Abort)); /* transient projections can be aborted by a normal user. Authorization checks are done internally for non-transient projections.*/
		Register(service, "/projection/{name}/config",
			HttpMethod.Get, OnProjectionConfigGet, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.ReadConfiguration));
		Register(service, "/projection/{name}/config",
			HttpMethod.Put, OnProjectionConfigPut, SupportedCodecs, SupportedCodecs, new Operation(Operations.Projections.UpdateConfiguration));
	}

	private void OnProjections(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		http.ReplyTextContent(
			"Moved", 302, "Found", ContentType.PlainText,
			new[] {
				new KeyValuePair<string, string>(
					"Location", new Uri(match.BaseUri, "/web/projections.htm").AbsoluteUri)
			}, x => Log.Debug(x, "Reply Text Content Failed."));
	}

	private void OnProjectionsRestart(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionSubsystemMessage.SubsystemRestarting>(_networkSendQueue, http,
			(e, message) => e.To("Restarting"),
			(e, message) => {
				switch (message) {
					case ProjectionSubsystemMessage.SubsystemRestarting _:
						return Configure.Ok(e.ContentType);
					default:
						return Configure.InternalServerError();
				}
			}, CreateErrorEnvelope(http)
		);

		Publish(new ProjectionSubsystemMessage.RestartSubsystem(envelope));
	}

	private void OnProjectionsGetAny(HttpEntityManager http, UriTemplateMatch match) {
		ProjectionsGet(http, match, null);
	}

	private void OnProjectionsGetAllNonTransient(HttpEntityManager http, UriTemplateMatch match) {
		ProjectionsGet(http, match, ProjectionMode.AllNonTransient);
	}

	private void OnProjectionsGetTransient(HttpEntityManager http, UriTemplateMatch match) {
		ProjectionsGet(http, match, ProjectionMode.Transient);
	}

	private void OnProjectionsGetOneTime(HttpEntityManager http, UriTemplateMatch match) {
		ProjectionsGet(http, match, ProjectionMode.OneTime);
	}

	private void OnProjectionsGetContinuous(HttpEntityManager http, UriTemplateMatch match) {
		ProjectionsGet(http, match, ProjectionMode.Continuous);
	}

	private void OnProjectionsPostTransient(HttpEntityManager http, UriTemplateMatch match) {
		ProjectionsPost(http, match, ProjectionMode.Transient, match.BoundVariables["name"]);
	}

	private void OnProjectionsPostOneTime(HttpEntityManager http, UriTemplateMatch match) {
		ProjectionsPost(http, match, ProjectionMode.OneTime, match.BoundVariables["name"]);
	}

	private void OnProjectionsPostContinuous(HttpEntityManager http, UriTemplateMatch match) {
		ProjectionsPost(http, match, ProjectionMode.Continuous, match.BoundVariables["name"]);
	}

	private void OnProjectionQueryGet(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		SendToHttpEnvelope<ProjectionManagementMessage.ProjectionQuery> envelope;
		var withConfig = IsOn(match, "config", false);
		if (withConfig)
			envelope = new SendToHttpEnvelope<ProjectionManagementMessage.ProjectionQuery>(
				_networkSendQueue, http, QueryConfigFormatter, QueryConfigConfigurator, CreateErrorEnvelope(http));
		else
			envelope = new SendToHttpEnvelope<ProjectionManagementMessage.ProjectionQuery>(
				_networkSendQueue, http, QueryFormatter, QueryConfigurator, CreateErrorEnvelope(http));
		Publish(new ProjectionManagementMessage.Command.GetQuery(envelope, match.BoundVariables["name"],
			GetRunAs(http, match)));
	}

	private void OnProjectionQueryPut(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.Updated>(
			_networkSendQueue, http, DefaultFormatter, OkResponseConfigurator, CreateErrorEnvelope(http));
		var emitEnabled = IsOn(match, "emit", null);
		http.ReadTextRequestAsync(
			(o, s) =>
				Publish(
			new ProjectionManagementMessage.Command.UpdateQuery(
				envelope, match.BoundVariables["name"], GetRunAs(http, match),
				s, emitEnabled: emitEnabled)), Console.WriteLine);
	}

	private void OnProjectionConfigGet(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.ProjectionConfig>(
			_networkSendQueue, http, ProjectionConfigFormatter, ProjectionConfigConfigurator, CreateErrorEnvelope(http));
		Publish(
			new ProjectionManagementMessage.Command.GetConfig(
				envelope, match.BoundVariables["name"], GetRunAs(http, match)));
	}

	private void OnProjectionConfigPut(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.Updated>(
			_networkSendQueue, http, DefaultFormatter, OkResponseConfigurator, CreateErrorEnvelope(http));
		http.ReadTextRequestAsync(
			(o, s) => {
				var config = http.RequestCodec.From<ProjectionConfigData>(s);
				if (config == null) {
					SendBadRequest(o, "Failed to parse the projection config");
					return;
				}

				if (config.ProjectionExecutionTimeout is not null && config.ProjectionExecutionTimeout <= 0) {
					SendBadRequest(o, $"projectionExecutionTimeout should be positive. Found : {config.ProjectionExecutionTimeout}");
					return;
				}
				var message = new ProjectionManagementMessage.Command.UpdateConfig(
					envelope, match.BoundVariables["name"], config.EmitEnabled, config.TrackEmittedStreams,
					config.CheckpointAfterMs, config.CheckpointHandledThreshold,
					config.CheckpointUnhandledBytesThreshold, config.PendingEventsThreshold,
					config.MaxWriteBatchLength, config.MaxAllowedWritesInFlight, GetRunAs(http, match), config.ProjectionExecutionTimeout);
				Publish(message);
			}, ex => Log.Debug("Failed to update projection configuration. Error: {e}", ex));
	}

	private void OnProjectionCommandDisable(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.Updated>(
			_networkSendQueue, http, DefaultFormatter, OkResponseConfigurator, CreateErrorEnvelope(http));
		Publish(new ProjectionManagementMessage.Command.Disable(envelope, match.BoundVariables["name"],
			GetRunAs(http, match)));
	}

	private void OnProjectionCommandEnable(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.Updated>(
			_networkSendQueue, http, DefaultFormatter, OkResponseConfigurator, CreateErrorEnvelope(http));
		var name = match.BoundVariables["name"];
		Publish(new ProjectionManagementMessage.Command.Enable(envelope, name, GetRunAs(http, match)));
	}

	private void OnProjectionCommandReset(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.Updated>(
			_networkSendQueue, http, DefaultFormatter, OkResponseConfigurator, CreateErrorEnvelope(http));
		Publish(new ProjectionManagementMessage.Command.Reset(envelope, match.BoundVariables["name"],
			GetRunAs(http, match)));
	}

	private void OnProjectionCommandAbort(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.Updated>(
			_networkSendQueue, http, DefaultFormatter, OkResponseConfigurator, CreateErrorEnvelope(http));
		Publish(new ProjectionManagementMessage.Command.Abort(envelope, match.BoundVariables["name"],
			GetRunAs(http, match)));
	}

	private void OnProjectionStatusGet(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope =
			new SendToHttpWithConversionEnvelope
				<ProjectionManagementMessage.Statistics, ProjectionStatisticsHttpFormatted>(
					_networkSendQueue, http, DefaultFormatter, OkNoCacheResponseConfigurator,
					status => new ProjectionStatisticsHttpFormatted(status.Projections[0], s => MakeUrl(http, s)),
					CreateErrorEnvelope(http));
		Publish(new ProjectionManagementMessage.Command.GetStatistics(envelope, null, match.BoundVariables["name"],
			true));
	}

	private void OnProjectionDelete(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.Updated>(
			_networkSendQueue, http, DefaultFormatter, OkResponseConfigurator, CreateErrorEnvelope(http));
		Publish(
			new ProjectionManagementMessage.Command.Delete(
				envelope, match.BoundVariables["name"], GetRunAs(http, match),
				IsOn(match, "deleteCheckpointStream", false),
				IsOn(match, "deleteStateStream", false),
				IsOn(match, "deleteEmittedStreams", false)));
	}

	private void OnProjectionStatisticsGet(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope =
			new SendToHttpWithConversionEnvelope
				<ProjectionManagementMessage.Statistics, ProjectionsStatisticsHttpFormatted>(
					_networkSendQueue, http, DefaultFormatter, OkNoCacheResponseConfigurator,
					status => new ProjectionsStatisticsHttpFormatted(status, s => MakeUrl(http, s)),
					CreateErrorEnvelope(http));
		Publish(new ProjectionManagementMessage.Command.GetStatistics(envelope, null, match.BoundVariables["name"],
			true));
	}

	private void OnProjectionStateGet(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.ProjectionState>(
			_networkSendQueue, http, StateFormatter, StateConfigurator, CreateErrorEnvelope(http));
		Publish(
			new ProjectionManagementMessage.Command.GetState(
				envelope, match.BoundVariables["name"], match.BoundVariables["partition"] ?? ""));
	}

	private void OnProjectionResultGet(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.ProjectionResult>(
			_networkSendQueue, http, ResultFormatter, ResultConfigurator, CreateErrorEnvelope(http));
		Publish(
			new ProjectionManagementMessage.Command.GetResult(
				envelope, match.BoundVariables["name"], match.BoundVariables["partition"] ?? ""));
	}

	public class ReadEventsBody {
		public QuerySourcesDefinition Query { get; set; }
		public JObject Position { get; set; }
		public int? MaxEvents { get; set; }
	}

	private void OnProjectionsReadEvents(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<FeedReaderMessage.FeedPage>(
			_networkSendQueue, http, FeedPageFormatter, FeedPageConfigurator, CreateErrorEnvelope(http));

		http.ReadTextRequestAsync(
			(o, body) => {
				var bodyParsed = body.ParseJson<ReadEventsBody>();
				var fromPosition = CheckpointTag.FromJson(
					new JTokenReader(bodyParsed.Position), new ProjectionVersion(0, 0, 0));


				Publish(
					new FeedReaderMessage.ReadPage(
						Guid.NewGuid(),
						envelope,
						http.User,
						bodyParsed.Query,
						fromPosition.Tag,
						bodyParsed.MaxEvents ?? 10));
			},
			x => Log.Debug(x, "Read Request Body Failed."));
	}

	private void ProjectionsGet(HttpEntityManager http, UriTemplateMatch match, ProjectionMode? mode) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope =
			new SendToHttpWithConversionEnvelope<ProjectionManagementMessage.Statistics,
				ProjectionsStatisticsHttpFormatted>(
				_networkSendQueue, http, DefaultFormatter, OkNoCacheResponseConfigurator,
				status => new ProjectionsStatisticsHttpFormatted(status, s => MakeUrl(http, s)),
				CreateErrorEnvelope(http));
		Publish(new ProjectionManagementMessage.Command.GetStatistics(envelope, mode, null, true));
	}

	private void ProjectionsPost(HttpEntityManager http, UriTemplateMatch match, ProjectionMode mode, string name) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.Updated>(
			_networkSendQueue, http, DefaultFormatter, (codec, message) => {
				var localPath = string.Format("/projection/{0}", message.Name);
				var url = MakeUrl(http, localPath);
				return new ResponseConfiguration(
					201, "Created", codec.ContentType, codec.Encoding,
					new KeyValuePair<string, string>("Location", url));
			}, CreateErrorEnvelope(http));
		http.ReadTextRequestAsync(
			(o, s) => {
				ProjectionManagementMessage.Command.Post postMessage;
				string handlerType = match.BoundVariables["type"] ?? "JS";
				bool emitEnabled = IsOn(match, "emit", false);
				bool checkpointsEnabled = mode >= ProjectionMode.Continuous || IsOn(match, "checkpoints", false);
				bool enabled = IsOn(match, "enabled", def: true);
				bool trackEmittedStreams = IsOn(match, "trackemittedstreams", def: false);
				if (!emitEnabled) {
					trackEmittedStreams = false;
				}

				var runAs = GetRunAs(http, match);
				if (mode <= ProjectionMode.OneTime && string.IsNullOrEmpty(name))
					postMessage = new ProjectionManagementMessage.Command.Post(
						envelope, mode, Guid.NewGuid().ToString("D"), runAs, handlerType, s, enabled: enabled,
						checkpointsEnabled: checkpointsEnabled, emitEnabled: emitEnabled,
						trackEmittedStreams: trackEmittedStreams, enableRunAs: true);
				else
					postMessage = new ProjectionManagementMessage.Command.Post(
						envelope, mode, name, runAs, handlerType, s, enabled: enabled,
						checkpointsEnabled: checkpointsEnabled, emitEnabled: emitEnabled,
						trackEmittedStreams: trackEmittedStreams, enableRunAs: true);
				Publish(postMessage);
			}, x => Log.Debug(x, "Reply Text Body Failed."));
	}

	private ResponseConfiguration
		StateConfigurator(ICodec codec, ProjectionManagementMessage.ProjectionState state) {
		if (state.Exception != null)
			return Configure.InternalServerError();
		else
			return state.Position != null
				? Configure.Ok(ContentType.Json, Helper.UTF8NoBom, null, null, false,
					new KeyValuePair<string, string>(SystemHeaders.ProjectionPosition, state.Position.ToJsonString()),
					new KeyValuePair<string, string>(SystemHeaders.LegacyProjectionPosition, state.Position.ToJsonString()))
				: Configure.Ok(ContentType.Json, Helper.UTF8NoBom, null, null, false);
	}

	private ResponseConfiguration ResultConfigurator(ICodec codec,
		ProjectionManagementMessage.ProjectionResult state) {
		if (state.Exception != null)
			return Configure.InternalServerError();
		else
			return state.Position != null
				? Configure.Ok(ContentType.Json, Helper.UTF8NoBom, null, null, false,
					new KeyValuePair<string, string>(SystemHeaders.ProjectionPosition, state.Position.ToJsonString()),
					new KeyValuePair<string, string>(SystemHeaders.LegacyProjectionPosition, state.Position.ToJsonString()))
				: Configure.Ok(ContentType.Json, Helper.UTF8NoBom, null, null, false);
	}

	private ResponseConfiguration FeedPageConfigurator(ICodec codec, FeedReaderMessage.FeedPage page) {
		if (page.Error == FeedReaderMessage.FeedPage.ErrorStatus.NotAuthorized)
			return Configure.Unauthorized();
		return Configure.Ok(ContentType.Json, Helper.UTF8NoBom, null, null, false);
	}

	private string StateFormatter(ICodec codec, ProjectionManagementMessage.ProjectionState state) {
		if (state.Exception != null)
			return state.Exception.ToString();
		else
			return state.State;
	}

	private string ResultFormatter(ICodec codec, ProjectionManagementMessage.ProjectionResult state) {
		if (state.Exception != null)
			return state.Exception.ToString();
		else
			return state.Result;
	}

	private string FeedPageFormatter(ICodec codec, FeedReaderMessage.FeedPage page) {
		if (page.Error != FeedReaderMessage.FeedPage.ErrorStatus.Success)
			return null;

		return new {
			CorrelationId = page.CorrelationId,
			ReaderPosition = page.LastReaderPosition.ToJsonRaw(),
			Events = (from e in page.Events
				let resolvedEvent = e.ResolvedEvent
				let isJson = resolvedEvent.IsJson
				let data = isJson
					? EatException(() => (object)(resolvedEvent.Data.ParseJson<JObject>()), resolvedEvent.Data)
					: resolvedEvent.Data
				let metadata = isJson
					? EatException(() => (object)(resolvedEvent.Metadata.ParseJson<JObject>()),
						resolvedEvent.Metadata)
					: resolvedEvent.Metadata
				let linkMetadata = isJson
					? EatException(() => (object)(resolvedEvent.PositionMetadata.ParseJson<JObject>()),
						resolvedEvent.PositionMetadata)
					: resolvedEvent.PositionMetadata
				select new {
					EventStreamId = resolvedEvent.EventStreamId,
					EventNumber = resolvedEvent.EventSequenceNumber,
					EventType = resolvedEvent.EventType,
					Data = data,
					Metadata = metadata,
					LinkMetadata = linkMetadata,
					IsJson = isJson,
					ReaderPosition = e.ReaderPosition.ToJsonRaw(),
				}).ToArray()
		}.ToJson();
	}

	private ResponseConfiguration
		QueryConfigurator(ICodec codec, ProjectionManagementMessage.ProjectionQuery state) {
		return Configure.Ok("application/javascript", Helper.UTF8NoBom, null, null, false);
	}

	private string QueryFormatter(ICodec codec, ProjectionManagementMessage.ProjectionQuery state) {
		return state.Query;
	}

	private string QueryConfigFormatter(ICodec codec, ProjectionManagementMessage.ProjectionQuery state) {
		return state.ToJson();
	}

	private ResponseConfiguration QueryConfigConfigurator(ICodec codec,
		ProjectionManagementMessage.ProjectionQuery state) {
		return Configure.Ok(ContentType.Json, Helper.UTF8NoBom, null, null, false);
	}

	private string ProjectionConfigFormatter(ICodec codec, ProjectionManagementMessage.ProjectionConfig config) {
		return config.ToJson();
	}

	private ResponseConfiguration ProjectionConfigConfigurator(ICodec codec,
		ProjectionManagementMessage.ProjectionConfig state) {
		return Configure.Ok(ContentType.Json, Helper.UTF8NoBom, null, null, false);
	}

	private ResponseConfiguration OkResponseConfigurator<T>(ICodec codec, T message) {
		return new ResponseConfiguration(200, "OK", codec.ContentType, Helper.UTF8NoBom);
	}

	private ResponseConfiguration OkNoCacheResponseConfigurator<T>(ICodec codec, T message) {
		return Configure.Ok(codec.ContentType, codec.Encoding, null, null, false);
	}

	private IEnvelope CreateErrorEnvelope(HttpEntityManager http) {
		return new SendToHttpEnvelope<ProjectionManagementMessage.NotFound>(
			_networkSendQueue,
			http,
			NotFoundFormatter,
			NotFoundConfigurator,
			new SendToHttpEnvelope<ProjectionManagementMessage.NotAuthorized>(
				_networkSendQueue,
				http,
				NotAuthorizedFormatter,
				NotAuthorizedConfigurator,
				new SendToHttpEnvelope<ProjectionManagementMessage.Conflict>(
					_networkSendQueue,
					http,
					ConflictFormatter,
					ConflictConfigurator,
					new SendToHttpEnvelope<ProjectionManagementMessage.OperationFailed>(
						_networkSendQueue,
						http,
						OperationFailedFormatter,
						OperationFailedConfigurator,
						new SendToHttpEnvelope<ProjectionSubsystemMessage.InvalidSubsystemRestart>(
							_networkSendQueue,
							http,
							InvalidSubsystemRestartFormatter,
							InvalidSubsystemRestartConfigurator,
							null)))));
	}

	private ResponseConfiguration NotFoundConfigurator(ICodec codec, ProjectionManagementMessage.NotFound message) {
		return new ResponseConfiguration(404, "Not Found", ContentType.PlainText, Helper.UTF8NoBom);
	}

	private string NotFoundFormatter(ICodec codec, ProjectionManagementMessage.NotFound message) {
		return message.Reason;
	}

	private ResponseConfiguration NotAuthorizedConfigurator(
		ICodec codec, ProjectionManagementMessage.NotAuthorized message) {
		return new ResponseConfiguration(401, "Not Authorized", ContentType.PlainText, Encoding.UTF8);
	}

	private string NotAuthorizedFormatter(ICodec codec, ProjectionManagementMessage.NotAuthorized message) {
		return message.Reason;
	}

	private ResponseConfiguration OperationFailedConfigurator(
		ICodec codec, ProjectionManagementMessage.OperationFailed message) {
		return new ResponseConfiguration(500, "Failed", ContentType.PlainText, Helper.UTF8NoBom);
	}

	private string OperationFailedFormatter(ICodec codec, ProjectionManagementMessage.OperationFailed message) {
		return message.Reason;
	}

	private ResponseConfiguration ConflictConfigurator(
		ICodec codec, ProjectionManagementMessage.OperationFailed message) {
		return new ResponseConfiguration(409, "Conflict", ContentType.PlainText, Helper.UTF8NoBom);
	}

	private string ConflictFormatter(ICodec codec, ProjectionManagementMessage.OperationFailed message) {
		return message.Reason;
	}

	private static string DefaultFormatter<T>(ICodec codec, T message) {
		return codec.To(message);
	}

	private ResponseConfiguration InvalidSubsystemRestartConfigurator(ICodec codec, ProjectionSubsystemMessage.InvalidSubsystemRestart message) {
		return new ResponseConfiguration(HttpStatusCode.BadRequest, "Bad Request", ContentType.PlainText,
			Helper.UTF8NoBom);
	}

	private string InvalidSubsystemRestartFormatter(ICodec codec, ProjectionSubsystemMessage.InvalidSubsystemRestart message) {
		return message.Reason;
	}


	private static ProjectionManagementMessage.RunAs GetRunAs(HttpEntityManager http, UriTemplateMatch match) {
		return new ProjectionManagementMessage.RunAs(http.User);
	}

	private static bool? IsOn(UriTemplateMatch match, string option, bool? def) {
		var rawValue = match.BoundVariables[option];
		if (string.IsNullOrEmpty(rawValue))
			return def;
		var value = rawValue.ToLowerInvariant();
		if ("yes" == value || "true" == value || "1" == value)
			return true;
		if ("no" == value || "false" == value || "0" == value)
			return false;
		//TODO: throw?
		return def;
	}

	private static bool IsOn(UriTemplateMatch match, string option, bool def) {
		var rawValue = match.BoundVariables[option];
		if (string.IsNullOrEmpty(rawValue))
			return def;
		var value = rawValue.ToLowerInvariant();
		return "yes" == value || "true" == value || "1" == value;
	}

	public static T EatException<T>(Func<T> func, T defaultValue = default(T)) {
		Ensure.NotNull(func, "func");
		try {
			return func();
		} catch (Exception) {
			return defaultValue;
		}
	}

	public class ProjectionConfigData {
		public bool EmitEnabled { get; set; }
		public bool TrackEmittedStreams { get; set; }
		public int CheckpointAfterMs { get; set; }
		public int CheckpointHandledThreshold { get; set; }
		public int CheckpointUnhandledBytesThreshold { get; set; }
		public int PendingEventsThreshold { get; set; }
		public int MaxWriteBatchLength { get; set; }
		public int MaxAllowedWritesInFlight { get; set; }

		public int? ProjectionExecutionTimeout { get; set; }
	}
}
