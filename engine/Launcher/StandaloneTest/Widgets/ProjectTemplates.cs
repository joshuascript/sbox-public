using Sandbox.DataModel;

namespace Editor;

internal class ProjectTemplates : Widget
{
	public ProjectTemplatesListView ListView { get; set; }

	public ProjectTemplates( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 8;

		ListView = new ProjectTemplatesListView( this );
		ListView.SetSizeMode( SizeMode.Default, SizeMode.Default );

		Layout.Add( ListView, 1 );
	}
}

internal class ProjectTemplatesListView : Widget
{
	/// <summary>
	/// The current template, used by the addon creator.
	/// </summary>
	public ProjectTemplate ChosenTemplate { get; set; }

	/// <summary>
	/// Relative to the game directory.
	/// </summary>
	const string TemplatesDirectory = "/templates";

	readonly List<ProjectTemplate> Templates = new();
	readonly List<(TemplateRow Row, ProjectTemplate Template)> Rows = new();

	public ProjectTemplatesListView( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Spacing = 4;
		Layout.Margin = 8;
		SetStyles( $"background-color: {Theme.WidgetBackground.Hex};" );

		FindLocalTemplates();

		foreach ( var template in Templates.OrderBy( x => x.Order ) )
		{
			AddTemplateRow( template );
		}
	}

	void AddTemplateRow( ProjectTemplate template )
	{
		var row = Layout.Add( new TemplateRow( template ) );
		row.Clicked = () => SelectTemplate( template );

		Rows.Add( (row, template) );

		if ( ChosenTemplate is null )
			SelectTemplate( template );
	}

	void SelectTemplate( ProjectTemplate template )
	{
		ChosenTemplate = template;

		foreach ( var (row, rowTemplate) in Rows )
		{
			row.Selected = rowTemplate == template;
		}
	}

	protected void FindLocalTemplates()
	{
		if ( !FileSystem.Root.DirectoryExists( TemplatesDirectory ) )
		{
			AddFallbackTemplates();
			return;
		}

		var directories = FileSystem.Root.FindDirectory( TemplatesDirectory );

		foreach ( var directory in directories )
		{
			var templateRoot = $"{TemplatesDirectory}/{directory}";
			var addonPath = $"{templateRoot}/$ident.sbproj";

			if ( !FileSystem.Root.FileExists( addonPath ) )
				continue;

			var addon = Json.Deserialize<ProjectConfig>( FileSystem.Root.ReadAllText( addonPath ) );
			if ( addon == null )
				continue;

			if ( addon.Type == "library" )
				continue;

			Templates.Add( new ProjectTemplate( addon, templateRoot ) );
		}

		if ( Templates.Count == 0 )
			AddFallbackTemplates();
	}

	void AddFallbackTemplates()
	{
		AddFallbackTemplate( "Game - Empty", "sports_esports", "The bare minimum required to create a game in s&box", "game", 0 );
		AddFallbackTemplate( "Game - Player Controller", "directions_run", "Contains a First Person, Third Person, and Top-Down Example", "game", 1 );
		AddFallbackTemplate( "Addon", "extension", "Create a custom addon for any game you wish to target", "addon", 2 );
		AddFallbackTemplate( "Map", "map", "Create a map that works for most games", "map", 3 );
		AddFallbackTemplate( "Sandbox Game Addon", "directions_car", "Create a custom entity for Sandbox Game", "addon", 4 );
	}

	void AddFallbackTemplate( string title, string icon, string description, string type, int order )
	{
		var config = new ProjectConfig { Title = title, Type = type, Schema = 1 };
		var template = new ProjectTemplate( config, null )
		{
			Icon = icon,
			Description = description,
			Order = order
		};

		Templates.Add( template );
	}

	class TemplateRow : Widget
	{
		public Action Clicked { get; set; }

		bool selected;
		public bool Selected
		{
			get => selected;
			set
			{
				selected = value;
				SetStyles( $"background-color: {(selected ? Theme.SelectedBackground.Hex : Theme.WidgetBackground.Hex)}; border-radius: 4px;" );
				Update();
			}
		}

		public TemplateRow( ProjectTemplate template ) : base( null )
		{
			FixedHeight = 48;
			Cursor = CursorShape.Finger;
			Layout = Layout.Row();
			Layout.Margin = 8;
			Layout.Spacing = 8;

			var icon = Layout.Add( new IconLabel( template.Icon ) );
			icon.IconSize = 24;
			icon.Foreground = Theme.Text;
			icon.FixedWidth = 36;

			var text = Layout.AddColumn( 1 );
			text.Spacing = 1;

			var title = text.Add( new Label( template.Title ) );
			title.SetStyles( $"color: {Theme.Text.Hex}; font-weight: bold;" );

			var description = text.Add( new Label( template.Description ) );
			description.SetStyles( $"color: {Theme.TextControl.WithAlpha( 0.65f ).Hex};" );

			Selected = false;
		}

		protected override void OnMousePress( MouseEvent e )
		{
			if ( e.LeftMouseButton )
			{
				Clicked?.Invoke();
				e.Accepted = true;
				return;
			}

			base.OnMousePress( e );
		}
	}
}
