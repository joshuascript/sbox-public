namespace Editor;

public class SceneOverlayWidget : Widget
{
	public static SceneOverlayWidget Active { get; private set; }

	public Layout Header { get; private set; }

	internal SceneOverlayWidget( Widget parent ) : base( parent )
	{
		TranslucentBackground = true;
		NoSystemBackground = true;

		if ( !System.OperatingSystem.IsLinux() )
			WindowFlags = WindowFlags.FramelessWindowHint | WindowFlags.Tool;

		Active = this;

		Layout = Layout.Column();
		Layout.Margin = 8;

		var header = Layout.AddRow();
		header.AddStretchCell();
		Header = header.AddRow();
		Header.Spacing = 4;

		Layout.AddStretchCell();

		// doesn't handle floating windows, but there's no way to hook into dockwrapper events right now
		EditorWindow.Moved += UpdateDimensions;

		TransparentForMouseEvents = true;
		if ( System.OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1" )
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-overlay-created] parent={parent?.GetType().Name} pos={Position} size={Size} translucent={TranslucentBackground} noSystemBackground={NoSystemBackground}{Environment.NewLine}" );
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		if ( EditorWindow.IsValid() )
		{
			EditorWindow.Moved -= UpdateDimensions;
		}
	}

	int lastGeometryHash = -1;
	int paintLogFrame;

	[EditorEvent.Frame]
	private void UpdateDimensions()
	{
		if ( !Parent.IsValid() )
			return;

		// this wasn't always being triggered properly when relying on widget events from the parent (causing HUGE jank)
		var position = System.OperatingSystem.IsLinux() ? Vector2.Zero : Parent.ScreenPosition;
		var size = System.OperatingSystem.IsLinux() ? new Vector2( Parent.Size.x, 48 ) : Parent.Size;
		int geometryHash = HashCode.Combine( position, size );
		if ( lastGeometryHash != geometryHash )
		{
			Position = position;
			Size = size;
			if ( System.OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1" )
				System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-overlay-geometry] pos={Position} size={Size} parentScreen={Parent.ScreenPosition} parentSize={Parent.Size}{Environment.NewLine}" );
		}

		lastGeometryHash = geometryHash;
	}

	internal RealTimeSince timeSinceNeededRedraw = 0.0f;

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( timeSinceNeededRedraw > 0.1f )
		{
			Update();
			timeSinceNeededRedraw = 0.0f;
		}
	}

	protected override void OnPaint()
	{
		Active = this;
		if ( System.OperatingSystem.IsLinux() &&
			Environment.GetEnvironmentVariable( "SBOX_VIEWPORT_BIND_LOG" ) == "1" &&
			(++paintLogFrame <= 5 || paintLogFrame % 60 == 0) )
		{
			System.IO.File.AppendAllText( "/tmp/sbox-viewport-bind.log", $"[viewport-overlay-paint] frame={paintLogFrame} pos={Position} size={Size} parentScreen={Parent?.ScreenPosition} parentSize={Parent?.Size}{Environment.NewLine}" );
		}

		if ( Parent is SceneViewportWidget vw )
		{
			if ( vw.SceneView.CurrentView == SceneViewWidget.ViewMode.Game )
			{
				EditorEvent.Run( "sceneview.paintoverlay" );
			}
			else
			{
				vw.PaintOrientationGizmo();
			}
		}
	}
}
