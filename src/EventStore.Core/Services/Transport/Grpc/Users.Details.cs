// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Client.Users;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using static EventStore.Plugins.Authorization.Operations.Users;

namespace EventStore.Core.Services.Transport.Grpc;

internal partial class Users {
	private static readonly Operation ReadOperation = new(Read);

	public override async Task Details(DetailsReq request, IServerStreamWriter<DetailsResp> responseStream, ServerCallContext context) {
		var options = request.Options;

		var user = context.GetHttpContext().User;
		var readOperation = ReadOperation;
		if (user?.Identity?.Name != null) {
			readOperation = readOperation.WithParameter(Parameters.User(user.Identity.Name));
		}

		if (!await _authorizationProvider.CheckAccessAsync(user, readOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		var detailsSource = new TaskCompletionSource<UserManagementMessage.UserData[]>();

		var envelope = new CallbackEnvelope(OnMessage);

		_publisher.Publish(string.IsNullOrWhiteSpace(options?.LoginName)
			? new UserManagementMessage.GetAll(envelope, user)
			: new UserManagementMessage.Get(envelope, user, options.LoginName));

		var details = await detailsSource.Task;

		foreach (var detail in details) {
			await responseStream.WriteAsync(new DetailsResp {
				UserDetails = new DetailsResp.Types.UserDetails {
					Disabled = detail.Disabled,
					Groups = { detail.Groups },
					FullName = detail.FullName,
					LoginName = detail.LoginName,
					LastUpdated = detail.DateLastUpdated.HasValue
						? new DetailsResp.Types.UserDetails.Types.DateTime
							{ TicksSinceEpoch = detail.DateLastUpdated.Value.UtcDateTime.ToTicksSinceEpoch() }
						: null
				}
			});
		}

		void OnMessage(Message message) {
			if (HandleErrors(options?.LoginName, message, detailsSource)) return;

			switch (message) {
				case UserManagementMessage.UserDetailsResult userDetails:
					detailsSource.TrySetResult([userDetails.Data]);
					break;
				case UserManagementMessage.AllUserDetailsResult allUserDetails:
					detailsSource.TrySetResult(allUserDetails.Data);
					break;
				default:
					detailsSource.TrySetException(RpcExceptions.UnknownError(1));
					break;
			}
		}
	}
}
