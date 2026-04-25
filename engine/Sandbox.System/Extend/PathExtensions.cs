
namespace Sandbox;

public static partial class SandboxSystemExtensions
{
    /// <summary>
    /// Returns a case-insensitive path for all operating systems.
    /// </summary>
    public static string CombinePlatformPath(params string[] path)
    {
        // linux is a dummy and everything is case sensitive
        // we just return paths how they are on windows,
        // and rebuild them if the casing varies from whats actually on the disk

        if ( !OperatingSystem.IsLinux() || path.Length == 0 )
            return System.IO.Path.Combine( path );

        string current = string.Empty;

        foreach ( string segment in System.IO.Path.Combine( path ).Replace( '\\', '/' ).Split( '/' ) )
        {
            if ( string.IsNullOrEmpty( segment ) )
                continue;

            string? match = System.IO.Directory.Exists( current )
                ? System.IO.Directory.EnumerateDirectories( current )
                    .Select( System.IO.Path.GetFileName )
                    .FirstOrDefault( e => string.Equals( e, segment, StringComparison.OrdinalIgnoreCase ) )
                : null;

            current = System.IO.Path.Combine( current, match ?? segment );
        }

        return current;
    }
}
