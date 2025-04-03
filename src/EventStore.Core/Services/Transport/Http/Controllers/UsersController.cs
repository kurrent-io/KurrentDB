// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Plugins.Authorization;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;
using EventStore.Transport.Http.EntityManagement;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Transport.Http.Controllers;

public class UsersController : CommunicationController {
	private readonly IHttpForwarder _httpForwarder;
	private readonly IPublisher _networkSendQueue;
	private static readonly ICodec[] DefaultCodecs = new ICodec[] {Codec.Json, Codec.Xml};
	private static readonly ILogger Log = Serilog.Log.ForContext<UsersController>();

	public UsersController(IHttpForwarder httpForwarder, IPublisher publisher, IPublisher networkSendQueue)
		: base(publisher) {
		_httpForwarder = httpForwarder;
		_networkSendQueue = networkSendQueue;
	}

	protected override void SubscribeCore(IHttpService service) {
		RegisterUrlBased(service, "/users", HttpMethod.Get, new Operation(Operations.Users.List), GetUsers);
		RegisterUrlBased(service, "/users/", HttpMethod.Get, new Operation(Operations.Users.List), GetUsers);
		RegisterUrlBased(service, "/users/{login}", HttpMethod.Get, new Operation(Operations.Users.Read), GetUser);
		RegisterUrlBased(service, "/users/$current", HttpMethod.Get, new Operation(Operations.Users.CurrentUser), GetCurrentUser);
		Register(service, "/users", HttpMethod.Post, PostUser, DefaultCodecs, DefaultCodecs, new Operation(Operations.Users.Create));
		Register(service, "/users/", HttpMethod.Post, PostUser, DefaultCodecs, DefaultCodecs, new Operation(Operations.Users.Create));
		Register(service, "/users/{login}", HttpMethod.Put, PutUser, DefaultCodecs, DefaultCodecs, new Operation(Operations.Users.Update));
		RegisterUrlBased(service, "/users/{login}", HttpMethod.Delete, new Operation(Operations.Users.Delete), DeleteUser);
		RegisterUrlBased(service, "/users/{login}/command/enable", HttpMethod.Post, new Operation(Operations.Users.Enable), PostCommandEnable);
		RegisterUrlBased(service, "/users/{login}/command/disable", HttpMethod.Post, new Operation(Operations.Users.Disable), PostCommandDisable);
		Register(service, "/users/{login}/command/reset-password", HttpMethod.Post, PostCommandResetPassword,
			DefaultCodecs, DefaultCodecs, new Operation(Operations.Users.ResetPassword));
		Register(service, "/users/{login}/command/change-password", HttpMethod.Post, PostCommandChangePassword,
			DefaultCodecs, DefaultCodecs, ForUser(Operations.Users.ChangePassword));
	}

	private static Func<UriTemplateMatch, Operation> ForUser(OperationDefinition definition) {
		return match => new Operation(definition).WithParameter(Operations.Users.Parameters.User(match.BoundVariables["login"]));
	}

	private void GetUsers(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope = CreateSendToHttpWithConversionEnvelope(http,
			(UserManagementMessage.AllUserDetailsResult msg) =>
				new UserManagementMessage.AllUserDetailsResultHttpFormatted(msg, s => MakeUrl(http, s)));

		var message = new UserManagementMessage.GetAll(envelope, http.User);
		Publish(message);
	}

	private void GetUser(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;

		var envelope = CreateSendToHttpWithConversionEnvelope(http,
			(UserManagementMessage.UserDetailsResult msg) =>
				new UserManagementMessage.UserDetailsResultHttpFormatted(msg, s => MakeUrl(http, s)));

		var login = match.BoundVariables["login"];
		var message = new UserManagementMessage.Get(envelope, http.User, login);
		Publish(message);
	}

	private void GetCurrentUser(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var envelope = CreateReplyEnvelope<UserManagementMessage.UserDetailsResult>(http);
		if (http.User == null) {
			envelope.ReplyWith(
				new UserManagementMessage.UserDetailsResult(UserManagementMessage.Error.Unauthorized));
			return;
		}

		var message = new UserManagementMessage.Get(envelope, http.User, http.User.Identity.Name);
		Publish(message);
	}

	private void PostUser(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var envelope = CreateReplyEnvelope<UserManagementMessage.UpdateResult>(
			http, configurator: (codec, result) => {
				var configuration = AutoConfigurator(codec, result);
				return configuration.Code == HttpStatusCode.OK
					? configuration.SetCreated(
						MakeUrl(http, "/users/" + Uri.EscapeDataString(result.LoginName)))
					: configuration;
			});
		http.ReadTextRequestAsync(
			(o, s) => {
				var data = http.RequestCodec.From<PostUserData>(s);
				var message = new UserManagementMessage.Create(
					envelope, http.User, data.LoginName, data.FullName, data.Groups, data.Password);
				Publish(message);
			}, x => Log.Debug(x, "Reply Text Content Failed."));
	}

	private void PutUser(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var envelope = CreateReplyEnvelope<UserManagementMessage.UpdateResult>(http);
		http.ReadTextRequestAsync(
			(o, s) => {
				var login = match.BoundVariables["login"];
				var data = http.RequestCodec.From<PutUserData>(s);
				var message =
					new UserManagementMessage.Update(envelope, http.User, login, data.FullName, data.Groups);
				Publish(message);
			}, x => Log.Debug(x, "Reply Text Content Failed."));
	}

	private void DeleteUser(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var envelope = CreateReplyEnvelope<UserManagementMessage.UpdateResult>(http);
		var login = match.BoundVariables["login"];
		var message = new UserManagementMessage.Delete(envelope, http.User, login);
		Publish(message);
	}

	private void PostCommandEnable(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var envelope = CreateReplyEnvelope<UserManagementMessage.UpdateResult>(http);
		var login = match.BoundVariables["login"];
		var message = new UserManagementMessage.Enable(envelope, http.User, login);
		Publish(message);
	}

	private void PostCommandDisable(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var envelope = CreateReplyEnvelope<UserManagementMessage.UpdateResult>(http);
		var login = match.BoundVariables["login"];
		var message = new UserManagementMessage.Disable(envelope, http.User, login);
		Publish(message);
	}

	private void PostCommandResetPassword(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var envelope = CreateReplyEnvelope<UserManagementMessage.UpdateResult>(http);
		http.ReadTextRequestAsync(
			(o, s) => {
				var login = match.BoundVariables["login"];
				var data = http.RequestCodec.From<ResetPasswordData>(s);
				var message = new UserManagementMessage.ResetPassword(envelope, http.User, login, data.NewPassword);
				Publish(message);
			}, x => Log.Debug(x, "Reply Text Content Failed."));
	}

	private void PostCommandChangePassword(HttpEntityManager http, UriTemplateMatch match) {
		if (_httpForwarder.ForwardRequest(http))
			return;
		var envelope = CreateReplyEnvelope<UserManagementMessage.UpdateResult>(http);
		http.ReadTextRequestAsync(
			(o, s) => {
				var login = match.BoundVariables["login"];
				var data = http.RequestCodec.From<ChangePasswordData>(s);
				var message = new UserManagementMessage.ChangePassword(
					envelope, http.User, login, data.CurrentPassword, data.NewPassword);
				Publish(message);
			},
			x => Log.Debug(x, "Reply Text Content Failed."));
	}

	private SendToHttpEnvelope<T> CreateReplyEnvelope<T>(
		HttpEntityManager http, Func<ICodec, T, string> formatter = null,
		Func<ICodec, T, ResponseConfiguration> configurator = null)
		where T : UserManagementMessage.ResponseMessage {
		return new SendToHttpEnvelope<T>(
			_networkSendQueue, http, formatter ?? AutoFormatter, configurator ?? AutoConfigurator, null);
	}

	private SendToHttpWithConversionEnvelope<T, R> CreateSendToHttpWithConversionEnvelope<T, R>(
		HttpEntityManager http, Func<T, R> formatter)
		where T : UserManagementMessage.ResponseMessage
		where R : UserManagementMessage.ResponseMessage {
		return new SendToHttpWithConversionEnvelope<T, R>(_networkSendQueue,
			http,
			(codec, msg) => codec.To(msg),
			(codec, transformed) => transformed.Success
				? new ResponseConfiguration(HttpStatusCode.OK, codec.ContentType, codec.Encoding)
				: new ResponseConfiguration(
					ErrorToHttpStatusCode(transformed.Error), codec.ContentType, codec.Encoding),
			formatter);
	}

	private ResponseConfiguration AutoConfigurator<T>(ICodec codec, T result)
		where T : UserManagementMessage.ResponseMessage {
		return result.Success
			? new ResponseConfiguration(HttpStatusCode.OK, codec.ContentType, codec.Encoding)
			: new ResponseConfiguration(
				ErrorToHttpStatusCode(result.Error), codec.ContentType, codec.Encoding);
	}

	private string AutoFormatter<T>(ICodec codec, T result) {
		return codec.To(result);
	}

	private int ErrorToHttpStatusCode(UserManagementMessage.Error error) {
		switch (error) {
			case UserManagementMessage.Error.Success:
				return HttpStatusCode.OK;
			case UserManagementMessage.Error.Conflict:
				return HttpStatusCode.Conflict;
			case UserManagementMessage.Error.NotFound:
				return HttpStatusCode.NotFound;
			case UserManagementMessage.Error.Error:
				return HttpStatusCode.InternalServerError;
			case UserManagementMessage.Error.TryAgain:
				return HttpStatusCode.RequestTimeout;
			case UserManagementMessage.Error.Unauthorized:
				return HttpStatusCode.Unauthorized;
			default:
				return HttpStatusCode.InternalServerError;
		}
	}

	private class PostUserData {
		public string LoginName { get; set; }
		public string FullName { get; set; }
		public string[] Groups { get; set; }
		public string Password { get; set; }
	}

	private class PutUserData {
		public string FullName { get; set; }
		public string[] Groups { get; set; }
	}

	private class ResetPasswordData {
		public string NewPassword { get; set; }
	}

	private class ChangePasswordData {
		public string CurrentPassword { get; set; }
		public string NewPassword { get; set; }
	}
}
