// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace Files.App.Helpers.Application;

/// <summary>
/// Carries settings, registry databases, and app data over from the previous app identity.
/// </summary>
internal static class AppDataMigration
{
	private const string MarkerFileName = ".octa-files-migrated";

	private const string RegistryRoot = @"Software\Files Community";

	private const string ProductName = "OctaFilesDev";

	private const string LegacyProductName = "FilesDev";

	private static readonly string[] SkippedFolderNames = ["Cache", "EBWebView", "WebView2", "Crashpad"];

	internal static void Run()
	{
		try
		{
			MigrateRegistry();
			MigrateFolder(GetLegacyLocalFolderPath(), AppStorage.LocalFolderPath);
			MigrateFolder(GetLegacyRoamingFolderPath(), AppStorage.RoamingFolderPath);
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"Could not migrate app data: {ex}");
		}
	}

	private static string GetLegacyLocalFolderPath() => AppStorage.HasIdentity
		? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", AppPackageInfo.LegacyFamilyName, "LocalState")
		: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPackageInfo.LegacyLocalFolderName);

	private static string GetLegacyRoamingFolderPath() => AppStorage.HasIdentity
		? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", AppPackageInfo.LegacyFamilyName, "RoamingState")
		: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppPackageInfo.LegacyLocalFolderName);

	private static void MigrateFolder(string source, string destination)
	{
		if (!Directory.Exists(source))
			return;

		var marker = Path.Combine(destination, MarkerFileName);
		if (File.Exists(marker))
			return;

		Directory.CreateDirectory(destination);

		foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
		{
			var relative = Path.GetRelativePath(source, directory);
			if (IsSkipped(relative))
				continue;

			Directory.CreateDirectory(Path.Combine(destination, relative));
		}

		foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
		{
			var relative = Path.GetRelativePath(source, file);
			if (IsSkipped(relative))
				continue;

			try
			{
				var target = Path.Combine(destination, relative);
				Directory.CreateDirectory(Path.GetDirectoryName(target)!);
				File.Copy(file, target, true);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Could not migrate {file}: {ex}");
			}
		}

		try
		{
			File.WriteAllText(marker, string.Empty);
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"Could not write the migration marker: {ex}");
		}
	}

	private static bool IsSkipped(string relativePath)
		=> relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
			.Any(segment => SkippedFolderNames.Contains(segment, StringComparer.OrdinalIgnoreCase));

	private static void MigrateRegistry()
	{
		using var current = Registry.CurrentUser.OpenSubKey($@"{RegistryRoot}\{ProductName}");
		if (current is not null)
			return;

		using var legacy = Registry.CurrentUser.OpenSubKey($@"{RegistryRoot}\{LegacyProductName}");
		if (legacy is null)
			return;

		CopyKey(legacy, Registry.CurrentUser.CreateSubKey($@"{RegistryRoot}\{ProductName}"));
	}

	private static void CopyKey(RegistryKey source, RegistryKey? destination)
	{
		if (destination is null)
			return;

		using (destination)
		{
			foreach (var valueName in source.GetValueNames())
			{					try
					{
						var value = source.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
						if (value is not null)
							destination.SetValue(valueName, value, source.GetValueKind(valueName));
					}
				catch (Exception ex)
				{
					Debug.WriteLine($"Could not migrate the registry value {valueName}: {ex}");
				}
			}

			foreach (var subKeyName in source.GetSubKeyNames())
			{
				using var sourceSubKey = source.OpenSubKey(subKeyName);
				if (sourceSubKey is null)
					continue;

				CopyKey(sourceSubKey, destination.CreateSubKey(subKeyName));
			}
		}
	}
}
