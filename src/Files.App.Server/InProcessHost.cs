// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.Win32;
using Windows.Win32.System.WinRT;

namespace Files.App.Server;

/// <summary>
/// Hosts the app services in a client process, which unpackaged clients require.
/// </summary>
internal static class InProcessHost
{
	private static readonly Lock gate = new();
	private static bool started;

	internal static void Start()
	{
		lock (gate)
		{
			if (started)
				return;

			started = true;
			_ = PInvoke.RoInitialize(RO_INIT_TYPE.RO_INIT_MULTITHREADED);
			ServerHost.Initialize(inProcess: true);
		}
	}
}
