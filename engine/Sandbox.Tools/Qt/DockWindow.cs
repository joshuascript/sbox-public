using Sandbox.Engine;

namespace Editor
{
	/// <summary>
	/// A window that is built from docking windows
	/// </summary>
	public partial class DockWindow : Window
	{
		/// <summary>
		/// The dock manager for this window, that is automatically created.
		/// </summary>
		public DockManager DockManager { get; init; }

		public DockWindow()
		{
			DockManager = new DockManager( this );
			Canvas = DockManager;
		}

		internal DockWindow( Native.CFramelessMainWindow ptr ) : base( ptr )
		{
			DockManager = new DockManager( this );
			Canvas = DockManager;
		}

		/// <summary>
		/// Override to apply a default layout to your window. This is called automatically from
		/// RestoreFromStateCookie if there is no cookie set.
		/// </summary>
		protected virtual void RestoreDefaultDockLayout()
		{

		}

		public override void RestoreFromStateCookie()
		{
			if ( string.IsNullOrWhiteSpace( StateCookie ) )
				return;

			base.RestoreFromStateCookie();

			var state = ProjectCookie.GetString( $"Window.{StateCookie}.Dock", null );
			if ( string.IsNullOrWhiteSpace( state ) || (System.OperatingSystem.IsLinux() && state.Trim() == "{}") )
			{
				RestoreDefaultDockLayout();
				return;
			}

			DockManager.State = state;
		}

		[Event( "app.exit" )]
		public override void SaveToStateCookie()
		{
			if ( string.IsNullOrWhiteSpace( StateCookie ) )
				return;

			base.SaveToStateCookie();

			var state = DockManager.State;

			ProjectCookie.SetString( $"Window.{StateCookie}.Dock", state );
		}

		/// <summary>
		/// Create a viewmenu dynamically, with common options
		/// </summary>
		public void CreateDynamicViewMenu( Menu menu )
		{
			menu.Clear();

			IToolsDll.Current?.RunEvent( "tools.editorwindow.createview", menu );

			menu.AddOption( "Reset Layout", "restart_alt", RestoreDefaultDockLayout );
			menu.AddSeparator();

			foreach ( var dock in DockManager.DockTypes.OrderBy( x => x.Title ) )
			{
				var o = menu.AddOption( dock.Title, dock.Icon );
				o.Checkable = true;
				o.Checked = DockManager.IsDockOpen( dock.Title );
				o.Toggled += ( b ) => DockManager.SetDockState( dock.Title, b );
			}

			IToolsDll.Current?.RunEvent( "tools.editorwindow.postcreateview", menu );
		}
	}
}
