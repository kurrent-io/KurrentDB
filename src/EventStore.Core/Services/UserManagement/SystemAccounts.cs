﻿using System.Collections.Generic;
using System.Security.Claims;

namespace EventStore.Core.Services.UserManagement {
	public class SystemAccounts {
		private static readonly IReadOnlyList<Claim> Claims = new[] {
			new Claim(ClaimTypes.Name, "system"), 
			new Claim(ClaimTypes.Role, "system"), 
			new Claim(ClaimTypes.Role, SystemRoles.Admins), 
		};
		public static readonly ClaimsPrincipal System = new ClaimsPrincipal(new ClaimsIdentity(Claims, "system"));
		public static readonly ClaimsPrincipal Anonymous = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]{new Claim(ClaimTypes.Anonymous, ""), }));

		public static readonly string SystemIndexMergeName = "system-index-merge";
		public static readonly string SystemIndexScavengeName = "system-index-scavenge";
		public static readonly string SystemRedactionName = "system-redaction";
	}
}
