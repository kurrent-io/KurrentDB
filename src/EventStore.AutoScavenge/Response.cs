// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.AutoScavenge;

public static class Response {
	public static Response<T> Successful<T>(T value) => Response<T>.Successful(value);
	public static Response<T> Accepted<T>() => Response<T>.Accepted();
	public static Response<T> Rejected<T>(string message) => Response<T>.Rejected(message);
	public static Response<T> ServerError<T>(string message) => Response<T>.ServerError(message);

	public static Response<Unit> Successful() => Response<Unit>.Successful(Unit.Instance);
	public static Response<Unit> Accepted() => Response<Unit>.Accepted();
	public static Response<Unit> Rejected(string message) => Response<Unit>.Rejected(message);
	public static Response<Unit> ServerError(string message) => Response<Unit>.ServerError(message);
}

public readonly struct Response<T> {
	public enum State {
		ServerError,
		Rejected,

		// Command has been accepted by the server but processing of it has not complete,
		// it may yet succeed or fail.
		Accepted,
		Successful,
	};

	private readonly State _state;
	private readonly T? _value;
	private readonly string? _message = "default response";

	private Response(State state, T? value = default, string? message = null) {
		_state = state;
		_value = value;
		_message = message;
	}

	public static Response<T> Successful(T value) => new(State.Successful, value);
	public static Response<T> Accepted() => new(State.Accepted);
	public static Response<T> Rejected(string message) => new(State.Rejected, message: message);
	public static Response<T> ServerError(string message) => new(State.ServerError, message: message);

	public U Visit<U>(
		Func<T, U> onSuccessful,
		Func<U> onAccepted,
		Func<string, U> onRejected,
		Func<string, U> onServerError) =>

		_state switch {
			State.Successful => onSuccessful(_value!),
			State.Accepted => onAccepted(),
			State.Rejected => onRejected(_message!),
			State.ServerError => onServerError(_message!),
			_ => onServerError("Unexpected state"),
		};

	public readonly bool IsSuccessful(out T value) {
		value = _value!;
		return _state == State.Successful;
	}

	public readonly bool IsAccepted(out string message) {
		message = _message!;
		return _state == State.Accepted;
	}

	public readonly bool IsRejected(out string message) {
		message = _message!;
		return _state == State.Rejected;
	}

	public readonly bool IsServerError(out string message) {
		message = _message!;
		return _state == State.ServerError;
	}
}
