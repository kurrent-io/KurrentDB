using System.Security.Claims;

namespace EventStore.Common.Utils {
	public static class ClaimsPrincipalExtensions {
		public static bool LegacyRoleCheck(this ClaimsPrincipal user, string role) {
			return user.HasClaim(ClaimTypes.Name, role) || user.HasClaim(ClaimTypes.Role, role);
		}
	}
}
