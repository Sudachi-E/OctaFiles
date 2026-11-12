// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Windows.Storage;
using FileAttributes = System.IO.FileAttributes;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Persists a snapshot of the tree below a folder so recursive filtering can match names without walking the disk again.
	/// </summary>
	internal static class FolderIndex
	{
		private const int FormatVersion = 1;
		private const int MaxEntryCount = 200_000;
		private const int MaxMatchCount = 20_000;
		private const int MaxCachedRoots = 8;
		private const string IndexFolderName = "FolderIndex";

		private static readonly TimeSpan MaxIndexAge = TimeSpan.FromMinutes(10);

		private static readonly EnumerationOptions enumerationOptions = new()
		{
			RecurseSubdirectories = false,
			IgnoreInaccessible = true,
			AttributesToSkip = 0,
			ReturnSpecialDirectories = false,
		};

		private static readonly Lock gate = new();
		private static readonly Dictionary<string, IndexSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);
		private static readonly HashSet<string> buildsInProgress = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Raised when the index of a folder starts building, finishes, or is loaded, with the indexed root as the argument.
		/// </summary>
		public static event EventHandler<string>? StatusChanged;

		private sealed class IndexSnapshot(string root, long rootWriteTicks, DateTime builtAtUtc, bool isComplete, List<IndexEntry> entries)
		{
			public string Root { get; } = root;

			public long RootWriteTicks { get; } = rootWriteTicks;

			public DateTime BuiltAtUtc { get; } = builtAtUtc;

			/// <summary>
			/// A snapshot that hit <see cref="MaxEntryCount"/> cannot answer filters, because matches may be missing.
			/// </summary>
			public bool IsComplete { get; } = isComplete;

			public List<IndexEntry> Entries { get; } = entries;
		}

		private readonly record struct IndexEntry(string Path, string Name, bool IsFolder, bool IsHidden, bool IsSystem, long Size, long ModifiedTicks, long CreatedTicks);

		/// <summary>
		/// Returns the entries of <paramref name="root"/> matching <paramref name="filter"/>, or false when no usable index exists.
		/// A missing or stale index starts a background build for the next query.
		/// </summary>
		public static bool TryGetMatches(string? root, string filter, out List<ListedItem> matches)
		{
			matches = [];

			if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(filter) || !CanIndex(root))
				return false;

			var snapshot = GetSnapshot(root);
			if (snapshot is null || !snapshot.IsComplete || !IsFresh(snapshot))
			{
				EnsureIndexAsync(root);
				return false;
			}

			var foldersSettings = Ioc.Default.GetRequiredService<IUserSettingsService>().FoldersSettingsService;
			var folderTypeText = Strings.Folder.GetLocalizedResource();
			var results = new List<ListedItem>();

			foreach (var entry in snapshot.Entries)
			{
				if (!entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
					continue;

				if (!ShouldBeListed(entry, foldersSettings.ShowHiddenItems, foldersSettings.ShowProtectedSystemFiles, foldersSettings.ShowDotFiles))
					continue;

				results.Add(CreateListedItem(entry, folderTypeText));

				if (results.Count >= MaxMatchCount)
					break;
			}

			matches = results;
			return true;
		}

		/// <summary>
		/// Builds the index for <paramref name="root"/> in the background when one is missing or stale. Repeated calls are ignored.
		/// </summary>
		public static void EnsureIndexAsync(string? root)
		{
			if (string.IsNullOrEmpty(root) || !CanIndex(root))
				return;

			lock (gate)
			{
				if (!buildsInProgress.Add(root))
					return;
			}

			RaiseStatusChanged(root);

			_ = Task.Run(() =>
			{
				try
				{
					Build(root);
				}
				catch (Exception ex)
				{
					App.Logger.LogWarning(ex, "Failed to index the folder '{Folder}'", root);
				}
				finally
				{
					lock (gate)
						buildsInProgress.Remove(root);
				}
			});
		}

		private static void Build(string root)
		{
			var rootWriteTicks = Directory.GetLastWriteTimeUtc(root).Ticks;
			var entries = new List<IndexEntry>();
			var pendingFolders = new Queue<string>();
			pendingFolders.Enqueue(root);

			while (pendingFolders.TryDequeue(out var folder) && entries.Count < MaxEntryCount)
			{
				FileSystemInfo[] items;
				try
				{
					items = new DirectoryInfo(folder).GetFileSystemInfos("*", enumerationOptions);
				}
				catch (Exception)
				{
					continue;
				}

				foreach (var item in items)
				{
					try
					{
						var attributes = item.Attributes;
						if (attributes == (FileAttributes)(-1))
							continue;

						var isFolder = attributes.HasFlag(FileAttributes.Directory);
						var size = !isFolder && item is FileInfo file ? file.Length : 0;

						entries.Add(new IndexEntry(
							item.FullName,
							item.Name,
							isFolder,
							attributes.HasFlag(FileAttributes.Hidden),
							attributes.HasFlag(FileAttributes.System),
							size,
							item.LastWriteTimeUtc.Ticks,
							item.CreationTimeUtc.Ticks));

						// Junctions, symlinks and mount points would make the walk loop or leave the tree
						if (isFolder && !attributes.HasFlag(FileAttributes.ReparsePoint))
							pendingFolders.Enqueue(item.FullName);
					}
					catch (Exception)
					{
						// A single item that disappears mid-walk must not abort the index
					}
				}
			}

			WriteSnapshot(root, rootWriteTicks, entries);
		}

		private static IndexSnapshot? GetSnapshot(string root)
		{
			lock (gate)
			{
				if (snapshots.TryGetValue(root, out var cached))
					return cached;
			}

			var snapshot = ReadSnapshot(root);

			if (snapshot is not null)
			{
				lock (gate)
					Cache(root, snapshot);

				RaiseStatusChanged(root);
			}

			return snapshot;
		}

		private static void Cache(string root, IndexSnapshot snapshot)
		{
			if (snapshots.Count >= MaxCachedRoots && !snapshots.ContainsKey(root))
			{
				var oldestRoot = snapshots.MinBy(x => x.Value.BuiltAtUtc).Key;
				if (oldestRoot is not null)
					snapshots.Remove(oldestRoot);
			}

			snapshots[root] = snapshot;
		}

		private static bool IsFresh(IndexSnapshot snapshot)
		{
			if (DateTime.UtcNow - snapshot.BuiltAtUtc > MaxIndexAge)
				return false;

			try
			{
				return Directory.GetLastWriteTimeUtc(snapshot.Root).Ticks == snapshot.RootWriteTicks;
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static void WriteSnapshot(string root, long rootWriteTicks, List<IndexEntry> entries)
		{
			var path = GetIndexPath(root);
			var temporaryPath = $"{path}.tmp";

			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);

				using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
				using (var writer = new BinaryWriter(stream, Encoding.UTF8))
				{
					writer.Write(FormatVersion);
					writer.Write(rootWriteTicks);
					writer.Write(entries.Count < MaxEntryCount);
					writer.Write(entries.Count);

					foreach (var entry in entries)
					{
						writer.Write(entry.Path);
						writer.Write((byte)(
							(entry.IsFolder ? 1 : 0) |
							(entry.IsHidden ? 2 : 0) |
							(entry.IsSystem ? 4 : 0)));
						writer.Write(entry.Size);
						writer.Write(entry.ModifiedTicks);
						writer.Write(entry.CreatedTicks);
					}
				}

				File.Move(temporaryPath, path, true);
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Failed to save the folder index for '{Folder}'", root);

				try
				{
					File.Delete(temporaryPath);
				}
				catch (Exception)
				{
				}

				return;
			}

			lock (gate)
				Cache(root, new IndexSnapshot(root, rootWriteTicks, DateTime.UtcNow, entries.Count < MaxEntryCount, entries));

			RaiseStatusChanged(root);
		}

		/// <summary>
		/// Describes the index amount of <paramref name="root" />, or null when the folder cannot be indexed or has no index in memory yet.
		/// </summary>
		public static string? GetStatusText(string? root)
		{
			if (string.IsNullOrEmpty(root) || !CanIndex(root))
				return null;

			IndexSnapshot? snapshot;

			lock (gate)
			{
				if (buildsInProgress.Contains(root))
					return Strings.FolderIndexBuilding.GetLocalizedResource();

				snapshot = snapshots.TryGetValue(root, out var cached) ? cached : null;
			}

			return snapshot is null
				? null
				: string.Format(Strings.FolderIndexItemCount.GetLocalizedResource(), snapshot.Entries.Count);
		}

		private static void RaiseStatusChanged(string root)
			=> StatusChanged?.Invoke(null, root);

		private static IndexSnapshot? ReadSnapshot(string root)
		{
			var path = GetIndexPath(root);

			try
			{
				if (!File.Exists(path))
					return null;

				using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
				using var reader = new BinaryReader(stream, Encoding.UTF8);

				if (reader.ReadInt32() != FormatVersion)
					return null;

				var rootWriteTicks = reader.ReadInt64();
				var isComplete = reader.ReadBoolean();
				var count = reader.ReadInt32();

				if (count < 0 || count > MaxEntryCount)
					return null;

				var entries = new List<IndexEntry>(count);

				for (var i = 0; i < count; i++)
				{
					var entryPath = reader.ReadString();
					var flags = reader.ReadByte();
					var size = reader.ReadInt64();
					var modifiedTicks = reader.ReadInt64();
					var createdTicks = reader.ReadInt64();

					entries.Add(new IndexEntry(
						entryPath,
						Path.GetFileName(entryPath),
						(flags & 1) != 0,
						(flags & 2) != 0,
						(flags & 4) != 0,
						size,
						modifiedTicks,
						createdTicks));
				}

				return new IndexSnapshot(root, rootWriteTicks, File.GetLastWriteTimeUtc(path), isComplete, entries);
			}
			catch (Exception)
			{
				// A truncated or outdated file is rebuilt on the next query
				return null;
			}
		}

		private static string GetIndexPath(string root)
		{
			var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..32];

			return Path.Combine(AppStorage.LocalCacheFolderPath, IndexFolderName, $"{hash}.bin");
		}

		private static bool CanIndex(string root)
		{
			if (!Path.IsPathRooted(root) || DriveHelpers.IsNetworkPath(root))
				return false;

			try
			{
				var drive = new DriveInfo(Path.GetPathRoot(root)!);

				return drive.DriveType is System.IO.DriveType.Fixed or System.IO.DriveType.Removable
					&& drive.DriveFormat is "NTFS" or "ReFS";
			}
			catch (Exception)
			{
				return false;
			}
		}

		private static bool ShouldBeListed(IndexEntry entry, bool showHiddenItems, bool showProtectedSystemFiles, bool showDotFiles)
		{
			if (entry.Name.StartsWith(".", StringComparison.Ordinal) && !showDotFiles)
				return false;

			return !entry.IsHidden || (showHiddenItems && (!entry.IsSystem || showProtectedSystemFiles));
		}

		private static ListedItem CreateListedItem(IndexEntry entry, string folderTypeText)
		{
			var item = new ListedItem(null)
			{
				PrimaryItemAttribute = entry.IsFolder ? StorageItemTypes.Folder : StorageItemTypes.File,
				ItemNameRaw = entry.Name,
				ItemPath = entry.Path,
				ItemDateModifiedReal = ToDateTimeOffset(entry.ModifiedTicks),
				ItemDateCreatedReal = ToDateTimeOffset(entry.CreatedTicks),
				IsHiddenItem = entry.IsHidden,
				LoadFileIcon = false,
				Opacity = entry.IsHidden ? Constants.UI.DimItemOpacity : 1,
				ItemType = folderTypeText,
			};

			if (!entry.IsFolder)
			{
				var extension = Path.GetExtension(entry.Name);
				var hasExtension = !string.IsNullOrEmpty(extension);

				item.FileExtension = hasExtension ? extension : null;
				item.ItemType = hasExtension ? extension.Trim('.') : null;
				item.FileSize = entry.Size.ToSizeString();
				item.FileSizeBytes = entry.Size;
			}

			return item;
		}

		private static DateTimeOffset ToDateTimeOffset(long utcTicks)
			=> new(DateTime.SpecifyKind(new DateTime(utcTicks), DateTimeKind.Utc).ToLocalTime());
	}
}
