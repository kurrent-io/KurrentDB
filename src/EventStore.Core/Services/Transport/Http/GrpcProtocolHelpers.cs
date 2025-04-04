#pragma warning disable IDE0073 // The file header does not match the required text
// From https://github.com/grpc/grpc-dotnet

using System;
using System.Runtime.CompilerServices;
using System.Text;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;
using HttpResponse = Microsoft.AspNetCore.Http.HttpResponse;

namespace EventStore.Core.Services.Transport.Http;

public static class GrpcProtocolHelpers {
	internal const string GrpcContentType = "application/grpc";
	internal const string GrpcProtoContentType = "application/grpc+proto";
	internal const string GrpcWebContentType = "application/grpc-web";
	internal const string GrpcWebTextContentType = "application/grpc-web-text";
	internal const string RetryPushbackHeader = "grpc-retry-pushback-ms";

	internal static readonly string StatusTrailer = HeaderNames.GrpcStatus;
	internal static readonly string MessageTrailer = HeaderNames.GrpcMessage;

	public static IHeaderDictionary GetTrailersDestination(HttpResponse response) {
		if (response.HasStarted) {
			// The response has content so write trailers to a trailing HEADERS frame
			var feature = response.HttpContext.Features.Get<IHttpResponseTrailersFeature>();
			if (feature?.Trailers == null || feature.Trailers.IsReadOnly) {
				throw new InvalidOperationException("Trailers are not supported for this response. The server may not support gRPC.");
			}

			return feature.Trailers;
		} else {
			// The response is "Trailers-Only". There are no gRPC messages in the response so the status
			// and other trailers can be placed in the header HEADERS frame
			return response.Headers;
		}
	}

	public static void AddProtocolHeaders(HttpResponse response) {
		response.ContentType = GrpcContentType;
	}

	public static bool IsGrpc(this HttpContext context) =>
		context.Request.ContentType
			is GrpcContentType
			or GrpcProtoContentType
			or GrpcWebContentType
			or GrpcWebTextContentType;

	public static void SetStatus(IHeaderDictionary destination, Status status) {
		// Overwrite any previously set status
		destination[StatusTrailer] = status.StatusCode.ToTrailerString();

		string escapedDetail;
		if (!string.IsNullOrEmpty(status.Detail)) {
			// https://github.com/grpc/grpc/blob/master/doc/PROTOCOL-HTTP2.md#responses
			// The value portion of Status-Message is conceptually a Unicode string description of the error,
			// physically encoded as UTF-8 followed by percent-encoding.
			escapedDetail = PercentEncode(status.Detail);
		} else {
			escapedDetail = null;
		}

		destination[MessageTrailer] = escapedDetail;
	}

	private static readonly char[] HexChars = new[]
	{
		'0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F'
	};
	internal const int MaxUnicodeCharsReallocate = 40; // Maximum batch size when working with unicode characters
	private const int MaxUtf8BytesPerUnicodeChar = 4;
	private const int AsciiMaxValue = 127;

	// From https://github.com/grpc/grpc/blob/324189c9dc540f0693d79f02dcb8c5f9261b535e/src/core/lib/slice/percent_encoding.cc#L31
	private static readonly byte[] PercentEncodingUnreservedBitField =
	{
		0x00, 0x00, 0x00, 0x00, 0xdf, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
		0xff, 0xff, 0xff, 0xff, 0x7f, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
	};

	public static string PercentEncode(string value) {
		// Count the number of bytes needed to output this string
		var encodedLength = 0L;
		for (var i = 0; i < value.Length; i++) {
			var c = value[i];
			if (c > AsciiMaxValue) {
				// Get additional unicode characters
				var unicodeCharCount = GetCountOfNonAsciiUtf16CodeUnits(value, i, maxCount: int.MaxValue);

				var utf8ByteCount = Encoding.UTF8.GetByteCount(value.AsSpan(i, unicodeCharCount));
				encodedLength += (long)utf8ByteCount * 3;
				i += unicodeCharCount - 1;
			} else {
				encodedLength += IsUnreservedCharacter(c) ? 1 : 3;
			}
		}

		if (encodedLength > int.MaxValue) {
			throw new InvalidOperationException("Value is too large to encode.");
		}

		// Return the original string if no encoding is required
		if (value.Length == encodedLength) {
			return value;
		}

		// Encode
		return string.Create((int)encodedLength, value, Encode);

		static void Encode(Span<char> span, string s) {
			Span<byte> unicodeBytesBuffer = stackalloc byte[MaxUnicodeCharsReallocate * MaxUtf8BytesPerUnicodeChar];

			var writePosition = 0;
			for (var i = 0; i < s.Length; i++) {
				var current = s[i];
				if (current > AsciiMaxValue) {
					// Leave a character for possible low surrogate
					const int MaxCount = MaxUnicodeCharsReallocate - 1;

					// Get additional unicode characters
					var unicodeCharCount = GetCountOfNonAsciiUtf16CodeUnits(s, i, MaxCount);

					// Note that invalid UTF-16 data, e.g. unpaired surrogates, will be converted to EF BF BD (unicode replacement character)
					var numberOfBytes = Encoding.UTF8.GetBytes(s.AsSpan(i, unicodeCharCount), unicodeBytesBuffer);

					for (var count = 0; count < numberOfBytes; count++) {
						EscapeAsciiChar(span, ref writePosition, (char)unicodeBytesBuffer[count]);
					}
					i += unicodeCharCount - 1;
				} else if (IsUnreservedCharacter(current)) {
					span[writePosition++] = current;
				} else {
					EscapeAsciiChar(span, ref writePosition, current);
				}
			}
		}
	}

	private static void EscapeAsciiChar(Span<char> span, ref int writePosition, char current) {
		span[writePosition++] = '%';
		span[writePosition++] = HexChars[current >> 4];
		span[writePosition++] = HexChars[current & 15];
	}

	private static int GetCountOfNonAsciiUtf16CodeUnits(string value, int currentIndex, int maxCount) {
		// We know we have started with a UTF-16 character
		var unicodeCharCount = 1;

		var maxSize = Math.Min(value.Length - currentIndex, maxCount);
		for (; unicodeCharCount < maxSize && value[currentIndex + unicodeCharCount] > AsciiMaxValue; unicodeCharCount++) {
		}

		if (char.IsHighSurrogate(value[currentIndex + unicodeCharCount - 1])) {
			if (unicodeCharCount < value.Length - currentIndex && char.IsLowSurrogate(value[currentIndex + unicodeCharCount])) {
				// Last character is a high surrogate so check ahead to see if it is followed by a low surrogate and include
				unicodeCharCount++;
			}
		}

		return unicodeCharCount;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsUnreservedCharacter(char c) {
		return ((PercentEncodingUnreservedBitField[c / 8] >> (c % 8)) & 1) != 0;
	}

	public static string ToTrailerString(this StatusCode status) {
		switch (status) {
			case StatusCode.OK:
				return "0";
			case StatusCode.Cancelled:
				return "1";
			case StatusCode.Unknown:
				return "2";
			case StatusCode.InvalidArgument:
				return "3";
			case StatusCode.DeadlineExceeded:
				return "4";
			case StatusCode.NotFound:
				return "5";
			case StatusCode.AlreadyExists:
				return "6";
			case StatusCode.PermissionDenied:
				return "7";
			case StatusCode.ResourceExhausted:
				return "8";
			case StatusCode.FailedPrecondition:
				return "9";
			case StatusCode.Aborted:
				return "10";
			case StatusCode.OutOfRange:
				return "11";
			case StatusCode.Unimplemented:
				return "12";
			case StatusCode.Internal:
				return "13";
			case StatusCode.Unavailable:
				return "14";
			case StatusCode.DataLoss:
				return "15";
			case StatusCode.Unauthenticated:
				return "16";
			default:
				return status.ToString("D");
		}
	}
}
