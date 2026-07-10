namespace Editor
{

	internal static class EngineTools
	{
		public record ToolDescription( string Name, string Description, string Library, string Icon );

		static List<ToolDescription> AllTools = new()
		{
			new ToolDescription( "Hammer",                  "For editing maps",                 "hammer",               "handyman" ),
			new ToolDescription( "Material Editor",         "For editing materials",            "met",                  "insert_photo" ),
			new ToolDescription( "Model Editor",            "For editing models",               "modeldoc_editor",      "view_in_ar" ),
			new ToolDescription( "Animgraph Editor",        "For editing animation graphs",     "animgraph_editor",     "directions_run" ),
		};

		/// <summary>
		/// Accessor to get tools available on this machine.
		/// </summary>
		public static IReadOnlyList<ToolDescription> All
		{
			get
			{
				return AllTools.AsReadOnly();
			}
		}

		public static void ShowTool( string name )
		{
			var tool = All.FirstOrDefault( x => x.Name == name );
		var ext = System.OperatingSystem.IsWindows() ? ".dll" : ".so";
		Native.ToolGlue.ShowTool( $"tools/{tool.Library}{ext}" );
		}
	}
}
