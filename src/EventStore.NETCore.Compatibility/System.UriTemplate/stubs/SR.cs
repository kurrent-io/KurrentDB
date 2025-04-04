namespace System.ServiceModel {
	using System.Globalization;
	using System.Resources;
	using System.Threading;

	internal sealed class SR {
		internal const string BindUriTemplateToNullOrEmptyPathParam = "BindUriTemplateToNullOrEmptyPathParam";
		internal const string ObjectIsReadOnly = "ObjectIsReadOnly";
		internal const string UTAdditionalDefaultIsInvalid = "UTAdditionalDefaultIsInvalid";
		internal const string UTBadBaseAddress = "UTBadBaseAddress";
		internal const string UTBindByNameCalledWithEmptyKey = "UTBindByNameCalledWithEmptyKey";
		internal const string UTBindByPositionNoVariables = "UTBindByPositionNoVariables";
		internal const string UTBindByPositionWrongCount = "UTBindByPositionWrongCount";
		internal const string UTBothLiteralAndNameValueCollectionKey = "UTBothLiteralAndNameValueCollectionKey";
		internal const string UTCSRLookupBeforeMatch = "UTCSRLookupBeforeMatch";
		internal const string UTDefaultValuesAreImmutable = "UTDefaultValuesAreImmutable";
		internal const string UTDefaultValueToCompoundSegmentVar = "UTDefaultValueToCompoundSegmentVar";
		internal const string UTDefaultValueToCompoundSegmentVarFromAdditionalDefaults = "UTDefaultValueToCompoundSegmentVarFromAdditionalDefaults";
		internal const string UTDefaultValueToQueryVar = "UTDefaultValueToQueryVar";
		internal const string UTDefaultValueToQueryVarFromAdditionalDefaults = "UTDefaultValueToQueryVarFromAdditionalDefaults";
		internal const string UTDoesNotSupportAdjacentVarsInCompoundSegment = "UTDoesNotSupportAdjacentVarsInCompoundSegment";
		internal const string UTInvalidDefaultPathValue = "UTInvalidDefaultPathValue";
		internal const string UTInvalidFormatSegmentOrQueryPart = "UTInvalidFormatSegmentOrQueryPart";
		internal const string UTInvalidVarDeclaration = "UTInvalidVarDeclaration";
		internal const string UTInvalidWildcardInVariableOrLiteral = "UTInvalidWildcardInVariableOrLiteral";
		internal const string UTNullableDefaultAtAdditionalDefaults = "UTNullableDefaultAtAdditionalDefaults";
		internal const string UTNullableDefaultMustBeFollowedWithNullables = "UTNullableDefaultMustBeFollowedWithNullables";
		internal const string UTNullableDefaultMustNotBeFollowedWithLiteral = "UTNullableDefaultMustNotBeFollowedWithLiteral";
		internal const string UTNullableDefaultMustNotBeFollowedWithWildcard = "UTNullableDefaultMustNotBeFollowedWithWildcard";
		internal const string UTQueryCannotEndInAmpersand = "UTQueryCannotEndInAmpersand";
		internal const string UTQueryCannotHaveCompoundValue = "UTQueryCannotHaveCompoundValue";
		internal const string UTQueryCannotHaveEmptyName = "UTQueryCannotHaveEmptyName";
		internal const string UTQueryMustHaveLiteralNames = "UTQueryMustHaveLiteralNames";
		internal const string UTQueryNamesMustBeUnique = "UTQueryNamesMustBeUnique";
		internal const string UTStarVariableWithDefaults = "UTStarVariableWithDefaults";
		internal const string UTStarVariableWithDefaultsFromAdditionalDefaults = "UTStarVariableWithDefaultsFromAdditionalDefaults";
		internal const string UTTAmbiguousQueries = "UTTAmbiguousQueries";
		internal const string UTTBaseAddressMustBeAbsolute = "UTTBaseAddressMustBeAbsolute";
		internal const string UTTBaseAddressNotSet = "UTTBaseAddressNotSet";
		internal const string UTTCannotChangeBaseAddress = "UTTCannotChangeBaseAddress";
		internal const string UTTDuplicate = "UTTDuplicate";
		internal const string UTTEmptyKeyValuePairs = "UTTEmptyKeyValuePairs";
		internal const string UTTInvalidTemplateKey = "UTTInvalidTemplateKey";
		internal const string UTTMultipleMatches = "UTTMultipleMatches";
		internal const string UTTMustBeAbsolute = "UTTMustBeAbsolute";
		internal const string UTTNullTemplateKey = "UTTNullTemplateKey";
		internal const string UTTOtherAmbiguousQueries = "UTTOtherAmbiguousQueries";
		internal const string UTVarNamesMustBeUnique = "UTVarNamesMustBeUnique";

		private ResourceManager resources;
		private static System.ServiceModel.SR loader;

		internal SR() {
			this.resources = new ResourceManager("System.ServiceModel", base.GetType().Assembly);
		}

		private static System.ServiceModel.SR GetLoader() {
			if (loader == null) {
				System.ServiceModel.SR sr = new System.ServiceModel.SR();
				Interlocked.CompareExchange<System.ServiceModel.SR>(ref loader, sr, null);
			}
			return loader;
		}

		public static object GetObject(string name) {
			System.ServiceModel.SR loader = GetLoader();
			if (loader == null) {
				return null;
			}
			return loader.resources.GetObject(name, Culture);
		}

		public static string GetString(string name) {
			System.ServiceModel.SR loader = GetLoader();
			if (loader == null) {
				return null;
			}
			return loader.resources.GetString(name, Culture);
		}

		public static string GetString(string name, params object[] args) {
			System.ServiceModel.SR loader = GetLoader();
			if (loader == null) {
				return null;
			}
			string format = loader.resources.GetString(name, Culture);
			if ((args == null) || (args.Length <= 0)) {
				return format;
			}
			for (int i = 0; i < args.Length; i++) {
				string str2 = args[i] as string;
				if ((str2 != null) && (str2.Length > 0x400)) {
					args[i] = str2.Substring(0, 0x3fd) + "...";
				}
			}
			return string.Format(CultureInfo.CurrentCulture, format, args);
		}

		public static string GetString(string name, out bool usedFallback) {
			usedFallback = false;
			return GetString(name);
		}

		private static CultureInfo Culture {
			get {
				return null;
			}
		}

		public static ResourceManager Resources {
			get {
				return GetLoader().resources;
			}
		}
	}
}
