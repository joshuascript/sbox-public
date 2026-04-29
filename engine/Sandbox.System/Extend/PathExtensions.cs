
namespace Sandbox;

public static partial class SandboxSystemExtensions
{
	/// <summary>
	/// Returns a case-insensitive path for all operating systems.
	/// </summary>
	public static string CombinePlatformPath( params string[] path )
	{
		// linux is a dummy and everything is case sensitive
		// we just return paths how they are on windows,
		// and rebuild them if the casing varies from whats actually on the disk

		if ( !OperatingSystem.IsLinux() || path.Length == 0 )
			return System.IO.Path.Combine( path );

		var combined = System.IO.Path.Combine( path ).Replace( '\\', '/' );

		// Preserve leading '/' for absolute paths — Path.Combine("", "home") drops it,
		// turning "/home/foo" into "home/foo" and breaking every Directory.Exists check.
		string current = combined.StartsWith( '/' ) ? "/" : string.Empty;

		foreach ( string segment in combined.Split( '/' ) )
		{
			if ( string.IsNullOrEmpty( segment ) )
				continue;

			string match = System.IO.Directory.Exists( current )
				? System.IO.Directory.EnumerateDirectories( current )
					.Select( System.IO.Path.GetFileName )
					.FirstOrDefault( e => string.Equals( e, segment, StringComparison.OrdinalIgnoreCase ) )
				: null;

			current = System.IO.Path.Combine( current, match ?? segment );
		}

		return current;
	}
}
