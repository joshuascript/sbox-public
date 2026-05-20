using System.Runtime.InteropServices;

namespace Sandbox;

// SDL3 P/Invoke layer for window discovery.
// libtier0.so owns the SDL3 instance in this build; SDL_Window* is cached on first FindGameWindow() call.
[SkipHotload]
internal static partial class DisplaySurface
{
	private const string Library = "libtier0";

	[DllImport( Library )] static extern unsafe IntPtr* SDL_GetWindows( out int count );
	[DllImport( Library )] static extern uint SDL_GetWindowProperties( IntPtr window );
	[DllImport( Library )] static extern IntPtr SDL_GetPointerProperty( uint props, string name, IntPtr defaultValue );
	[DllImport( Library )] static extern long SDL_GetNumberProperty( uint props, string name, long defaultValue );
	[DllImport( Library )] static extern void SDL_free( IntPtr mem );

	static IntPtr s_window;
	static bool s_isWayland;

	// Walks the SDL window list using platform property keys to locate the game window.
	// Caches the SDL_Window* in s_window for input methods.
	// Returns the OS-level window handle (wl_surface* on Wayland, XID on X11), or Zero if not found.
	internal static unsafe IntPtr FindGameWindow()
	{
		int count = 0;
		IntPtr* windows = SDL_GetWindows( out count );
		if ( windows == null ) return IntPtr.Zero;

		IntPtr osHandle = IntPtr.Zero;
		for ( int i = 0; i < count && osHandle == IntPtr.Zero; i++ )
		{
			if ( windows[i] == IntPtr.Zero ) continue;
			uint props = SDL_GetWindowProperties( windows[i] );
			if ( props == 0 ) continue;

			// Wayland: wl_surface*
			osHandle = SDL_GetPointerProperty( props, "SDL.window.wayland.surface", IntPtr.Zero );
			if ( osHandle != IntPtr.Zero )
			{
				s_window = windows[i];
				s_isWayland = true;
			}
			else
			{
				// X11 / XWayland: XID stored as a number property
				long xid = SDL_GetNumberProperty( props, "SDL.window.x11.window", 0 );
				if ( xid != 0 )
				{
					osHandle = (IntPtr)xid;
					s_window = windows[i];
					s_isWayland = false;
				}
			}
		}

		SDL_free( (IntPtr)windows );
		return osHandle;
	}
}
