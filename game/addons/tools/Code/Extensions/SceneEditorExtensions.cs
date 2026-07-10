namespace Editor;

public static class SceneEditorExtensions
{
	private static readonly Pixmap EyeCursor = Pixmap.FromFile( "toolimages:scene_view/cursor_eye.png" );

	/// <summary>
	/// Helper to easily set up all of the inputs for this camera and widget. This is assuming
	/// that the passed in widget is the render panel.
	/// </summary>
	public static void UpdateInputs( this Gizmo.Instance self, SceneCamera camera, Widget canvas = null, bool hasMouseFocus = true )
	{
		ArgumentNullException.ThrowIfNull( camera );

		self.Input.Camera = camera;
		self.Input.Modifiers = Application.KeyboardModifiers;

		if ( !hasMouseFocus )
		{
			self.Input.CursorRay = new Ray();
			return;
		}

		self.Input.CursorPosition = Application.CursorPosition;
		var mouseButtons = GetMouseButtons( canvas );
		self.Input.LeftMouse = mouseButtons.HasFlag( MouseButtons.Left );
		self.Input.RightMouse = mouseButtons.HasFlag( MouseButtons.Right );

		if ( canvas.IsValid() )
		{
			self.Input.CursorPosition -= canvas.ScreenPosition;
			self.Input.CursorRay = camera.GetRay( self.Input.CursorPosition, canvas.Size );

			if ( !self.Input.IsHovered )
			{
				self.Input.LeftMouse = false;
				self.Input.RightMouse = false;
			}
		}
	}

	record struct CameraStorage( Vector3 velocity, Vector3? targetPosition );

	/// <summary>
	/// Locks the cursor to a specific widget. If we go outside it, this function will
	/// wrap the cursor around nicely.
	/// </summary>
	public static bool LockCursorToCanvas( Widget canvas, int margin = 16 )
	{
		var rect = canvas.LocalRect.Shrink( margin );
		var pos = canvas.FromScreen( Application.CursorPosition );

		if ( rect.IsInside( pos ) )
			return false;

		var newPos = new Vector2(
			pos.x < rect.Left ? rect.Right : pos.x > rect.Right ? rect.Left : pos.x,
			pos.y < rect.Top ? rect.Bottom : pos.y > rect.Bottom ? rect.Top : pos.y );

		Application.UnscaledCursorPosition += (newPos - pos) * canvas.DpiScale;

		return true;
	}

	private static float RoundToNearest( float value, float step )
	{
		return (float)Math.Round( value / step ) * step;
	}

	private static MouseButtons GetMouseButtons( Widget canvas )
	{
		return canvas is SceneRenderingWidget renderer
			? renderer.MouseButtons
			: Application.MouseButtons;
	}

	private static bool IsKeyDown( string shortcut, KeyCode key )
	{
		return EditorShortcuts.IsDown( shortcut ) || (System.OperatingSystem.IsLinux() && Application.IsKeyDown( key ));
	}

	private static Vector2 ConsumeMouseDelta( Widget canvas )
	{
		if ( canvas is SceneRenderingWidget renderer )
		{
			var delta = renderer.ConsumeMouseDelta();
			var polledDelta = renderer.ConsumePolledMouseDelta( renderer.FromScreen( Application.CursorPosition ), renderer.MouseButtons != MouseButtons.None );
			if ( !delta.IsNearZeroLength )
				return delta / Application.DpiScale;

			if ( !polledDelta.IsNearZeroLength )
				return polledDelta;
		}

		var cursorDelta = Application.CursorDelta;
		if ( !System.OperatingSystem.IsLinux() || !cursorDelta.IsNearZeroLength )
			return cursorDelta;

		var mouseDelta = Sandbox.Mouse.Delta;
		return !mouseDelta.IsNearZeroLength ? mouseDelta : Sandbox.Input.MouseDelta;
	}

	private static Vector2 ConsumeMouseWheelDelta( Widget canvas )
	{
		if ( canvas is SceneRenderingWidget renderer )
		{
			var delta = renderer.ConsumeMouseWheelDelta();
			if ( delta.x != 0.0f || delta.y != 0.0f )
				return delta;
		}

		var wheel = Application.MouseWheelDelta;
		return System.OperatingSystem.IsLinux() && wheel.x == 0.0f && wheel.y == 0.0f
			? Sandbox.Input.MouseWheel
			: wheel;
	}

	/// <summary>
	/// Helper to easily set up all of the inputs for this camera and widget. This is assuming
	/// that the passed in widget is the render panel.
	/// </summary>
	public static bool FirstPersonCamera( this Gizmo.Instance self, CameraComponent camera, Widget canvas, bool lockCursor = false )
	{
		ArgumentNullException.ThrowIfNull( camera );
		ArgumentNullException.ThrowIfNull( canvas );

		var cameraTarget = self.GetValue<Vector3?>( "CameraTarget" );
		var cameraVelocity = self.GetValue<Vector3>( "CameraVelocity" );

		bool moved = false;
		var mouseButtons = GetMouseButtons( canvas );
		var rightMouse = mouseButtons.HasFlag( MouseButtons.Right );
		var middleMouse = mouseButtons.HasFlag( MouseButtons.Middle );
		var mouseWheelDelta = ConsumeMouseWheelDelta( canvas );
		var cursorDelta = ConsumeMouseDelta( canvas );
		var lookMouse = rightMouse;
		var panMouse = middleMouse;


		if ( ((lookMouse && !camera.Orthographic) || panMouse) && self.Input.IsHovered )
		{
			EditorShortcuts.AllowShortcuts = false;
			canvas.Focus();

			var delta = cursorDelta * 0.1f;

			if ( lockCursor && LockCursorToCanvas( canvas ) )
				delta = Vector2.Zero;

			if ( self.ControlMode != "firstperson" )
			{
				EditorShortcuts.ReleaseAll();

				delta = 0;
				self.ControlMode = "firstperson";
				self.StompCursorPosition( Application.CursorPosition );
			}

			var moveSpeed = EditorPreferences.CameraSpeed;

			var modifiers = Application.KeyboardModifiers;
			if ( EditorShortcuts.IsDown( "scene.movement-quick" ) || (System.OperatingSystem.IsLinux() && modifiers.HasFlag( KeyboardModifiers.Shift )) ) moveSpeed *= 8.0f;
			if ( EditorShortcuts.IsDown( "scene.movement-slow" ) || (System.OperatingSystem.IsLinux() && modifiers.HasFlag( KeyboardModifiers.Ctrl )) ) moveSpeed /= 8.0f;

			if ( lookMouse && !camera.Orthographic )
			{
				// adjust camera speed with scroll wheel
				if ( mouseWheelDelta.y != 0.0f )
				{
					var currentSpeed = EditorPreferences.CameraSpeed;

					// Determine increment/decrement based on current speed
					var adjustment = (currentSpeed < 5.0f) ? 0.25f :
									   (currentSpeed < 20.0f) ? 1.0f :
									   RoundToNearest( currentSpeed * 0.1f, 2.5f );

					currentSpeed += adjustment * Math.Sign( mouseWheelDelta.y );
					currentSpeed = Math.Clamp( currentSpeed, 0.25f, 100.0f );

					EditorPreferences.CameraSpeed = currentSpeed;
					SceneViewWidget.Current?.LastSelectedViewportWidget?.timeSinceCameraSpeedChange = 0;
				}

				var sens = EditorPreferences.CameraSensitivity;
				var angles = camera.WorldRotation.Angles();

				angles.roll = 0;
				angles.yaw -= delta.x * sens;
				angles.pitch += delta.y * sens;
				angles.pitch = angles.pitch.Clamp( -89, 89 );

				// Updating camera angles is lossy on the backing quat, so don't shake the camera non stop
				if ( !delta.IsNearZeroLength )
					camera.WorldRotation = angles;

				if ( EditorPreferences.HideRotateCursor )
					canvas.Cursor = CursorShape.Blank;
				else
					canvas.PixmapCursor = EyeCursor;
			}
			else if ( panMouse )
			{
				cameraVelocity = default;
				cameraTarget = default;

				var positionChange = new Vector3();

				float zoomModifierY = camera.Orthographic ? camera.OrthographicHeight / canvas.Height : 2.0f;
				float zoomModifierX = camera.Orthographic ? (camera.OrthographicHeight * (canvas.Size.x / canvas.Size.y)) / canvas.Width : 2.0f;

				positionChange += camera.WorldRotation.Right * cursorDelta.x * zoomModifierX;
				positionChange += camera.WorldRotation.Down * cursorDelta.y * zoomModifierY;

				if ( !EditorPreferences.CameraInvertPan )
					positionChange = -positionChange;

				camera.WorldPosition += positionChange;

				if ( EditorPreferences.HidePanCursor )
					canvas.Cursor = CursorShape.Blank;
				else
					canvas.Cursor = CursorShape.ClosedHand;
			}

			var move = Vector3.Zero;

			if ( IsKeyDown( "scene.move-forward", KeyCode.W ) ) move += camera.WorldRotation.Forward;
			if ( IsKeyDown( "scene.move-backward", KeyCode.S ) ) move += camera.WorldRotation.Backward;
			if ( IsKeyDown( "scene.move-left", KeyCode.A ) ) move += camera.WorldRotation.Left;
			if ( IsKeyDown( "scene.move-right", KeyCode.D ) ) move += camera.WorldRotation.Right;
			if ( IsKeyDown( "scene.move-down", KeyCode.Q ) ) move += Vector3.Down;
			if ( IsKeyDown( "scene.move-up", KeyCode.E ) ) move += Vector3.Up;

			if ( !move.IsNearZeroLength )
			{
				move = move.Normal;

				cameraTarget ??= camera.WorldPosition;
				cameraTarget += move * RealTime.Delta * 100.0f * moveSpeed;
			}

			moved = true;
		}
		else
		{
			canvas.Cursor = CursorShape.None;

			if ( self.ControlMode != "mouse" )
			{
				self.ControlMode = "mouse";
			}

			//if ( Scene.Settings.CursorMode != "mouse" )
			//{
			//	//Scene.Settings.CursorMode = "mouse";
			//}
		}

		if ( self.Input.IsHovered && !lookMouse && Math.Abs( mouseWheelDelta.y ) > 0.001f )
		{
			const float zoomSpeed = 24.0f;
			if ( camera.Orthographic )
			{
				var canvasCursor = Application.CursorPosition - canvas.ScreenPosition;
				Vector3 worldBefore = camera.ScreenToWorld( canvasCursor );

				camera.OrthographicHeight -= mouseWheelDelta.y * zoomSpeed * 2 * (camera.OrthographicHeight / canvas.Height);
				camera.OrthographicHeight = camera.OrthographicHeight.Clamp( 32.0f, 8192.0f );

				Vector3 worldAfter = camera.ScreenToWorld( canvasCursor );
				camera.WorldPosition -= worldAfter - worldBefore;
			}
			else
			{
				camera.WorldPosition += camera.WorldRotation.Forward * mouseWheelDelta.y * zoomSpeed;
			}

			cameraTarget = default;
			moved = true;
		}

		if ( cameraTarget.HasValue )
		{
			Vector3 vel = cameraVelocity;
			camera.WorldPosition = Vector3.SmoothDamp( camera.WorldPosition, cameraTarget.Value, ref vel, EditorPreferences.CameraMovementSmoothing.Clamp( 0.0f, 1.0f ), RealTime.Delta );
			cameraVelocity = vel;

			if ( camera.WorldPosition.AlmostEqual( cameraTarget.Value, 0.01f ) )
			{
				camera.WorldPosition = cameraTarget.Value;
				cameraTarget = default;
				cameraVelocity = default;
			}
		}

		self.SetValue( "CameraTarget", cameraTarget );
		self.SetValue( "CameraVelocity", cameraVelocity );

		return moved;
	}

	/// <summary>
	/// Orbit the camera around a point into the distance.
	/// </summary>
	public static bool OrbitCamera( this Gizmo.Instance self, CameraComponent camera, Widget canvas, ref float distance )
	{
		var mouseButtons = GetMouseButtons( canvas );
		var leftMouse = mouseButtons.HasFlag( MouseButtons.Left );
		var rightMouse = mouseButtons.HasFlag( MouseButtons.Right );

		if ( !self.Input.IsHovered )
			return false;

		if ( !Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Alt ) )
			return false;

		if ( !leftMouse && !rightMouse )
			return false;

		canvas.Focus();

		var delta = ConsumeMouseDelta( canvas ) * 0.1f;
		var angles = camera.WorldRotation.Angles();

		if ( LockCursorToCanvas( canvas ) )
			delta = Vector2.Zero;

		var orbitPosition = camera.WorldPosition + camera.WorldRotation.Forward * distance;

		if ( rightMouse )
		{
			float zoomDelta = (delta.x + delta.y) * EditorPreferences.OrbitZoomSpeed;

			if ( EditorPreferences.InvertOrbitZoom )
				zoomDelta = -zoomDelta;

			if ( camera.Orthographic )
			{
				camera.OrthographicHeight += zoomDelta * (camera.OrthographicHeight / canvas.Height);
			}
			else
			{
				distance += zoomDelta;
			}

			if ( EditorPreferences.HideOrbitCursor )
				canvas.Cursor = CursorShape.Blank;
			else
				canvas.Cursor = CursorShape.SizeV;
		}
		else if ( !camera.Orthographic )
		{
			angles.roll = 0;
			angles.yaw -= delta.x;
			angles.pitch += delta.y;
			angles = angles.Normal;
			angles.pitch = angles.pitch.Clamp( -89, 89 );

			camera.WorldRotation = angles;

			if ( EditorPreferences.HideOrbitCursor )
				canvas.Cursor = CursorShape.Blank;
			else
				canvas.Cursor = CursorShape.ClosedHand;
		}
		else
		{
			return false;
		}

		distance = distance.Clamp( 0, 10000 );
		camera.WorldPosition = orbitPosition + camera.WorldRotation.Backward * distance;

		// I hate this but we need to stomp the camera lerp in first person camera when we switch back
		self.SetValue<Vector3?>( "CameraTarget", default );
		self.SetValue<Vector3>( "CameraVelocity", default );

		return true;
	}

	/// <summary>
	/// Stop this bone being procedural
	/// </summary>
	public static void BreakProceduralBone( this GameObject go )
	{
		GameObjectFlags flags = go.Flags;

		if ( !flags.Contains( GameObjectFlags.Bone ) )
			return;

		if ( flags.Contains( GameObjectFlags.ProceduralBone ) )
			return;

		flags |= GameObjectFlags.ProceduralBone;
		go.Flags = flags;
	}

	#region Dispatch Edited

	/// <summary>
	/// Run the <see cref="EditorEvent.ISceneEdited.GameObjectPreEdited"/> or <see cref="EditorEvent.ISceneEdited.ComponentPreEdited"/>
	/// event for the given property.
	/// </summary>
	public static void DispatchPreEdited( this SerializedProperty property )
	{
		if ( property.FindPathInScene() is not { } path ) return;

		foreach ( var target in path.Targets )
		{
			switch ( target )
			{
				case GameObject go:
					DispatchPreEdited( go, path.FullName );
					break;
				case Component cmp:
					DispatchPreEdited( cmp, path.FullName );
					break;
			}
		}
	}

	/// <summary>
	/// Run the <see cref="EditorEvent.ISceneEdited.GameObjectEdited"/> or <see cref="EditorEvent.ISceneEdited.ComponentEdited"/>
	/// event for the given property.
	/// </summary>
	public static void DispatchEdited( this SerializedProperty property )
	{
		if ( property.FindPathInScene() is not { } path ) return;

		foreach ( var target in path.Targets )
		{
			switch ( target )
			{
				case GameObject go:
					DispatchEdited( go, path.FullName );
					break;
				case Component cmp:
					DispatchEdited( cmp, path.FullName );
					break;
			}
		}
	}

	/// <summary>
	/// Run the <see cref="EditorEvent.ISceneEdited.GameObjectPreEdited"/> event for the given property.
	/// </summary>
	public static void DispatchPreEdited( this GameObject go, string propertyName ) =>
		EditorEvent.RunInterface<EditorEvent.ISceneEdited>( i => i.GameObjectPreEdited( go, propertyName ) );

	/// <summary>
	/// Run the <see cref="EditorEvent.ISceneEdited.ComponentPreEdited"/> event for the given property.
	/// </summary>
	public static void DispatchPreEdited( this Component cmp, string propertyName ) =>
		EditorEvent.RunInterface<EditorEvent.ISceneEdited>( i => i.ComponentPreEdited( cmp, propertyName ) );

	/// <summary>
	/// Run the <see cref="EditorEvent.ISceneEdited.GameObjectEdited"/> event for the given property.
	/// </summary>
	public static void DispatchEdited( this GameObject go, string propertyName ) =>
		EditorEvent.RunInterface<EditorEvent.ISceneEdited>( i => i.GameObjectEdited( go, propertyName ) );

	/// <summary>
	/// Run the <see cref="EditorEvent.ISceneEdited.ComponentEdited"/> event for the given property.
	/// </summary>
	public static void DispatchEdited( this Component cmp, string propertyName ) =>
		EditorEvent.RunInterface<EditorEvent.ISceneEdited>( i => i.ComponentEdited( cmp, propertyName ) );

	/// <inheritdoc cref="DispatchPreEdited(GameObject, string)"/>
	public static void DispatchPreEdited( this IEnumerable<GameObject> gos, string propertyName )
	{
		foreach ( var go in gos ) go.DispatchPreEdited( propertyName );
	}

	/// <inheritdoc cref="DispatchPreEdited(Component, string)"/>
	public static void DispatchPreEdited( this IEnumerable<Component> cmps, string propertyName )
	{
		foreach ( var cmp in cmps ) cmp.DispatchPreEdited( propertyName );
	}

	/// <inheritdoc cref="DispatchEdited(GameObject, string)"/>
	public static void DispatchEdited( this IEnumerable<GameObject> gos, string propertyName )
	{
		foreach ( var go in gos ) go.DispatchEdited( propertyName );
	}

	/// <inheritdoc cref="DispatchEdited(Component, string)"/>
	public static void DispatchEdited( this IEnumerable<Component> cmps, string propertyName )
	{
		foreach ( var cmp in cmps ) cmp.DispatchEdited( propertyName );
	}

	#endregion
}
