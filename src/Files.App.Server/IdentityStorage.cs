// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.ApplicationModel;
using Windows.Storage;

namespace Files.App.Server;

/// <summary>
/// Supplies the package values the server needs when it runs outside a package.
/// </summary>
internal static class IdentityStorage
{
	private static readonly Lazy<bool> hasIdentity = new(DetectIdentity);
	private static readonly Lazy<string> localFolder = new(ResolveLocalFolder);

	public static bool HasIdentity => hasIdentity.Value;    public static string Name => HasIdentity ? Package.Current.Id.Name : "OctaFilesDev";    public static string DisplayName => HasIdentity ? Package.Current.DisplayName : "Octa Files";    public static string FamilyName => HasIdentity ? Package.Current.Id.FamilyName : "OctaFilesDev_ykqwq8d6ps0ag";

	public static PackageVersion Version => HasIdentity
		? Package.Current.Id.Version
		: new PackageVersion
		{
			Major = (ushort)AssemblyVersion.Major,
			Minor = (ushort)AssemblyVersion.Minor,
			Build = (ushort)AssemblyVersion.Build,
			Revision = (ushort)Math.Max(0, AssemblyVersion.Revision),
		};

	public static string InstalledPath => HasIdentity
		? Package.Current.InstalledLocation.Path
		: Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

	public static string LocalFolderPath => localFolder.Value;

	private static Version AssemblyVersion => typeof(IdentityStorage).Assembly.GetName().Version ?? new Version(4, 3, 3, 0);

	private static bool DetectIdentity()
	{
		try
		{
			_ = Package.Current.Id.Name;
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
			return ApplicationData.Current.LocalFolder.Path;        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Octa Files");
		Directory.CreateDirectory(path);
		return path;
	}
}
