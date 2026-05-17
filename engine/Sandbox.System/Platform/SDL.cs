using System.Runtime.InteropServices;

namespace Sandbox;

// SDL3 P/Invoke layer for window management and input.
// libtier0.so owns the SDL3 instance in this build; SDL_Window* is cached on first FindGameWindow() call.
[SkipHotload]
internal static class SDL
{
	private const string Library = "libtier0";

	[DllImport( Library )] static extern unsafe IntPtr* SDL_GetWindows( out int count );
	[DllImport( Library )] static extern uint SDL_GetWindowProperties( IntPtr window );
	[DllImport( Library )] static extern IntPtr SDL_GetPointerProperty( uint props, string name, IntPtr defaultValue );
	[DllImport( Library )] static extern long SDL_GetNumberProperty( uint props, string name, long defaultValue );
	[DllImport( Library )] static extern void SDL_free( IntPtr mem );
	[DllImport( Library )] static extern bool SDL_SetWindowRelativeMouseMode( IntPtr window, bool enabled );
	[DllImport( Library )] static extern bool SDL_SetWindowMouseGrab( IntPtr window, bool grabbed );
	[DllImport( Library )] static extern bool SDL_StartTextInput( IntPtr window );
	[DllImport( Library )] static extern bool SDL_StopTextInput( IntPtr window );

	static IntPtr s_window;

	// SDL_SetWindowRelativeMouseMode → zwp_locked_pointer_v1 (pointer lock for camera rotation)
	// SDL_SetWindowMouseGrab        → zwp_confined_pointer_v1 (safety net on unlock transitions)
	internal static void SetMouseGrab( bool grab )
	{
		if ( s_window == IntPtr.Zero ) return;
		SDL_SetWindowRelativeMouseMode( s_window, grab );
		SDL_SetWindowMouseGrab( s_window, grab );
	}

	// SDL_StartTextInput/StopTextInput → zwp_text_input_v3 (required for SDL_EVENT_TEXT_INPUT on Wayland)
	internal static void SetTextInput( bool active )
	{
		if ( s_window == IntPtr.Zero ) return;
		if ( active ) SDL_StartTextInput( s_window );
		else SDL_StopTextInput( s_window );
	}

	// Walks the SDL window list using platform property keys to locate the game window.
	// Caches the SDL_Window* in s_window for SetMouseGrab / SetTextInput.
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
			if ( osHandle == IntPtr.Zero )
			{
				// X11 / XWayland: XID stored as a number property
				long xid = SDL_GetNumberProperty( props, "SDL.window.x11.window", 0 );
				if ( xid != 0 ) osHandle = (IntPtr)xid;
			}

			if ( osHandle != IntPtr.Zero )
				s_window = windows[i];
		}

		SDL_free( (IntPtr)windows );
		return osHandle;
	}
}
