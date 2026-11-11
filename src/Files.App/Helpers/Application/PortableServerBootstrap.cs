// Copyright (c) Files Community
// Licensed under the MIT License.

#if FILES_PORTABLE

using Files.App.Server;

namespace Files.App.Helpers.Application;

internal static class PortableServerBootstrap
{
	private static readonly Lock gate = new();
	private static bool started;

	public static void Start()
	{
		lock (gate)
		{
			if (started)
				return;

			started = true;
			InProcessHost.Start();
		}
	}
}

#endif
