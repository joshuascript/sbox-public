using System.Runtime.InteropServices;

namespace Sandbox;

internal static partial class DisplaySurface
{
	[DllImport( Library )] static extern bool SDL_SetWindowRelativeMouseMode( IntPtr window, bool enabled );
	[DllImport( Library )] static extern bool SDL_SetWindowMouseGrab( IntPtr window, bool grabbed );
	[DllImport( Library )] static extern bool SDL_SetWindowMouseRect( IntPtr window, ref SDL_Rect rect );
	[DllImport( Library )] static extern bool SDL_SetWindowMouseRect( IntPtr window, IntPtr rect );
	[DllImport( Library )] static extern void SDL_GetWindowSizeInPixels( IntPtr window, out int w, out int h );
	[DllImport( Library )] static extern void SDL_GetWindowSize( IntPtr window, out int w, out int h );
	[DllImport( Library )] static extern void SDL_WarpMouseInWindow( IntPtr window, float x, float y );
	[DllImport( Library )] static extern bool SDL_StartTextInput( IntPtr window );
	[DllImport( Library )] static extern bool SDL_StopTextInput( IntPtr window );

	[StructLayout( LayoutKind.Sequential )]
	struct SDL_Rect { public int x, y, w, h; }

	// SDL_SetWindowRelativeMouseMode → zwp_locked_pointer_v1 (pointer lock for camera rotation)
	// SDL_SetWindowMouseGrab        → zwp_confined_pointer_v1 / XGrabPointer (safety net on unlock transitions)
	// SDL_SetWindowMouseRect        → X11/XWayland only: server-side confinement barrier.
	//   On native Wayland, a non-empty mouse_rect causes SDL to switch from zwp_locked_pointer_v1
	//   (absolute lock, cannot escape) to zwp_confined_pointer_v1 (softer, can be escaped).
	//   Keep the rect empty on Wayland so SDL always uses the locked pointer.
	internal static void SetMouseGrab( bool grab )
	{
		if ( s_window == IntPtr.Zero ) return;
		SDL_SetWindowRelativeMouseMode( s_window, grab );
		SDL_SetWindowMouseGrab( s_window, grab );

		if ( grab && !s_isWayland )
		{
			SDL_GetWindowSizeInPixels( s_window, out int w, out int h );
			var rect = new SDL_Rect { x = 0, y = 0, w = w, h = h };
			SDL_SetWindowMouseRect( s_window, ref rect );
		}
		else
		{
			SDL_SetWindowMouseRect( s_window, IntPtr.Zero );
		}
	}

	// SDL_StartTextInput/StopTextInput → zwp_text_input_v3 (required for SDL_EVENT_TEXT_INPUT on Wayland)
	internal static void SetTextInput( bool active )
	{
		if ( s_window == IntPtr.Zero ) return;
		if ( active ) SDL_StartTextInput( s_window );
		else SDL_StopTextInput( s_window );
	}

	// Warps the cursor to the window centre. Called each frame during mouse capture on Wayland
	// as a software fallback when the compositor-level pointer lock (zwp_locked_pointer_v1) fails
	// to hold — keeps the cursor from drifting out of the window.
	internal static void WarpToCenter()
	{
		if ( s_window == IntPtr.Zero ) return;
		SDL_GetWindowSize( s_window, out int w, out int h );
		SDL_WarpMouseInWindow( s_window, w / 2f, h / 2f );
	}
}
