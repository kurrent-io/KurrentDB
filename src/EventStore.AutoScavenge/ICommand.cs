// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.AutoScavenge;

public interface ICommand {
	public void OnSavedEvents();
	public void OnServerError(string message);
}

public interface ICommand<T> : ICommand {
	// conditional upon writing the events (if any).
	public void SetConditionalResponse(Response<T> response);
}

public record Command<T> : ICommand<T> {
	readonly Action<Response<T>> _callback;
	Response<T> _conditionalResponse;

	public Command(Action<Response<T>> callback) {
		_callback = callback;
	}

	// This response will be sent if the associated events (if any) are persisted
	public void SetConditionalResponse(Response<T> response) {
		_conditionalResponse = response;
	}

	public void OnSavedEvents() {
		_callback(_conditionalResponse);
	}

	public void OnServerError(string message) {
		_callback(Response<T>.ServerError(message));
	}
}
