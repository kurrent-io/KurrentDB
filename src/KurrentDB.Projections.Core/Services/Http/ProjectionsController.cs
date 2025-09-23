// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EventStore.Plugins.Authorization;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Transport.Http;
using KurrentDB.Core.Services.Transport.Http.Controllers;
using KurrentDB.Core.Util;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Messages.EventReaders.Feeds;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Transport.Http;
using KurrentDB.Transport.Http.Codecs;
using KurrentDB.Transport.Http.EntityManagement;
using Newtonsoft.Json.Linq;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Projections.Core.Services.Http;

public class ProjectionsController(IHttpForwarder httpForwarder, IPublisher publisher, IPublisher networkSendQueue)
	: CommunicationController(publisher) {
	private static readonly ILogger Log = Serilog.Log.ForContext<ProjectionsController>();

	private static readonly ICodec[] SupportedCodecs = [Codec.Json];

	private readonly MiniWeb _clusterNodeJs = new("/web/es/js/projections", Locations.ProjectionsDirectory);
	private readonly MiniWeb _miniWebPrelude = new("/web/es/js/projections/v8/Prelude", Locations.PreludeDirectory);

	protected override void SubscribeCore(IHttpService service) {
		_clusterNodeJs.RegisterControllerActions(service);

		_miniWebPrelude.RegisterControllerActions(service);

		HttpHelpers.RegisterRedirectAction(service, "/web/projections", "/web/projections.htm");

		Register(service, "/projections",
			HttpMethod.Get, OnProjections, Codec.NoCodecs, [Codec.ManualEncoding], new Operation(Operations.Projections.List));
		Register(service, "/projections/restart",
			HttpMethod.Post, OnProjectionsRestart, [Codec.ManualEncoding], SupportedCodecs, new Operation(Operations.Projections.Restart));
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
			HttpMethod.Post, OnProjectionsPostTransient, [Codec.ManualEncoding], SupportedCodecs,
			new Operation(Operations.Projections.Create).WithParameter(Operations.Projections.Parameters.Query));
		Register(service,
			"/projections/onetime?name={name}&type={type}&enabled={enabled}&checkpoints={checkpoints}&emit={emit}&trackemittedstreams={trackemittedstreams}",
			HttpMethod.Post, OnProjectionsPostOneTime, [Codec.ManualEncoding], SupportedCodecs,
			new Operation(Operations.Projections.Create).WithParameter(Operations.Projections.Parameters.OneTime));
		Register(service,
			"/projections/continuous?name={name}&type={type}&enabled={enabled}&emit={emit}&trackemittedstreams={trackemittedstreams}",
			HttpMethod.Post, OnProjectionsPostContinuous, [Codec.ManualEncoding], SupportedCodecs,
			new Operation(Operations.Projections.Create).WithParameter(Operations.Projections.Parameters.Continuous));
		Register(service, "/projection/{name}/query?config={config}",
			HttpMethod.Get, OnProjectionQueryGet, Codec.NoCodecs, [Codec.ManualEncoding], new Operation(Operations.Projections.Read));
		Register(service, "/projection/{name}/query?type={type}&emit={emit}",
			HttpMethod.Put, OnProjectionQueryPut, [Codec.ManualEncoding], SupportedCodecs,
			new Operation(Operations.Projections
				.Update)); /* source of transient projections can be set by a normal user. Authorization checks are done internally for non-transient projections. */
		Register(service, "/projection/{name}",
			HttpMethod.Get, OnProjectionStatusGet, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.Status));
		Register(service,
			"/projection/{name}?deleteStateStream={deleteStateStream}&deleteCheckpointStream={deleteCheckpointStream}&deleteEmittedStreams={deleteEmittedStreams}",
			HttpMethod.Delete, OnProjectionDelete, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.Delete));
		Register(service, "/projection/{name}/statistics",
			HttpMethod.Get, OnProjectionStatisticsGet, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.Statistics));
		Register(service, "/projections/read-events",
			HttpMethod.Post, OnProjectionsReadEvents, SupportedCodecs, SupportedCodecs,
			new Operation(Operations.Projections.DebugProjection));
		Register(service, "/projection/{name}/state?partition={partition}",
			HttpMethod.Get, OnProjectionStateGet, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.State));
		Register(service, "/projection/{name}/result?partition={partition}",
			HttpMethod.Get, OnProjectionResultGet, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Projections.Result));
		Register(service, "/projection/{name}/command/disable?enableRunAs={enableRunAs}",
			HttpMethod.Post, OnProjectionCommandDisable, Codec.NoCodecs, SupportedCodecs,
			new Operation(Operations.Projections
				.Disable)); /* transient projections can be stopped by a normal user. Authorization checks are done internally for non-transient projections.*/
		Register(service, "/projection/{name}/command/enable?enableRunAs={enableRunAs}",
			HttpMethod.Post, OnProjectionCommandEnable, Codec.NoCodecs, SupportedCodecs,
			new Operation(Operations.Projections
				.Enable)); /* transient projections can be enabled by a normal user. Authorization checks are done internally for non-transient projections.*/
		Register(service, "/projection/{name}/command/reset?enableRunAs={enableRunAs}",
			HttpMethod.Post, OnProjectionCommandReset, Codec.NoCodecs, SupportedCodecs,
			new Operation(Operations.Projections
				.Reset)); /* transient projections can be reset by a normal user (when debugging). Authorization checks are done internally for non-transient projections.*/
		Register(service, "/projection/{name}/command/abort?enableRunAs={enableRunAs}",
			HttpMethod.Post, OnProjectionCommandAbort, Codec.NoCodecs, SupportedCodecs,
			new Operation(Operations.Projections
				.Abort)); /* transient projections can be aborted by a normal user. Authorization checks are done internally for non-transient projections.*/
		Register(service, "/projection/{name}/config",
			HttpMethod.Get, OnProjectionConfigGet, Codec.NoCodecs, SupportedCodecs,
			new Operation(Operations.Projections.ReadConfiguration));
		Register(service, "/projection/{name}/config",
			HttpMethod.Put, OnProjectionConfigPut, SupportedCodecs, SupportedCodecs,
			new Operation(Operations.Projections.UpdateConfiguration));
	}

	private void OnProjections(HttpEntityManager http, UriTemplateMatch match) {
		if (httpForwarder.ForwardRequest(http))
			return;

		http.ReplyTextContent(
			"Moved", 302, "Found", ContentType.PlainText,
			[
				new("Location", new Uri(match.BaseUri, "/web/projections.htm").AbsoluteUri)
			], x => Log.Debug(x, "Reply Text Content Failed."));
	}

	private void OnProjectionsRestart(HttpEntityManager http, UriTemplateMatch match) {
		if (httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionSubsystemMessage.SubsystemRestarting>(networkSendQueue, http,
			(e, _) => e.To("Restarting"),
			(e, message) => {
				return message switch {
					not null => Configure.Ok(e.ContentType),
					_ => Configure.InternalServerError()
				};
			}, CreateErrorEnvelope(http)
		);

		Publish(new ProjectionSubsystemMessage.RestartSubsystem(envelope));
	}

	private void OnProjectionsGetAny(HttpEntityManager http, UriTemplateMatch match) {
		ProjectionsGet(http, null);
	}

	private void OnProjectionsGetAllNonTransient(HttpEntityManager http, UriTemplateMatch match) {
		ProjectionsGet(http, ProjectionMode.AllNonTransient);
	}

	private void OnProjectionsGetTransient(HttpEntityManager http, UriTemplateMatch match) {
		ProjectionsGet(http, ProjectionMode.Transient);
	}

	private void OnProjectionsGetOneTime(HttpEntityManager http, UriTemplateMatch match) {
		ProjectionsGet(http, ProjectionMode.OneTime);
	}

	private void OnProjectionsGetContinuous(HttpEntityManager http, UriTemplateMatch match) {
		ProjectionsGet(http, ProjectionMode.Continuous);
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
		if (httpForwarder.ForwardRequest(http))
			return;

		SendToHttpEnvelope<ProjectionManagementMessage.ProjectionQuery> envelope;
		var withConfig = IsOn(match, "config", false);
		if (withConfig)
			envelope = new(networkSendQueue, http, QueryConfigFormatter, QueryConfigConfigurator, CreateErrorEnvelope(http));
		else
			envelope = new(networkSendQueue, http, QueryFormatter, QueryConfigurator, CreateErrorEnvelope(http));
		Publish(new ProjectionManagementMessage.Command.GetQuery(envelope, match.BoundVariables["name"], GetRunAs(http)));
	}

	private void OnProjectionQueryPut(HttpEntityManager http, UriTemplateMatch match) {
		if (httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.Updated>(
			networkSendQueue, http, DefaultFormatter, OkResponseConfigurator, CreateErrorEnvelope(http));
		var emitEnabled = IsOn(match, "emit", null);
		http.ReadTextRequestAsync(
			(_, s) => Publish(
				new ProjectionManagementMessage.Command.UpdateQuery(
					envelope, match.BoundVariables["name"], GetRunAs(http),
					s, emitEnabled: emitEnabled)
			), Console.WriteLine);
	}

	private void OnProjectionConfigGet(HttpEntityManager http, UriTemplateMatch match) {
		if (httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.ProjectionConfig>(
			networkSendQueue, http, ProjectionConfigFormatter, ProjectionConfigConfigurator, CreateErrorEnvelope(http));
		Publish(new ProjectionManagementMessage.Command.GetConfig(envelope, match.BoundVariables["name"], GetRunAs(http)));
	}

	private void OnProjectionConfigPut(HttpEntityManager http, UriTemplateMatch match) {
		if (httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.Updated>(
			networkSendQueue, http, DefaultFormatter, OkResponseConfigurator, CreateErrorEnvelope(http));
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
					config.MaxWriteBatchLength, config.MaxAllowedWritesInFlight, GetRunAs(http), config.ProjectionExecutionTimeout);
				Publish(message);
			}, ex => Log.Debug("Failed to update projection configuration. Error: {e}", ex));
	}

	private void OnProjectionCommandDisable(HttpEntityManager http, UriTemplateMatch match) {
		if (httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.Updated>(
			networkSendQueue, http, DefaultFormatter, OkResponseConfigurator, CreateErrorEnvelope(http));
		Publish(new ProjectionManagementMessage.Command.Disable(envelope, match.BoundVariables["name"], GetRunAs(http)));
	}

	private void OnProjectionCommandEnable(HttpEntityManager http, UriTemplateMatch match) {
		if (httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.Updated>(
			networkSendQueue, http, DefaultFormatter, OkResponseConfigurator, CreateErrorEnvelope(http));
		var name = match.BoundVariables["name"];
		Publish(new ProjectionManagementMessage.Command.Enable(envelope, name, GetRunAs(http)));
	}

	private void OnProjectionCommandReset(HttpEntityManager http, UriTemplateMatch match) {
		if (httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.Updated>(
			networkSendQueue, http, DefaultFormatter, OkResponseConfigurator, CreateErrorEnvelope(http));
		Publish(new ProjectionManagementMessage.Command.Reset(envelope, match.BoundVariables["name"], GetRunAs(http)));
	}

	private void OnProjectionCommandAbort(HttpEntityManager http, UriTemplateMatch match) {
		if (httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.Updated>(
			networkSendQueue, http, DefaultFormatter, OkResponseConfigurator, CreateErrorEnvelope(http));
		Publish(new ProjectionManagementMessage.Command.Abort(envelope, match.BoundVariables["name"], GetRunAs(http)));
	}

	private void OnProjectionStatusGet(HttpEntityManager http, UriTemplateMatch match) {
		if (httpForwarder.ForwardRequest(http))
			return;

		var envelope =
			new SendToHttpWithConversionEnvelope
				<ProjectionManagementMessage.Statistics, ProjectionStatisticsHttpFormatted>(
					networkSendQueue, http, DefaultFormatter, OkNoCacheResponseConfigurator,
					status => new(status.Projections[0], s => MakeUrl(http, s)),
					CreateErrorEnvelope(http));
		Publish(new ProjectionManagementMessage.Command.GetStatistics(envelope, null, match.BoundVariables["name"]));
	}

	private void OnProjectionDelete(HttpEntityManager http, UriTemplateMatch match) {
		if (httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.Updated>(
			networkSendQueue, http, DefaultFormatter, OkResponseConfigurator, CreateErrorEnvelope(http));
		Publish(
			new ProjectionManagementMessage.Command.Delete(
				envelope, match.BoundVariables["name"], GetRunAs(http),
				IsOn(match, "deleteCheckpointStream", false),
				IsOn(match, "deleteStateStream", false),
				IsOn(match, "deleteEmittedStreams", false)));
	}

	private void OnProjectionStatisticsGet(HttpEntityManager http, UriTemplateMatch match) {
		if (httpForwarder.ForwardRequest(http))
			return;

		var envelope =
			new SendToHttpWithConversionEnvelope
				<ProjectionManagementMessage.Statistics, ProjectionsStatisticsHttpFormatted>(
					networkSendQueue, http, DefaultFormatter, OkNoCacheResponseConfigurator,
					status => new(status, s => MakeUrl(http, s)),
					CreateErrorEnvelope(http));
		Publish(new ProjectionManagementMessage.Command.GetStatistics(envelope, null, match.BoundVariables["name"]));
	}

	private void OnProjectionStateGet(HttpEntityManager http, UriTemplateMatch match) {
		if (httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.ProjectionState>(
			networkSendQueue, http, StateFormatter, StateConfigurator, CreateErrorEnvelope(http));
		Publish(
			new ProjectionManagementMessage.Command.GetState(
				envelope, match.BoundVariables["name"], match.BoundVariables["partition"] ?? ""));
	}

	private void OnProjectionResultGet(HttpEntityManager http, UriTemplateMatch match) {
		if (httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.ProjectionResult>(
			networkSendQueue, http, ResultFormatter, ResultConfigurator, CreateErrorEnvelope(http));
		Publish(
			new ProjectionManagementMessage.Command.GetResult(
				envelope, match.BoundVariables["name"], match.BoundVariables["partition"] ?? ""));
	}

	[UsedImplicitly]
	private class ReadEventsBody {
		public QuerySourcesDefinition Query { get; set; }
		public JObject Position { get; set; }
		public int? MaxEvents { get; set; }
	}

	private void OnProjectionsReadEvents(HttpEntityManager http, UriTemplateMatch match) {
		if (httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<FeedReaderMessage.FeedPage>(
			networkSendQueue, http, FeedPageFormatter, FeedPageConfigurator, CreateErrorEnvelope(http));

		http.ReadTextRequestAsync(
			(_, body) => {
				var bodyParsed = body.ParseJson<ReadEventsBody>();
				var fromPosition = CheckpointTag.FromJson(new JTokenReader(bodyParsed.Position), new ProjectionVersion(0, 0, 0));

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

	private void ProjectionsGet(HttpEntityManager http, ProjectionMode? mode) {
		if (httpForwarder.ForwardRequest(http))
			return;

		var envelope =
			new SendToHttpWithConversionEnvelope<ProjectionManagementMessage.Statistics,
				ProjectionsStatisticsHttpFormatted>(
				networkSendQueue, http, DefaultFormatter, OkNoCacheResponseConfigurator,
				status => new(status, s => MakeUrl(http, s)),
				CreateErrorEnvelope(http));
		Publish(new ProjectionManagementMessage.Command.GetStatistics(envelope, mode, null));
	}

	private void ProjectionsPost(HttpEntityManager http, UriTemplateMatch match, ProjectionMode mode, string name) {
		if (httpForwarder.ForwardRequest(http))
			return;

		var envelope = new SendToHttpEnvelope<ProjectionManagementMessage.Updated>(
			networkSendQueue, http, DefaultFormatter, (codec, message) => {
				var localPath = $"/projection/{message.Name}";
				var url = MakeUrl(http, localPath);
				return new(201, "Created", codec.ContentType, codec.Encoding, new KeyValuePair<string, string>("Location", url));
			}, CreateErrorEnvelope(http));
		http.ReadTextRequestAsync(
			(_, s) => {
				ProjectionManagementMessage.Command.Post postMessage;
				string handlerType = match.BoundVariables["type"] ?? "JS";
				bool emitEnabled = IsOn(match, "emit", false);
				bool checkpointsEnabled = mode >= ProjectionMode.Continuous || IsOn(match, "checkpoints", false);
				bool enabled = IsOn(match, "enabled", def: true);
				bool trackEmittedStreams = IsOn(match, "trackemittedstreams", def: false);
				if (!emitEnabled) {
					trackEmittedStreams = false;
				}

				var runAs = GetRunAs(http);
				if (mode <= ProjectionMode.OneTime && string.IsNullOrEmpty(name))
					postMessage = new(
						envelope, mode, Guid.NewGuid().ToString("D"), runAs, handlerType, s, enabled: enabled,
						checkpointsEnabled: checkpointsEnabled, emitEnabled: emitEnabled,
						trackEmittedStreams: trackEmittedStreams, enableRunAs: true);
				else
					postMessage = new(
						envelope, mode, name, runAs, handlerType, s, enabled: enabled,
						checkpointsEnabled: checkpointsEnabled, emitEnabled: emitEnabled,
						trackEmittedStreams: trackEmittedStreams, enableRunAs: true);
				Publish(postMessage);
			}, x => Log.Debug(x, "Reply Text Body Failed."));
	}

	private static ResponseConfiguration StateConfigurator(ICodec codec, ProjectionManagementMessage.ProjectionState state) {
		return state.Exception != null
			? Configure.InternalServerError()
			: state.Position != null
				? Configure.Ok(ContentType.Json, Helper.UTF8NoBom, null, null, false,
					new KeyValuePair<string, string>(SystemHeaders.ProjectionPosition, state.Position.ToJsonString()),
					new KeyValuePair<string, string>(SystemHeaders.LegacyProjectionPosition, state.Position.ToJsonString()))
				: Configure.Ok(ContentType.Json, Helper.UTF8NoBom, null, null, false);
	}

	private static ResponseConfiguration ResultConfigurator(ICodec codec, ProjectionManagementMessage.ProjectionResult state) {
		return state.Exception != null
			? Configure.InternalServerError()
			: state.Position != null
				? Configure.Ok(ContentType.Json, Helper.UTF8NoBom, null, null, false,
					new KeyValuePair<string, string>(SystemHeaders.ProjectionPosition, state.Position.ToJsonString()),
					new KeyValuePair<string, string>(SystemHeaders.LegacyProjectionPosition, state.Position.ToJsonString()))
				: Configure.Ok(ContentType.Json, Helper.UTF8NoBom, null, null, false);
	}

	private static ResponseConfiguration FeedPageConfigurator(ICodec codec, FeedReaderMessage.FeedPage page) {
		return page.Error == FeedReaderMessage.FeedPage.ErrorStatus.NotAuthorized
			? Configure.Unauthorized()
			: Configure.Ok(ContentType.Json, Helper.UTF8NoBom, null, null, false);
	}

	private static string StateFormatter(ICodec codec, ProjectionManagementMessage.ProjectionState state)
		=> state.Exception != null ? state.Exception.ToString() : state.State;

	private static string ResultFormatter(ICodec codec, ProjectionManagementMessage.ProjectionResult state)
		=> state.Exception != null ? state.Exception.ToString() : state.Result;

	private static string FeedPageFormatter(ICodec codec, FeedReaderMessage.FeedPage page) {
		if (page.Error != FeedReaderMessage.FeedPage.ErrorStatus.Success)
			return null;

		return new {
			CorrelationId = page.CorrelationId,
			ReaderPosition = page.LastReaderPosition.ToJsonRaw(),
			Events = (from e in page.Events
				let resolvedEvent = e.ResolvedEvent
				let isJson = resolvedEvent.IsJson
				let data = isJson
					? EatException(object () => resolvedEvent.Data.ParseJson<JObject>(), resolvedEvent.Data)
					: resolvedEvent.Data
				let metadata = isJson
					? EatException(object () => resolvedEvent.Metadata.ParseJson<JObject>(), resolvedEvent.Metadata)
					: resolvedEvent.Metadata
				let linkMetadata = isJson
					? EatException(object () => resolvedEvent.PositionMetadata.ParseJson<JObject>(), resolvedEvent.PositionMetadata)
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

	private static ResponseConfiguration QueryConfigurator(ICodec codec, ProjectionManagementMessage.ProjectionQuery state)
		=> Configure.Ok("application/javascript", Helper.UTF8NoBom, null, null, false);

	private static string QueryFormatter(ICodec codec, ProjectionManagementMessage.ProjectionQuery state) => state.Query;

	private static string QueryConfigFormatter(ICodec codec, ProjectionManagementMessage.ProjectionQuery state) => state.ToJson();

	private static ResponseConfiguration QueryConfigConfigurator(ICodec codec, ProjectionManagementMessage.ProjectionQuery state)
		=> Configure.Ok(ContentType.Json, Helper.UTF8NoBom, null, null, false);

	private static string ProjectionConfigFormatter(ICodec codec, ProjectionManagementMessage.ProjectionConfig config) => config.ToJson();

	private static ResponseConfiguration ProjectionConfigConfigurator(ICodec codec, ProjectionManagementMessage.ProjectionConfig state)
		=> Configure.Ok(ContentType.Json, Helper.UTF8NoBom, null, null, false);

	private static ResponseConfiguration OkResponseConfigurator<T>(ICodec codec, T message)
		=> new(200, "OK", codec.ContentType, Helper.UTF8NoBom);

	private static ResponseConfiguration OkNoCacheResponseConfigurator<T>(ICodec codec, T message)
		=> Configure.Ok(codec.ContentType, codec.Encoding, null, null, false);

	private IEnvelope CreateErrorEnvelope(HttpEntityManager http) {
		return new SendToHttpEnvelope<ProjectionManagementMessage.NotFound>(
			networkSendQueue,
			http,
			NotFoundFormatter,
			NotFoundConfigurator,
			new SendToHttpEnvelope<ProjectionManagementMessage.NotAuthorized>(
				networkSendQueue,
				http,
				NotAuthorizedFormatter,
				NotAuthorizedConfigurator,
				new SendToHttpEnvelope<ProjectionManagementMessage.Conflict>(
					networkSendQueue,
					http,
					ConflictFormatter,
					ConflictConfigurator,
					new SendToHttpEnvelope<ProjectionManagementMessage.OperationFailed>(
						networkSendQueue,
						http,
						OperationFailedFormatter,
						OperationFailedConfigurator,
						new SendToHttpEnvelope<ProjectionSubsystemMessage.InvalidSubsystemRestart>(
							networkSendQueue,
							http,
							InvalidSubsystemRestartFormatter,
							InvalidSubsystemRestartConfigurator,
							null)))));
	}

	private static ResponseConfiguration NotFoundConfigurator(ICodec codec, ProjectionManagementMessage.NotFound message)
		=> new(404, "Not Found", ContentType.PlainText, Helper.UTF8NoBom);

	private static string NotFoundFormatter(ICodec codec, ProjectionManagementMessage.NotFound message) => message.Reason;

	private static ResponseConfiguration NotAuthorizedConfigurator(ICodec codec, ProjectionManagementMessage.NotAuthorized message)
		=> new(401, "Not Authorized", ContentType.PlainText, Encoding.UTF8);

	private static string NotAuthorizedFormatter(ICodec codec, ProjectionManagementMessage.NotAuthorized message) => message.Reason;

	private static ResponseConfiguration OperationFailedConfigurator(ICodec codec, ProjectionManagementMessage.OperationFailed message)
		=> new(500, "Failed", ContentType.PlainText, Helper.UTF8NoBom);

	private static string OperationFailedFormatter(ICodec codec, ProjectionManagementMessage.OperationFailed message) => message.Reason;

	private static ResponseConfiguration ConflictConfigurator(ICodec codec, ProjectionManagementMessage.OperationFailed message)
		=> new(409, "Conflict", ContentType.PlainText, Helper.UTF8NoBom);

	private static string ConflictFormatter(ICodec codec, ProjectionManagementMessage.OperationFailed message) => message.Reason;

	private static string DefaultFormatter<T>(ICodec codec, T message) => codec.To(message);

	private static ResponseConfiguration InvalidSubsystemRestartConfigurator(ICodec codec,
		ProjectionSubsystemMessage.InvalidSubsystemRestart message)
		=> new(HttpStatusCode.BadRequest, "Bad Request", ContentType.PlainText, Helper.UTF8NoBom);

	private static string InvalidSubsystemRestartFormatter(ICodec codec, ProjectionSubsystemMessage.InvalidSubsystemRestart message) => message.Reason;

	private static ProjectionManagementMessage.RunAs GetRunAs(HttpEntityManager http) => new(http.User);

	private static bool? IsOn(UriTemplateMatch match, string option, bool? def) {
		var rawValue = match.BoundVariables[option];
		if (string.IsNullOrEmpty(rawValue))
			return def;
		var value = rawValue.ToLowerInvariant();
		return value switch {
			"yes" or "true" or "1" => true,
			"no" or "false" or "0" => false,
			_ => def
		};
	}

	private static bool IsOn(UriTemplateMatch match, string option, bool def) {
		var rawValue = match.BoundVariables[option];
		if (string.IsNullOrEmpty(rawValue))
			return def;
		var value = rawValue.ToLowerInvariant();
		return value is "yes" or "true" or "1";
	}

	private static T EatException<T>(Func<T> func, T defaultValue = default) {
		Ensure.NotNull(func);
		try {
			return func();
		} catch (Exception) {
			return defaultValue;
		}
	}

	[UsedImplicitly]
	private class ProjectionConfigData {
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
