// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Transport.Http;

public static class HttpStatusCode {
	public const int Continue = (int)System.Net.HttpStatusCode.Continue;
	public const int SwitchingProtocols = (int)System.Net.HttpStatusCode.SwitchingProtocols;
	public const int OK = (int)System.Net.HttpStatusCode.OK;
	public const int Created = (int)System.Net.HttpStatusCode.Created;
	public const int Accepted = (int)System.Net.HttpStatusCode.Accepted;
	public const int NonAuthoritativeInformation = (int)System.Net.HttpStatusCode.NonAuthoritativeInformation;
	public const int NoContent = (int)System.Net.HttpStatusCode.NoContent;
	public const int ResetContent = (int)System.Net.HttpStatusCode.ResetContent;
	public const int PartialContent = (int)System.Net.HttpStatusCode.PartialContent;
	public const int Ambiguous = (int)System.Net.HttpStatusCode.Ambiguous;
	public const int MultipleChoices = (int)System.Net.HttpStatusCode.MultipleChoices;
	public const int Moved = (int)System.Net.HttpStatusCode.Moved;
	public const int MovedPermanently = (int)System.Net.HttpStatusCode.MovedPermanently;
	public const int Found = (int)System.Net.HttpStatusCode.Found;
	public const int Redirect = (int)System.Net.HttpStatusCode.Redirect;
	public const int RedirectMethod = (int)System.Net.HttpStatusCode.RedirectMethod;
	public const int SeeOther = (int)System.Net.HttpStatusCode.SeeOther;
	public const int NotModified = (int)System.Net.HttpStatusCode.NotModified;
	public const int UseProxy = (int)System.Net.HttpStatusCode.UseProxy;
	public const int Unused = (int)System.Net.HttpStatusCode.Unused;
	public const int RedirectKeepVerb = (int)System.Net.HttpStatusCode.RedirectKeepVerb;
	public const int TemporaryRedirect = (int)System.Net.HttpStatusCode.TemporaryRedirect;
	public const int BadRequest = (int)System.Net.HttpStatusCode.BadRequest;
	public const int Unauthorized = (int)System.Net.HttpStatusCode.Unauthorized;
	public const int PaymentRequired = (int)System.Net.HttpStatusCode.PaymentRequired;
	public const int Forbidden = (int)System.Net.HttpStatusCode.Forbidden;
	public const int NotFound = (int)System.Net.HttpStatusCode.NotFound;
	public const int MethodNotAllowed = (int)System.Net.HttpStatusCode.MethodNotAllowed;
	public const int NotAcceptable = (int)System.Net.HttpStatusCode.NotAcceptable;
	public const int ProxyAuthenticationRequired = (int)System.Net.HttpStatusCode.ProxyAuthenticationRequired;
	public const int RequestTimeout = (int)System.Net.HttpStatusCode.RequestTimeout;
	public const int Conflict = (int)System.Net.HttpStatusCode.Conflict;
	public const int Gone = (int)System.Net.HttpStatusCode.Gone;
	public const int LengthRequired = (int)System.Net.HttpStatusCode.LengthRequired;
	public const int PreconditionFailed = (int)System.Net.HttpStatusCode.PreconditionFailed;
	public const int RequestEntityTooLarge = (int)System.Net.HttpStatusCode.RequestEntityTooLarge;
	public const int RequestUriTooLong = (int)System.Net.HttpStatusCode.RequestUriTooLong;
	public const int UnsupportedMediaType = (int)System.Net.HttpStatusCode.UnsupportedMediaType;
	public const int RequestedRangeNotSatisfiable = (int)System.Net.HttpStatusCode.RequestedRangeNotSatisfiable;
	public const int ExpectationFailed = (int)System.Net.HttpStatusCode.ExpectationFailed;
	public const int InternalServerError = (int)System.Net.HttpStatusCode.InternalServerError;
	public const int NotImplemented = (int)System.Net.HttpStatusCode.NotImplemented;
	public const int BadGateway = (int)System.Net.HttpStatusCode.BadGateway;
	public const int ServiceUnavailable = (int)System.Net.HttpStatusCode.ServiceUnavailable;
	public const int GatewayTimeout = (int)System.Net.HttpStatusCode.GatewayTimeout;
	public const int HttpVersionNotSupported = (int)System.Net.HttpStatusCode.HttpVersionNotSupported;
}
