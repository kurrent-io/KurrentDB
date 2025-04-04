using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Plugins.Authentication;
using EventStore.Plugins.Authorization;
using EventStore.Transport.Http;
using EventStore.Transport.Http.Codecs;
using EventStore.Transport.Http.EntityManagement;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Transport.Http.Controllers {
	public class InfoController : IHttpController,
		IHandle<SystemMessage.StateChangeMessage> {
		private static readonly ILogger Log = Serilog.Log.ForContext<InfoController>();
		private static readonly ICodec[] SupportedCodecs = { Codec.Json, Codec.Xml, Codec.ApplicationXml, Codec.Text };

		private readonly ClusterVNodeOptions _options;
		private readonly IDictionary<string, bool> _features;
		private readonly IAuthenticationProvider _authenticationProvider;
		private VNodeState _currentState;

		public InfoController(ClusterVNodeOptions options, IDictionary<string, bool> features, IAuthenticationProvider authenticationProvider) {
			_options = options;
			_features = features;
			_authenticationProvider = authenticationProvider;
		}

		public void Subscribe(IHttpService service) {
			Ensure.NotNull(service, "service");
			service.RegisterAction(new ControllerAction("/info", HttpMethod.Get, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Node.Information.Read)),
				OnGetInfo);
			service.RegisterAction(
				new ControllerAction("/info/options", HttpMethod.Get, Codec.NoCodecs, SupportedCodecs, new Operation(Operations.Node.Information.Options)), OnGetOptions);
		}


		public void Handle(SystemMessage.StateChangeMessage message) {
			_currentState = message.State;
		}

		private void OnGetInfo(HttpEntityManager entity, UriTemplateMatch match) {
			entity.ReplyTextContent(Codec.Json.To(new {
				ESVersion = VersionInfo.Version,
				State = _currentState.ToString().ToLower(),
				Features = _features,
				Authentication = GetAuthenticationInfo()
			}),
				HttpStatusCode.OK,
				"OK",
				entity.ResponseCodec.ContentType,
				null,
				e => Log.Error(e, "Error while writing HTTP response (info)"));
		}

		private Dictionary<string, object> GetAuthenticationInfo() {
			if (_authenticationProvider == null)
				return null;

			return new Dictionary<string, object>(){
				{ "type", _authenticationProvider.Name },
				{ "properties", _authenticationProvider.GetPublicProperties() }
			};
		}

		private void OnGetOptions(HttpEntityManager entity, UriTemplateMatch match) {
			if (entity.User != null && (entity.User.LegacyRoleCheck(SystemRoles.Operations) || entity.User.LegacyRoleCheck(SystemRoles.Admins))) {
				var options = _options.GetPrintableOptions()?.Select(x => new OptionStructure {
					Name = x.Name,
					Description = x.Description,
					Group = x.Group,
					PossibleValues = x.AllowedValues,
					Value = x.Value
				});
				entity.ReplyTextContent(Codec.Json.To(options),
					HttpStatusCode.OK,
					"OK",
					entity.ResponseCodec.ContentType,
					null,
					e => Log.Error(e, "error while writing HTTP response (options)"));
			} else {
				entity.ReplyStatus(HttpStatusCode.Unauthorized, "Unauthorized", LogReplyError);
			}
		}

		private void LogReplyError(Exception exc) {
			Log.Debug("Error while replying (info controller): {e}.", exc.Message);
		}

		public class OptionStructure {
			public string Name { get; set; }
			public string Description { get; set; }
			public string Group { get; set; }
			public string Value { get; set; }
			public string[] PossibleValues { get; set; }
		}
	}
}
