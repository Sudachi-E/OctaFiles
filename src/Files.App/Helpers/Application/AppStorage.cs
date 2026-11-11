// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Text.Json;
using Windows.Storage;

using System.IO;

namespace Files.App.Helpers.Application;

/// <summary>
/// Provides application data paths when the process has no package identity.
/// </summary>
internal static class AppStorage
{
	private static readonly Lazy<string> localFolder = new(ResolveLocalFolder);
	private static readonly Lazy<string> roamingFolder = new(ResolveRoamingFolder);
	private static readonly Lazy<string> localCacheFolder = new(ResolveLocalCacheFolder);

	public static bool HasIdentity { get; } = DetectIdentity();

	public static string LocalFolderPath => localFolder.Value;

	public static string RoamingFolderPath => roamingFolder.Value;

	public static string LocalCacheFolderPath => localCacheFolder.Value;    public static string TemporaryFolderPath { get; } = Path.Combine(Path.GetTempPath(), AppPackageInfo.LocalFolderName);

	public static SettingsValues LocalSettings { get; } = new();

	private static bool DetectIdentity()
	{
		try
		{
			_ = global::Windows.ApplicationModel.Package.Current.Id.Name;
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static string ResolveLocalFolder()
	{
		if (HasIdentity)
			return ApplicationData.Current.LocalFolder.Path;        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPackageInfo.LocalFolderName);
		Directory.CreateDirectory(path);
		return path;
	}

	private static string ResolveRoamingFolder()
	{
		if (HasIdentity)
			return ApplicationData.Current.RoamingFolder.Path;        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppPackageInfo.LocalFolderName);
		Directory.CreateDirectory(path);
		return path;
	}

	private static string ResolveLocalCacheFolder()
	{
		if (HasIdentity)
			return ApplicationData.Current.LocalCacheFolder.Path;        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPackageInfo.LocalFolderName, "Cache");
		Directory.CreateDirectory(path);
		return path;
	}
}

/// <summary>
/// Settings container with the same shape as the one the package provided.
/// </summary>
internal sealed class SettingsValues
{
	private readonly Dictionary<string, object?> values = new(StringComparer.Ordinal);
	private readonly Lock gate = new();
	private readonly string? path;

	public SettingsValues()
	{
		if (!AppStorage.HasIdentity)
		{
			path = Path.Combine(AppStorage.LocalFolderPath, "settings.json");
			Load();
		}
	}

	public SettingsValues Values => this;

	public object? this[string key]
	{
		get
		{
			lock (gate)
				return values.TryGetValue(key, out var value) ? value : null;
		}
		set
		{
			lock (gate)
			{
				values[key] = value;
				Save();
			}
		}
	}

	public bool TryGetValue(string key, out object? value)
	{
		lock (gate)
			return values.TryGetValue(key, out value);
	}

	public bool ContainsKey(string key)
	{
		lock (gate)
			return values.ContainsKey(key);
	}

	public void Remove(string key)
	{
		lock (gate)
		{
			if (values.Remove(key))
				Save();
		}
	}

	public T Get<T>(string key, T defaultValue)
	{
		lock (gate)
			return values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue;
	}

	private void Load()
	{
		try
		{
			if (path is null || !File.Exists(path))
				return;

			var stored = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path));
			if (stored is null)
				return;

			foreach (var (key, element) in stored)
			{
				values[key] = element.ValueKind switch
				{
					JsonValueKind.True => true,
					JsonValueKind.False => false,
					JsonValueKind.String => element.GetString(),
					JsonValueKind.Number => element.TryGetInt64(out var number) ? number : element.GetDouble(),
					_ => null,
				};
			}
		}
		catch
		{
			// A damaged settings file must not prevent startup.
		}
	}

	private void Save()
	{
		if (path is null)
			return;

		try
		{
			File.WriteAllText(path, JsonSerializer.Serialize(values));
		}
		catch
		{
			// Settings are best effort outside the package.
		}
	}
}
