// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;

namespace Files.App.Helpers.Application;

/// <summary>
/// Supplies the application identity values that used to come from the package.
/// </summary>
internal static class AppPackageInfo
{
	/// <summary>
	/// Name of the application data folder used when the process has no package identity.
	/// </summary>
	public const string LocalFolderName = "Octa Files";

	public const string LegacyLocalFolderName = "Files";

	public const string LegacyName = "FilesDev";

	public const string LegacyFamilyName = "FilesDev_ykqwq8d6ps0ag";

	public static string Name => "OctaFilesDev";

	public static string DisplayName => "Octa Files";

	public static string FamilyName => "OctaFilesDev_ykqwq8d6ps0ag";

	public static Version Version { get; } = typeof(AppPackageInfo).Assembly.GetName().Version ?? new Version(4, 3, 3, 0);

	public static string InstalledPath { get; } = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
}
