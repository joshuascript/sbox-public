using Sandbox.Engine.Settings;
using System;
using System.Runtime.InteropServices;

namespace Editor;

/// <summary>
/// Render a scene to a native widget. This replaces NativeRenderingWidget. 
/// </summary>
public class SceneRenderingWidget : Frame
{
	private static readonly HashSet<SceneRenderingWidget> All = new();

	internal SwapChainHandle_t SwapChain;
	private bool _ownsSwapChain;
	private bool _registeredWithSdl;
	private IntPtr _registeredWindowId;
	private IntPtr _swapChainWindowId;
	private static bool _creatingLinuxSwapChain;
	private static bool _allowLinuxSwapChainCreate;
	private Vector2 _swapChainPixelSize;
	private int _viewportDebugFrame;
	private int _paintOverrideFrame;
	private bool _loggedMissingLinuxSwapChain;
	private bool _loggedLinuxSwapChain;
	private bool _boundEngineState;
	private bool _loggedLinuxRender;
	private bool _loggedRenderEntry;
	private static IntPtr _tier0;
	private static bool _triedTier0;
	private static IntPtr _renderDevice;
	private static bool _triedRenderDevice;
	private static bool _triedDisableAsyncPresent;
	private static IntPtr _registerSdlWindowXid;
	private static bool _triedRegisterSdlWindowXid;
	private static IntPtr _sdlSetWindowSize;
	private static bool _triedSdlSetWindowSize;
	private IntPtr _x11NoBackgroundWindow;
	private static IntPtr _x11Display;
	private static bool _triedX11Display;

	private MouseButtons _mouseButtons;
	private Vector2 _mouseWheelDelta;
	private Vector2 _mouseDelta;
	private Vector2? _lastMousePosition;
	private Vector2? _lastPolledMousePosition;

	public MouseButtons MouseButtons => Application.MouseButtons | _mouseButtons;

	public Vector2 ConsumeMouseWheelDelta()
	{
		var delta = _mouseWheelDelta;
		_mouseWheelDelta = default;
		return delta;
	}

	public Vector2 ConsumeMouseDelta()
	{
		var delta = _mouseDelta;
		_mouseDelta = default;
		return delta;
	}

	public Vector2 ConsumePolledMouseDelta( Vector2 localPosition, bool tracking )
	{
		if ( !tracking )
		{
			_lastPolledMousePosition = null;
			return default;
		}

		var lastMousePosition = _lastPolledMousePosition;
		_lastPolledMousePosition = localPosition;
		return lastMousePosition.HasValue ? localPosition - lastMousePosition.Value : default;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		_mouseButtons = e.ButtonState | e.Button;
		_mouseDelta = default;
		_lastMousePosition = e.LocalPosition;
		_lastPolledMousePosition = e.LocalPosition;
		Focus();
		base.OnMousePress( e );
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		_mouseButtons = e.ButtonState;
		if ( _mouseButtons == MouseButtons.None )
		{
			_mouseDelta = default;
			_lastMousePosition = null;
			_lastPolledMousePosition = null;
		}
		base.OnMouseReleased( e );
	}

	protected override void OnMouseLeave()
	{
		_mouseDelta = default;
		_mouseButtons = MouseButtons.None;
		_lastMousePosition = null;
		_lastPolledMousePosition = null;
		base.OnMouseLeave();
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		var buttons = e.ButtonState | _mouseButtons | Application.MouseButtons;
		if ( buttons == MouseButtons.None )
		{
			_lastMousePosition = null;
			base.OnMouseMove( e );
			return;
		}

		var lastMousePosition = _lastMousePosition;
		if ( lastMousePosition.HasValue )
			_mouseDelta += e.LocalPosition - lastMousePosition.Value;


		_lastMousePosition = e.LocalPosition;
		base.OnMouseMove( e );
	}

	protected override void OnMouseWheel( WheelEvent e )
	{
		_mouseWheelDelta += new Vector2( 0, e.Delta / 120.0f );
		e.Accepted = true;
		base.OnMouseWheel( e );
	}

	[DllImport( "libX11.so.6", EntryPoint = "XOpenDisplay" )]
	private static extern IntPtr XOpenDisplay( IntPtr displayName );

	[DllImport( "libX11.so.6", EntryPoint = "XSetWindowBackgroundPixmap" )]
	private static extern int XSetWindowBackgroundPixmap( IntPtr display, IntPtr window, IntPtr pixmap );

	[DllImport( "libX11.so.6", EntryPoint = "XFlush" )]
	private static extern int XFlush( IntPtr display );

	[DllImport( "libX11.so.6", EntryPoint = "XGetImage" )]
	private static extern IntPtr XGetImage( IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, ulong planeMask, int format );

	[DllImport( "libX11.so.6", EntryPoint = "XGetPixel" )]
	private static extern ulong XGetPixel( IntPtr image, int x, int y );

	[DllImport( "libX11.so.6", EntryPoint = "XDestroyImage" )]
	private static extern int XDestroyImage( IntPtr image );

	private const int X11ZPixmap = 2;

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

	private void DisableLinuxX11Background( IntPtr windowId )
	{
		if ( _x11NoBackgroundWindow == windowId )
			return;

		var display = GetX11Display();
		if ( display == default )
			return;

		XSetWindowBackgroundPixmap( display, windowId, default );
		XFlush( display );
		_x11NoBackgroundWindow = windowId;
		if ( Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1" )
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-x11-bg-none] xid=0x{windowId.ToInt64():x}{Environment.NewLine}" );
	}

	private void LogLinuxX11Sample()
	{
		if ( !System.OperatingSystem.IsLinux() ||
			Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_X11_SAMPLE" ) != "1" ||
			(_viewportDebugFrame > 10 && _viewportDebugFrame % 60 != 0) )
			return;

		var display = GetX11Display();
		var window = _swapChainWindowId != default ? _swapChainWindowId : _widget.winId();
		if ( display == default || window == default )
			return;

		var x = Math.Max( 0, (int)(Size.x * 0.5f) );
		var y = Math.Max( 0, (int)(Size.y * 0.5f) );
		var image = default( IntPtr );
		try
		{
			image = XGetImage( display, window, x, y, 1, 1, ulong.MaxValue, X11ZPixmap );
			var pixel = image != default ? XGetPixel( image, 0, 0 ) : 0;
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-x11-sample] frame={_viewportDebugFrame} xid=0x{window.ToInt64():x} xy={x},{y} pixel=0x{pixel:x} image=0x{image.ToInt64():x} size={Size} swapchain=0x{SwapChain.self.ToInt64():x}{Environment.NewLine}" );
		}
		catch ( DllNotFoundException ex )
		{
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-x11-sample-error] {ex.GetType().Name}{Environment.NewLine}" );
		}
		catch ( EntryPointNotFoundException ex )
		{
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-x11-sample-error] {ex.GetType().Name}{Environment.NewLine}" );
		}
		catch ( BadImageFormatException ex )
		{
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-x11-sample-error] {ex.GetType().Name}{Environment.NewLine}" );
		}
		finally
		{
			if ( image != default )
				XDestroyImage( image );
		}
	}
	private unsafe SwapChainHandle_t CreateLinuxSwapChain( NativeEngine.RenderMultisampleType msaa, IntPtr windowId )
	{
		if ( !TryGetWindowHandles( windowId, out var platWindow, out var osWindow ) )
			return default;

		var pixelSize = Size * DpiScale;
		SetLinuxSdlWindowSize( platWindow, Math.Max( 1, (int)pixelSize.x ), Math.Max( 1, (int)pixelSize.y ) );
		RegisterLinuxSdlWindowXid( platWindow, windowId );

		var renderDevice = GetRenderDevice();
		if ( renderDevice == default )
			return default;
		var mainSwapChain = g_pEngineServiceMgr.GetEngineSwapChain();
		var info = mainSwapChain != default ? g_pRenderDevice.GetSwapChainInfo( mainSwapChain ) : new RenderDeviceInfo_t();
		info.m_nVersion = 1;
		info.m_DisplayMode.m_nVersion = 1;
		info.m_DisplayMode.m_nWidth = Math.Max( 1, (int)pixelSize.x );
		info.m_DisplayMode.m_nHeight = Math.Max( 1, (int)pixelSize.y );
		if ( mainSwapChain == default )
			info.m_DisplayMode.m_Format = ImageFormat.RGBA8888;
		info.m_DisplayMode.m_nRefreshRateNumerator = 60;
		info.m_DisplayMode.m_nRefreshRateDenominator = 1;
		info.m_DisplayMode.m_nFlags = uint.TryParse( Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_DISPLAY_FLAGS" ), out var displayFlags ) ? displayFlags : info.m_DisplayMode.m_nFlags;
		info.m_nBackBufferCount = Math.Max( 1, info.m_nBackBufferCount );
		info.m_nModeUsage = byte.TryParse( Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_MODE_USAGE" ), out var modeUsage ) ? modeUsage : (byte)8; // RENDER_DISPLAY_MODE_BORDERED_WINDOW
		info.m_nMultisampleType = msaa;
		info.m_bUseStencil = 1;
		info.m_bWaitForVSync = byte.TryParse( Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_VSYNC" ), out var vsync ) ? vsync : (byte)1;
		info.m_bUsingMultipleWindows = byte.TryParse( Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_MULTI_WINDOW" ), out var multiWindow ) ? multiWindow : (byte)1;
		info.m_bIsMainWindow = byte.TryParse( Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_MAIN_WINDOW" ), out var mainWindow ) ? mainWindow : (byte)0;

		var vtable = *(IntPtr**)renderDevice;
		TryDisableAsyncPresent( renderDevice );
		if ( Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1" )
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-create] xid=0x{windowId.ToInt64():x} plat=0x{platWindow.ToInt64():x} os=0x{osWindow.ToInt64():x} renderDevice=0x{renderDevice.ToInt64():x} v0=0x{vtable[0].ToInt64():x} v1=0x{vtable[1].ToInt64():x} v2=0x{vtable[2].ToInt64():x} v4=0x{vtable[4].ToInt64():x} v179=0x{vtable[179].ToInt64():x} v180=0x{vtable[180].ToInt64():x} size={info.m_DisplayMode.m_nWidth}x{info.m_DisplayMode.m_nHeight} refresh={info.m_DisplayMode.m_nRefreshRateNumerator}/{info.m_DisplayMode.m_nRefreshRateDenominator} flags=0x{info.m_DisplayMode.m_nFlags:x} usage={info.m_nModeUsage} vsync={info.m_bWaitForVSync} main={info.m_bIsMainWindow}{Environment.NewLine}" );
		// Source 2 IRenderDevice slot 0 is GetRenderDeviceAPI; slot 1 creates swapchains.
		byte* debugName = stackalloc byte[] { (byte)'E', (byte)'d', (byte)'i', (byte)'t', (byte)'o', (byte)'r', (byte)'V', (byte)'i', (byte)'e', (byte)'w', (byte)'p', (byte)'o', (byte)'r', (byte)'t', 0 };
		var createSwapChain = (delegate* unmanaged<IntPtr, IntPtr, IntPtr, RenderDeviceInfo_t*, byte*, IntPtr>)(void*)vtable[1];
		var swapChain = createSwapChain( renderDevice, platWindow, osWindow, &info, debugName );
		var swapChainWindowId = osWindow != default ? osWindow : windowId;
		if ( swapChain != default )
		{
			_swapChainWindowId = swapChainWindowId;
			_swapChainPixelSize = new Vector2( info.m_DisplayMode.m_nWidth, info.m_DisplayMode.m_nHeight );
		}
		if ( Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1" )
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-create-result] swapchain=0x{swapChain.ToInt64():x} osAfter=0x{swapChainWindowId.ToInt64():x}{Environment.NewLine}" );
		return swapChain;
	}


	private static unsafe bool TryGetWindowHandles( IntPtr windowId, out IntPtr platWindow, out IntPtr osWindow )
	{
		platWindow = default;
		osWindow = windowId;

		var tier0 = GetTier0();
		if ( tier0 == default || windowId == default )
			return false;

		var logViewport = Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1";

		if ( NativeLibrary.TryGetExport( tier0, "Plat_OsSpecificHandleToPlatWindow", out var toPlatWindowPtr ) )
		{
			var toPlatWindow = (delegate* unmanaged<IntPtr, IntPtr>)(void*)toPlatWindowPtr;
			platWindow = toPlatWindow( windowId );
			if ( logViewport )
				System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-plat-existing] xid=0x{windowId.ToInt64():x} plat=0x{platWindow.ToInt64():x}{Environment.NewLine}" );
			if ( platWindow != default )
			{
				osWindow = GetOsSpecificWindowHandle( tier0, platWindow, windowId );
				return true;
			}
		}

		if ( NativeLibrary.TryGetExport( tier0, "Plat_FindOrCreateWrappedPlatWindow", out var wrapWindowPtr ) )
		{
			byte created = 0;
			IntPtr wrappedWindow = default;
			var wrapWindow = (delegate* unmanaged<IntPtr, IntPtr, byte*, IntPtr*, IntPtr>)(void*)wrapWindowPtr;
			platWindow = wrapWindow( windowId, default, &created, &wrappedWindow );
			if ( logViewport )
				System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-plat-wrap] xid=0x{windowId.ToInt64():x} plat=0x{platWindow.ToInt64():x} created={created} wrapped=0x{wrappedWindow.ToInt64():x}{Environment.NewLine}" );
			if ( platWindow != default )
			{
				osWindow = GetOsSpecificWindowHandle( tier0, platWindow, wrappedWindow != default ? wrappedWindow : windowId );
				if ( logViewport )
					System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-plat-wrap-os] plat=0x{platWindow.ToInt64():x} os=0x{osWindow.ToInt64():x}{Environment.NewLine}" );
				return true;
			}
		}

		if ( logViewport )
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-plat-missing] xid=0x{windowId.ToInt64():x}{Environment.NewLine}" );
		return false;
	}

	private static unsafe IntPtr GetOsSpecificWindowHandle( IntPtr tier0, IntPtr platWindow, IntPtr fallback )
	{
		if ( NativeLibrary.TryGetExport( tier0, "Plat_WindowToOsSpecificHandle", out var toOsWindowPtr ) )
		{
			var toOsWindow = (delegate* unmanaged<IntPtr, IntPtr>)(void*)toOsWindowPtr;
			var osWindow = toOsWindow( platWindow );
			if ( osWindow != default )
				return osWindow;
		}

		return fallback;
	}

	private static IntPtr GetTier0()
	{
		if ( _tier0 != default || _triedTier0 )
			return _tier0;

		_triedTier0 = true;
		var name = Sandbox.Interop.GetNativeLibraryName( "tier0" );
		NativeLibrary.TryLoad( System.IO.Path.Combine( NetCore.NativeDllPath, name ), out _tier0 );
		return _tier0;
	}


	private static unsafe void RegisterLinuxSdlWindowXid( IntPtr sdlWindow, IntPtr windowId )
	{
		if ( sdlWindow == default || windowId == default )
			return;

		if ( _registerSdlWindowXid == default && !_triedRegisterSdlWindowXid )
		{
			_triedRegisterSdlWindowXid = true;
			byte* symbol = stackalloc byte[] { (byte)'s', (byte)'b', (byte)'o', (byte)'x', (byte)'_', (byte)'r', (byte)'e', (byte)'g', (byte)'i', (byte)'s', (byte)'t', (byte)'e', (byte)'r', (byte)'_', (byte)'s', (byte)'d', (byte)'l', (byte)'_', (byte)'w', (byte)'i', (byte)'n', (byte)'d', (byte)'o', (byte)'w', (byte)'_', (byte)'x', (byte)'i', (byte)'d', 0 };

			if ( NativeLibrary.TryLoad( "libdl.so.2", out var libdl ) &&
				NativeLibrary.TryGetExport( libdl, "dlsym", out var dlsymPtr ) )
			{
				var dlsym = (delegate* unmanaged<IntPtr, byte*, IntPtr>)(void*)dlsymPtr;
				_registerSdlWindowXid = dlsym( IntPtr.Zero, symbol );
			}

			if ( _registerSdlWindowXid == default )
			{
				var name = Sandbox.Interop.GetNativeLibraryName( "sbox_vulkan_swapchain_patch" );
				var preload = Environment.GetEnvironmentVariable( "LD_PRELOAD" );
				if ( !string.IsNullOrEmpty( preload ) )
				{
					foreach ( var path in preload.Split( ':', StringSplitOptions.RemoveEmptyEntries ) )
					{
						if ( !path.EndsWith( name, StringComparison.Ordinal ) )
							continue;

						if ( NativeLibrary.TryLoad( path, out var patch ) &&
							NativeLibrary.TryGetExport( patch, "sbox_register_sdl_window_xid", out var registerPtr ) )
						{
							_registerSdlWindowXid = registerPtr;
							break;
						}
					}
				}
			}
		}

		if ( Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1" )
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-register-sdl] window=0x{sdlWindow.ToInt64():x} xid=0x{windowId.ToInt64():x} fn=0x{_registerSdlWindowXid.ToInt64():x}{Environment.NewLine}" );

		if ( _registerSdlWindowXid == default )
			return;

		var register = (delegate* unmanaged<IntPtr, ulong, void>)(void*)_registerSdlWindowXid;
		register( sdlWindow, (ulong)windowId.ToInt64() );
	}

	private static unsafe void SetLinuxSdlWindowSize( IntPtr sdlWindow, int width, int height )
	{
		if ( sdlWindow == default )
			return;

		if ( _sdlSetWindowSize == default && !_triedSdlSetWindowSize )
		{
			_triedSdlSetWindowSize = true;
			byte* symbol = stackalloc byte[] { (byte)'S', (byte)'D', (byte)'L', (byte)'_', (byte)'S', (byte)'e', (byte)'t', (byte)'W', (byte)'i', (byte)'n', (byte)'d', (byte)'o', (byte)'w', (byte)'S', (byte)'i', (byte)'z', (byte)'e', 0 };

			if ( NativeLibrary.TryLoad( "libdl.so.2", out var libdl ) &&
				NativeLibrary.TryGetExport( libdl, "dlsym", out var dlsymPtr ) )
			{
				var dlsym = (delegate* unmanaged<IntPtr, byte*, IntPtr>)(void*)dlsymPtr;
				_sdlSetWindowSize = dlsym( IntPtr.Zero, symbol );
			}
		}

		if ( Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1" )
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-sdl-size] window=0x{sdlWindow.ToInt64():x} size={width}x{height} fn=0x{_sdlSetWindowSize.ToInt64():x}{Environment.NewLine}" );

		if ( _sdlSetWindowSize == default )
			return;

		var setSize = (delegate* unmanaged<IntPtr, int, int, void>)(void*)_sdlSetWindowSize;
		setSize( sdlWindow, width, height );
	}


	private static unsafe IntPtr GetRenderDevice()
	{
		if ( _renderDevice != default || _triedRenderDevice )
			return _renderDevice;

		_triedRenderDevice = true;

		var present = FindRenderDevice( (void*)g_pRenderDevice.__N.g_pRenderDevice_Present );
		var destroy = FindRenderDevice( (void*)g_pRenderDevice.__N.g_pRenderDevice_DestroySwapChain );
		var info = FindRenderDevice( (void*)g_pRenderDevice.__N.g_pRenderDevice_GetSwapChainInfo );

		if ( present != default && (present == destroy || present == info) )
			_renderDevice = present;
		else if ( destroy != default && destroy == info )
			_renderDevice = destroy;

		return _renderDevice;
	}

	private static unsafe IntPtr FindRenderDevice( void* wrapper )
	{
		if ( wrapper == null )
			return default;

		var code = (byte*)wrapper;
		for ( var i = 0; i < 96; i++ )
		{
			if ( !TryReadRenderDeviceCandidate( code + i, out var candidate ) )
				continue;

			if ( LooksLikeRenderDevice( candidate ) )
				return candidate;
		}

		return default;
	}

	private static unsafe bool TryReadRenderDeviceCandidate( byte* code, out IntPtr candidate )
	{
		candidate = default;

		if ( !IsRipRelativeMov( code ) && !IsRipRelativeLea( code ) )
			return false;

		var target = (IntPtr*)(code + 7 + *(int*)(code + 3));
		candidate = *target;
		return true;
	}

	private static unsafe bool IsRipRelativeMov( byte* code )
	{
		return (code[0] == 0x48 || code[0] == 0x4c) && code[1] == 0x8b && (code[2] & 0xc7) == 0x05;
	}

	private static unsafe bool IsRipRelativeLea( byte* code )
	{
		return (code[0] == 0x48 || code[0] == 0x4c) && code[1] == 0x8d && (code[2] & 0xc7) == 0x05;
	}

	private static unsafe bool LooksLikeRenderDevice( IntPtr candidate )
	{
		if ( candidate == default )
			return false;

		var vtable = *(IntPtr**)candidate;
		return vtable != null && vtable[0] != default && vtable[1] != default;
	}

	private static unsafe void TryDisableAsyncPresent( IntPtr renderDevice )
	{
		if ( _triedDisableAsyncPresent || Environment.GetEnvironmentVariable( "SBOX_DISABLE_ASYNC_PRESENT" ) != "1" )
			return;

		_triedDisableAsyncPresent = true;
		var vtable = *(IntPtr**)renderDevice;
		var isAsyncPresentEnabled = (delegate* unmanaged<IntPtr, int>)(void*)vtable[179];
		var enableAsyncPresent = (delegate* unmanaged<IntPtr, int, void>)(void*)vtable[180];
		var before = isAsyncPresentEnabled( renderDevice );
		enableAsyncPresent( renderDevice, 0 );
		var after = isAsyncPresentEnabled( renderDevice );

		if ( Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1" )
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-async-present] before={before} after={after}{Environment.NewLine}" );
	}


	/// <summary>
	/// The active scene that we're rendering
	/// </summary>
	public Scene Scene { get; set; }

	/// <summary>
	/// The camera to render from. We will fallback to Scene.Camera if this is null
	/// </summary>
	public CameraComponent Camera { get; set; }

	/// <summary>
	/// This widget manages it's own gizmo instance.
	/// </summary>
	public Gizmo.Instance GizmoInstance { get; private set; } = new();

	public bool EnableEngineOverlays { get; set; } = false;

	// Track if we've locked this widget's size for recording
	private bool _sizeLockedForRecording;
	private Vector2 _savedMinSize;
	private Vector2 _savedMaxSize;

	public SceneRenderingWidget( Widget parent = null ) : base( parent )
	{
		// WA_DontCreateNativeAncestors must be set on all platforms, including Linux.
		// Without it, WA_NativeWindow forces Qt to create native X11 windows for every
		// ancestor in the hierarchy. Any ancestor carrying Qt::Tool/Qt::Window flags
		// (dock panels, tool windows) then becomes a separate top-level X11 window
		// that renders over the editor instead of being embedded in the Qt layout.
		SetFlag( Flag.WA_DontCreateNativeAncestors, true );
		SetFlag( Flag.WA_NativeWindow, true );
		SetFlag( Flag.WA_NoSystemBackground, true );
		SetFlag( Flag.WA_OpaquePaintEvent, true );
		if ( System.OperatingSystem.IsLinux() )
			SetFlag( Flag.WA_PaintOnScreen, true );

		// On Linux/XWayland, Qt's software paint cycle fires on mouse-enter and click
		// expose events, briefly clearing the native window before the next SwapChain
		// present and causing a visible flash. Since all rendering goes through the
		// SwapChain, the Qt paint path serves no purpose and can be suppressed entirely.
		OnPaintOverride = () =>
		{
			if ( System.OperatingSystem.IsLinux() &&
				Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1" &&
				(++_paintOverrideFrame <= 5 || _paintOverrideFrame % 60 == 0) )
			{
				System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-paint-suppressed] frame={_paintOverrideFrame} visible={Visible} screen={ScreenPosition} size={Size} swapchain=0x{SwapChain.self.ToInt64():x}{Environment.NewLine}" );
			}

			return true;
		};

		var tryNativeQtSwapChain = !System.OperatingSystem.IsLinux() ||
			Environment.GetEnvironmentVariable( "SBOX_EXPERIMENTAL_QT_SWAPCHAIN" ) == "1";

		SwapChain = tryNativeQtSwapChain
			? WidgetUtil.CreateSwapChain( _widget, RenderSettings.Instance.AntiAliasQuality.ToEngine() )
			: default;
		_ownsSwapChain = SwapChain != default;

		RenderSettings.Instance.OnVideoSettingsChanged += HandleVideoChanged;

		FocusMode = FocusMode.Click; // If we're focused we're probably accepting input, don't let tab blur us
		MouseTracking = true;

		All.Add( this );
	}


	internal override void NativeShutdown()
	{
		base.NativeShutdown();

		All.Remove( this );
		RenderSettings.Instance.OnVideoSettingsChanged -= HandleVideoChanged;
		if ( _registeredWindowId != default )
			NativeEngine.InputSystem.UnregisterWindowFromSDL( _registeredWindowId );

		_registeredWithSdl = false;
		_registeredWindowId = default;
		_boundEngineState = false;
		_swapChainWindowId = default;

		if ( _ownsSwapChain && SwapChain != default )
		{
			// The swapchain might still be in use by native, so defer its destruction until the end of the frame.
			// Otherwise, a race condition could occur where render targets are accessed after destruction, causing a delayed crash.
			var oldSwapChain = SwapChain;
			EngineLoop.DisposeAtFrameEnd( new Sandbox.Utility.DisposeAction( () =>
			{
				g_pRenderDevice.DestroySwapChain( oldSwapChain );
			} ) );
		}

		SwapChain = default;

		GizmoInstance?.Dispose();
		GizmoInstance = default;
	}

	void EnsureLinuxSwapChain()
	{
		if ( !System.OperatingSystem.IsLinux() )
			return;

		var widgetWindowId = _widget.winId();
		if ( widgetWindowId == default )
			return;
		DisableLinuxX11Background( widgetWindowId );
		if ( _creatingLinuxSwapChain )
			return;


		if ( _registeredWithSdl && _registeredWindowId != widgetWindowId )
		{
			if ( _registeredWindowId != default )
				NativeEngine.InputSystem.UnregisterWindowFromSDL( _registeredWindowId );

			_registeredWithSdl = false;
			_boundEngineState = false;
		}

		if ( !_registeredWithSdl )
		{
			NativeEngine.InputSystem.RegisterWindowWithSDL( widgetWindowId );
			_registeredWithSdl = true;
			_registeredWindowId = widgetWindowId;
		}

		var pixelSize = Size * DpiScale;
		var pixelWidth = Math.Max( 1, (int)pixelSize.x );
		var pixelHeight = Math.Max( 1, (int)pixelSize.y );
		var windowId = widgetWindowId;
		var sizeChanged = _swapChainPixelSize != default &&
			((int)_swapChainPixelSize.x != pixelWidth || (int)_swapChainPixelSize.y != pixelHeight);
		if ( _allowLinuxSwapChainCreate && _ownsSwapChain && SwapChain != default && _swapChainWindowId != default && (_swapChainWindowId != windowId || sizeChanged) )
		{
			if ( Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1" )
				System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-swapchain-recreate] old=0x{_swapChainWindowId.ToInt64():x} new=0x{windowId.ToInt64():x} oldSize={_swapChainPixelSize} newSize={pixelWidth}x{pixelHeight} swapchain=0x{SwapChain.self.ToInt64():x}{Environment.NewLine}" );

			var oldSwapChain = SwapChain;
			EngineLoop.DisposeAtFrameEnd( new Sandbox.Utility.DisposeAction( () =>
			{
				g_pRenderDevice.DestroySwapChain( oldSwapChain );
			} ) );
			SwapChain = default;
			_ownsSwapChain = false;
			_boundEngineState = false;
			_loggedLinuxSwapChain = false;
			_loggedLinuxRender = false;
			_swapChainWindowId = default;
			_swapChainPixelSize = default;
			return;
		}

		var msaa = RenderSettings.Instance.AntiAliasQuality.ToEngine();
		// ponytail: only the native Qt helper is opt-in; the managed Linux path is the viewport.
		var tryNativeQtSwapChain = Environment.GetEnvironmentVariable( "SBOX_EXPERIMENTAL_QT_SWAPCHAIN" ) == "1";

		if ( SwapChain == default && _allowLinuxSwapChainCreate )
		{
			if ( tryNativeQtSwapChain )
				SwapChain = WidgetUtil.CreateSwapChain( _widget, msaa );

			if ( SwapChain == default )
			{
				_creatingLinuxSwapChain = true;
				try
				{
					SwapChain = CreateLinuxSwapChain( msaa, windowId );
				}
				finally
				{
					_creatingLinuxSwapChain = false;
				}
			}

			if ( SwapChain != default )
			{
				_ownsSwapChain = true;
			}
		}


		if ( SwapChain == default )
		{
			if ( Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1" && !_loggedMissingLinuxSwapChain )
			{
				_loggedMissingLinuxSwapChain = true;
				System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-bind-missing] visible={Visible} screen={ScreenPosition} size={Size} dpi={DpiScale}{Environment.NewLine}" );
			}
			return;
		}

		if ( !_boundEngineState )
		{
			g_pEngineServiceMgr.SetEngineState( windowId, SwapChain );
			_boundEngineState = true;
		}


		if ( Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1" && !_loggedLinuxSwapChain )
		{
			_loggedLinuxSwapChain = true;
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-bind] widget=0x{widgetWindowId.ToInt64():x} xid=0x{windowId.ToInt64():x} swapchain={SwapChain} visible={Visible} screen={ScreenPosition} size={Size} dpi={DpiScale}{Environment.NewLine}" );
		}
	}

	/// <summary>
	/// Create a hidden scene editor camera, post processing will be copied from a main camera in the scene.
	/// </summary>
	public CameraComponent CreateSceneEditorCamera()
	{
		if ( Scene is null ) return null;

		using ( Scene.Push() )
		{
			var go = new GameObject( true, "editor_camera" );
			go.Flags = GameObjectFlags.Hidden | GameObjectFlags.NotSaved | GameObjectFlags.EditorOnly | GameObjectFlags.Absolute;
			var camera = go.AddComponent<CameraComponent>();
			camera.RenderExcludeTags.Add( "hidden" );
			camera.IsMainCamera = false;
			camera.IsSceneEditorCamera = true;
			return camera;
		}
	}

	void RenderScene()
	{
		if ( !this.IsValid() )
			return;

		EnsureLinuxSwapChain();

		if ( SwapChain == default ) return;
		var logViewport = Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1";

		var sceneCamera = GetSceneCamera();
		if ( sceneCamera is not null )
		{
			sceneCamera.EnableEngineOverlays = EnableEngineOverlays;
		}

		// Set the recording camera for video/screenshot recording (only if this widget has focus)
		if ( sceneCamera is not null && IsFocused )
		{
			SceneCamera.RecordingCamera = sceneCamera;
		}

		// Lock widget size during recording to prevent resolution changes
		if ( ScreenRecorder.IsRecording() && sceneCamera?.IsRecordingCamera == true && !_sizeLockedForRecording )
		{
			_savedMinSize = MinimumSize;
			_savedMaxSize = MaximumSize;
			MinimumSize = Size;
			MaximumSize = Size;
			_sizeLockedForRecording = true;
		}
		else if ( !ScreenRecorder.IsRecording() && _sizeLockedForRecording )
		{
			MinimumSize = _savedMinSize;
			MaximumSize = _savedMaxSize;
			_sizeLockedForRecording = false;
		}

		var renderSize = _swapChainPixelSize != default ? _swapChainPixelSize : Size * DpiScale;
		if ( Camera.IsValid() )
		{
			Camera.Scene?.PreCameraRender();
			if ( logViewport && !_loggedLinuxRender )
			{
				var objects = Scene.GetAllObjects( false ).Count();
				var renderers = Scene.GetAllComponents<ModelRenderer>().Count();
				System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-before-camera-render] camera={Camera.IsValid()} active={Camera?.Active} pos={Camera?.WorldPosition} rot={Camera?.WorldRotation} clear={Camera?.BackgroundColor} sceneObjects={objects} modelRenderers={renderers} size={Size} target={renderSize} swapchain=0x{SwapChain.self.ToInt64():x}{Environment.NewLine}" );
			}
			Camera.AddToRenderList( SwapChain, renderSize );
			if ( logViewport && !_loggedLinuxRender )
				System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-after-camera-render]{Environment.NewLine}" );
		}
		else if ( Scene.IsValid() )
		{
			Scene.Render( SwapChain, renderSize );
		}
	}

	/// <inheritdoc cref="PreFrame"/>
	public event Action OnPreFrame;

	/// <summary>
	/// Called just before rendering.
	/// </summary>
	protected virtual void PreFrame()
	{
		OnPreFrame?.Invoke();
	}

	/// <summary>
	/// Update common inputs for gizmo.
	/// </summary>
	public void UpdateGizmoInputs( bool hasMouseFocus = true )
	{
		var camera = GetSceneCamera();
		if ( camera is null ) return;

		UpdateGizmoInputs( ref GizmoInstance.Input, camera, hasMouseFocus );
	}

	void Render()
	{
		var logViewport = Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1";
		if ( logViewport && !_loggedRenderEntry )
		{
			_loggedRenderEntry = true;
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-render-entry] visible={Visible} scene={Scene.IsValid()} swapchain=0x{SwapChain.self.ToInt64():x} size={Size}{Environment.NewLine}" );
		}
		if ( !Visible ) return;

		EnsureLinuxSwapChain();

		logViewport = Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1";
		if ( logViewport && System.OperatingSystem.IsLinux() && ++_viewportDebugFrame % 60 == 0 )
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-frame] frame={_viewportDebugFrame} visible={Visible} widget=0x{_widget.winId().ToInt64():x} render=0x{_swapChainWindowId.ToInt64():x} swapchain=0x{SwapChain.self.ToInt64():x} size={Size} pixel={_swapChainPixelSize}{Environment.NewLine}" );
		if ( !Scene.IsValid() )
		{
			if ( logViewport && !_loggedLinuxRender )
			{
				_loggedLinuxRender = true;
				System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-render-missing-scene] visible={Visible} swapchain={SwapChain} size={Size}{Environment.NewLine}" );
			}
			return;
		}

		if ( SwapChain == default ) return;
		if ( System.OperatingSystem.IsLinux() )
			g_pEngineServiceMgr.SetEngineState( _swapChainWindowId != default ? _swapChainWindowId : _widget.winId(), SwapChain );



		using ( Scene.Push() )
		{
			using ( GizmoInstance.Push() )
			{
				if ( logViewport && !_loggedLinuxRender )
					System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-before-render-scene]{Environment.NewLine}" );
				PreFrame();
				RenderScene();
				if ( logViewport && !_loggedLinuxRender )
					System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-after-render-scene]{Environment.NewLine}" );
			}
		}

		if ( GameMode.IsPlayWidget( this ) )
		{
			CCameraRenderer.RenderOverlay( SwapChain );
		}

		if ( logViewport && !_loggedLinuxRender )
		{
			_loggedLinuxRender = true;
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-render] camera={Camera.IsValid()} active={Camera?.Active} viewport={Camera?.Viewport} sceneCamera={Scene.Camera.IsValid()} size={Size} dpi={DpiScale} swapchain=0x{SwapChain.self.ToInt64():x}{Environment.NewLine}" );
		}

		g_pRenderDevice.Present( SwapChain );
		if ( logViewport && System.OperatingSystem.IsLinux() && _viewportDebugFrame % 60 == 0 )
		{
			var info = g_pRenderDevice.GetSwapChainInfo( SwapChain );
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-present] frame={_viewportDebugFrame} swapchain=0x{SwapChain.self.ToInt64():x} infoSize={info.m_DisplayMode.m_nWidth}x{info.m_DisplayMode.m_nHeight} infoUsage={info.m_nModeUsage} infoMain={info.m_bIsMainWindow} screen={ScreenPosition} widgetSize={Size}{Environment.NewLine}" );
		}
		LogLinuxX11Sample();
	}

	private void UpdateGizmoInputs( ref Gizmo.Inputs input, SceneCamera camera, bool hasMouseFocus )
	{
		ArgumentNullException.ThrowIfNull( camera );

		input.Camera = camera;
		input.Modifiers = Application.KeyboardModifiers;

		if ( !hasMouseFocus )
		{
			input.CursorRay = new Ray();
			return;
		}

		input.CursorPosition = Application.CursorPosition;
		var mouseButtons = MouseButtons;
		input.LeftMouse = mouseButtons.HasFlag( MouseButtons.Left );
		input.RightMouse = mouseButtons.HasFlag( MouseButtons.Right );

		input.CursorPosition -= ScreenPosition;
		input.CursorRay = camera.GetRay( input.CursorPosition, Size );

		if ( !input.IsHovered )
		{
			input.LeftMouse = false;
			input.RightMouse = false;
		}
	}

	private SceneCamera GetSceneCamera()
	{
		if ( Camera.IsValid() )
			return Camera.SceneCamera;

		if ( !Scene.IsValid() )
			return null;

		if ( !Scene.Camera.IsValid() )
			return null;

		return Scene.Camera.SceneCamera;
	}

	/// <summary>
	/// Return a ray for the current cursor position
	/// </summary>
	public Ray CursorRay
	{
		get => GetRay( Application.CursorPosition - ScreenPosition );
	}

	/// <summary>
	/// Given a local widget position, return a Ray
	/// </summary>
	public Ray GetRay( Vector2 localPosition )
	{
		var camera = GetSceneCamera();
		if ( camera is null )
			return default;

		return camera.GetRay( localPosition, Size );
	}

	internal static void CreateMissingLinuxSwapChains()
	{
		if ( !System.OperatingSystem.IsLinux() )
			return;

		_allowLinuxSwapChainCreate = true;
		try
		{
			foreach ( var widget in All )
			{
				if ( widget.Visible )
					widget.EnsureLinuxSwapChain();
			}
		}
		finally
		{
			_allowLinuxSwapChainCreate = false;
		}
	}

	internal void HandleVideoChanged()
	{
		var msaaAmount = RenderSettings.Instance.AntiAliasQuality.ToEngine();

		if ( !_ownsSwapChain )
			return;

		if ( SwapChain == default )
		{
			SwapChain = WidgetUtil.CreateSwapChain( _widget, msaaAmount );
			if ( SwapChain == default && System.OperatingSystem.IsLinux() )
				SwapChain = CreateLinuxSwapChain( msaaAmount, _widget.winId() );
			return;
		}

		WidgetUtil.UpdateSwapChainMSAA( SwapChain, msaaAmount );
	}

	internal static void RenderAll()
	{
		foreach ( var widget in All )
		{
			if ( !widget.Visible ) continue;

			widget.Render();
		}
	}
}
