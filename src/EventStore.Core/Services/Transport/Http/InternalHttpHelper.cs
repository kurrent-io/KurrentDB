using System.Threading.Tasks;
using EventStore.Transport.Http.EntityManagement;
using Microsoft.AspNetCore.Http;

namespace EventStore.Core.Services.Transport.Http {
	public static class InternalHttpHelper {
		public static bool TryGetInternalContext(HttpContext context, out HttpEntityManager manager, out UriToActionMatch match, out TaskCompletionSource<bool> tcs) {
			manager = null;
			match = null;
			tcs = null;
			return context.Items.TryGetValue(typeof(HttpEntityManager), out var untypedManager) &&
				   context.Items.TryGetValue(typeof(UriToActionMatch), out var untypedMatch) &&
				   context.Items.TryGetValue(typeof(TaskCompletionSource<bool>), out var untypedTcs) &&
				   (manager = untypedManager as HttpEntityManager) != null &&
				   (match = untypedMatch as UriToActionMatch) != null &&
				   (tcs = untypedTcs as TaskCompletionSource<bool>) != null;
		}
	}
}
