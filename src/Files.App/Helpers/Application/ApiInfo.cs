// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.Foundation.Metadata;

namespace Files.App.Helpers.Application
{
	/// <summary>
	/// Provides API contract and property checks that work without package identity.
	/// </summary>
	public static class ApiInfo
	{
		public static bool IsApiContractPresent(string contractName, ushort majorVersion)
		{
#if FILES_PORTABLE
			// ApiInformation throws 0x80073D54 in unpackaged processes; portable builds target a fixed supported OS range.
			return true;
#else
			return ApiInfo.IsApiContractPresent(contractName, majorVersion);
#endif
		}

		public static bool IsPropertyPresent(string typeName, string propertyName)
		{
#if FILES_PORTABLE
			return true;
#else
			return ApiInfo.IsPropertyPresent(typeName, propertyName);
#endif
		}
	}
}
