using Native;
using Sandbox.Engine;
using System;
using System.Runtime.InteropServices;

namespace Editor;

public static class Application
{
	private static IntPtr _x11Display;
	private static bool _triedX11Display;
	private static readonly byte[] _x11Keymap = new byte[32];

	[DllImport( "libX11.so.6", EntryPoint = "XOpenDisplay" )]
	private static extern IntPtr XOpenDisplay( IntPtr displayName );

	[DllImport( "libX11.so.6", EntryPoint = "XDefaultRootWindow" )]
	private static extern IntPtr XDefaultRootWindow( IntPtr display );

	[DllImport( "libX11.so.6", EntryPoint = "XQueryPointer" )]
	private static extern int XQueryPointer( IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int windowX, out int windowY, out uint mask );

	private static bool TryGetLinuxCursorPosition( out Vector2 position )
	{
		position = default;
		if ( !OperatingSystem.IsLinux() )
			return false;

		var display = GetX11Display();
		if ( display == default )
			return false;

		var rootWindow = XDefaultRootWindow( display );
		if ( XQueryPointer( display, rootWindow, out _, out _, out var rootX, out var rootY, out _, out _, out _ ) == 0 )
			return false;

		position = new Vector2( rootX, rootY );
		return true;
	}

	[DllImport( "libX11.so.6", EntryPoint = "XQueryKeymap" )]
	private static extern void XQueryKeymap( IntPtr display, [Out] byte[] keys );

	[DllImport( "libX11.so.6", EntryPoint = "XKeysymToKeycode" )]
	private static extern byte XKeysymToKeycode( IntPtr display, IntPtr keySym );

	private static IntPtr GetX11Display()
	{
		if ( _x11Display != default || _triedX11Display )
			return _x11Display;

		_triedX11Display = true;
		try
		{
			_x11Display = XOpenDisplay( default );
		}
		catch ( DllNotFoundException ) { }
		catch ( EntryPointNotFoundException ) { }
		catch ( BadImageFormatException ) { }

		return _x11Display;
	}

	private static bool TryGetLinuxKeyDown( KeyCode code, out bool down )
	{
		down = false;
		if ( !OperatingSystem.IsLinux() )
			return false;

		var key = (int)code;
		if ( key < 'A' || key > 'Z' )
			return false;

		var display = GetX11Display();
		if ( display == default )
			return false;

		var keyCode = XKeysymToKeycode( display, (IntPtr)key );
		if ( keyCode == 0 )
			return false;

		XQueryKeymap( display, _x11Keymap );

		down = (_x11Keymap[keyCode >> 3] & (1 << (keyCode & 7))) != 0;
		return true;
	}

	private static MouseButtons GetLinuxMouseButtons()
	{
		if ( !OperatingSystem.IsLinux() )
			return MouseButtons.None;

		var display = GetX11Display();
		if ( display == default )
			return MouseButtons.None;

		var rootWindow = XDefaultRootWindow( display );
		if ( XQueryPointer( display, rootWindow, out _, out _, out _, out _, out _, out _, out var mask ) == 0 )
			return MouseButtons.None;

		var buttons = MouseButtons.None;
		if ( (mask & (1u << 8)) != 0 ) buttons |= MouseButtons.Left;
		if ( (mask & (1u << 9)) != 0 ) buttons |= MouseButtons.Middle;
		if ( (mask & (1u << 10)) != 0 ) buttons |= MouseButtons.Right;
		return buttons;
	}

	/// <summary>
	/// Called when any widget is clicked. Can set MouseEvent.Accepted to true to prevent the Widget's OnMouseClick from firing.
	/// </summary>
	public static Action<Widget, MouseEvent> OnWidgetClicked { get; set; }

	/// <inheritdoc cref="Widget.SetStyles"/>.
	public static void SetStyles( string style )
	{
		Native.QApp.setStyleSheet( style );
	}

	internal static void ReloadStyles()
	{
		var sheet = "";

		foreach ( var file in FileSystem.Root.FindFile( "/addons/tools/assets/styles/", "*.css" ) )
		{
			var txt = FileSystem.Root.ReadAllText( $"/addons/tools/assets/styles/{file}" );
			txt = Theme.ParseVariables( txt );

			sheet += $"\n{txt}\n";
		}

		Native.QApp.setStyleSheet( sheet );
	}

	/// <summary>
	/// Will process all of the UI events - allowing the UI to stay responsive during a blocking call.
	/// </summary>
	public static void Spin()
	{
		g_pToolFramework2.Spin();
	}

	public static float DpiScale
	{
		get => Native.QApp.DpiPixelRatio();
	}

	/// <summary>
	/// Get/Set cursor position.
	/// </summary>
	public static Vector2 CursorPosition
	{
		get => TryGetLinuxCursorPosition( out var position )
			? position / DpiScale
			: (Vector2)Native.QApp.CursorPosition();
		set
		{
			Native.QApp.SetCursorPosition( value );
			lastCursorPos = UnscaledCursorPosition;
		}
	}

	/// <summary>
	/// The cursor position, not scaled for DPI
	/// </summary>
	public static Vector2 UnscaledCursorPosition
	{
		get => TryGetLinuxCursorPosition( out var position )
			? position
			: (Vector2)Native.QApp.NativeCursorPosition();
		set
		{
			Native.QApp.SetNativeCursorPosition( value );
			lastCursorPos = UnscaledCursorPosition;
		}
	}

	/// <summary>
	/// The cursor delta between this and previous frame.
	/// </summary>
	public static Vector2 CursorDelta
	{
		get; private set;
	}

	/// <summary>
	/// The mouse wheel delta between this and previous frame
	/// </summary>
	public static Vector2 MouseWheelDelta { get; private set; }

	static Vector2 lastCursorPos;
	internal static Vector2 accumulatedCursorDelta = Vector2.Zero;

	internal static void StartFrame()
	{
		// we scale the delta by dpi scale. This should give something close to 
		// the values they will be expecting, but will also provide fractions
		// which CursorPos does not.
		CursorDelta = (UnscaledCursorPosition - lastCursorPos) / DpiScale;

		// record this for next time around
		lastCursorPos = UnscaledCursorPosition;

		MouseWheelDelta = accumulatedCursorDelta;
		accumulatedCursorDelta = Vector2.Zero;
	}


	/// <summary>
	/// Returns which keyboard modified keys are held down right at this point.
	/// </summary>
	public static KeyboardModifiers KeyboardModifiers
	{
		get => QtHelpers.Translate( Native.QApp.queryKeyboardModifiers() );
	}

	/// <summary>
	/// Returns the current state of the mouse buttons.
	/// </summary>
	public static MouseButtons MouseButtons
	{
		get => Native.QApp.mouseButtons() | GetLinuxMouseButtons();
	}


	/// <summary>
	/// Returns whether or not a key is currently being held down.
	/// </summary>
	public static bool IsKeyDown( Editor.KeyCode code )
	{
		return TryGetLinuxKeyDown( code, out var down )
			? down
			: CQUtils.IsKeyPressed( (int)code );
	}

	/// <summary>
	/// Converts an editor keycode to a string used by the game
	/// Qt::Key -> WindowsVirtualKey -> ButtonCode_t -> string
	/// </summary>
	/// <param name="code"></param>
	/// <returns></returns>
	public static string KeyCodeToString( Editor.KeyCode code )
	{
		var virt = CQUtils.GetWindowsVirtualKey( (int)code );
		var buttonCode = NativeEngine.InputSystem.VirtualKeyToButtonCode( virt );
		var str = NativeEngine.InputSystem.CodeToString( buttonCode );
		return str;
	}

	/// <summary>
	/// The <see cref="Widget"/> that has the keyboard input focus, or <c>null</c>if no widget in this application has the focus.
	/// </summary>
	public static Widget FocusWidget => QObject.FindOrCreate( QApp.focusWidget() ) as Widget;


	/// <summary>
	/// The Widget that is currently hovered
	/// </summary>
	public static Widget HoveredWidget => QObject.FindOrCreate( QApp.HoveredWidget() ) as Widget;

	/// <summary>
	/// Get the current editor if any. Will return null if we're not in the editor, or there is no active editor session.
	/// </summary>
	public static EditorSystem Editor => IToolsDll.Current?.ActiveEditor;
}
