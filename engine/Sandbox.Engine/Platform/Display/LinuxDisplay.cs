using NativeEngine;
using Sandbox;

namespace Sandbox.Engine;

internal enum LinuxDisplayServer
{
	X11,
	XWayland,  // Wayland compositor session, running under XWayland (SDL_VIDEODRIVER=x11)
	Wayland,   // Native Wayland compositor session
}

[SkipHotload]
internal static class LinuxDisplay
{
	internal static LinuxDisplayServer Server { get; private set; }

	internal static void Detect()
	{
		var sdlDriver   = Environment.GetEnvironmentVariable( "SDL_VIDEODRIVER" );
		var sessionType = Environment.GetEnvironmentVariable( "XDG_SESSION_TYPE" );
		var waylandDisp = Environment.GetEnvironmentVariable( "WAYLAND_DISPLAY" );

		bool isWaylandSession = sessionType == "wayland" || waylandDisp != null;

		if ( sdlDriver == "x11" )
			Server = isWaylandSession ? LinuxDisplayServer.XWayland : LinuxDisplayServer.X11;
		else if ( sdlDriver == "wayland" || isWaylandSession )
			Server = LinuxDisplayServer.Wayland;
		else
			Server = LinuxDisplayServer.X11;

		Log.Info( $"[Linux] Display server: {Server} (SDL_VIDEODRIVER={sdlDriver ?? "unset"}, XDG_SESSION_TYPE={sessionType ?? "unset"}, WAYLAND_DISPLAY={waylandDisp ?? "unset"})" );
	}

	internal static void RegisterInputWindow()
	{
		try
		{
			var osHandle = DisplaySurface.FindGameWindow();
			if ( osHandle == IntPtr.Zero )
			{
				Log.Warning( "[Linux] Window handle not available yet — input may not work." );
				return;
			}

			InputSystem.RegisterWindowWithSDL( osHandle );
			InputSystem.SetEditorMainWindow( osHandle );
			InputSystem.OnEditorGameFocusChange( osHandle, true );

			Log.Info( $"[Linux] Input window registered (handle=0x{osHandle:x}, server={Server})" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Linux] Input window registration failed: {e.Message}" );
		}
	}
}
