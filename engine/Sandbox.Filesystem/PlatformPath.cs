using Zio;
using Zio.FileSystems;

namespace Sandbox;

/// <summary>
/// Static path-resolution helpers that route through Zio's case-insensitive
/// filesystem on Linux, and pass through unchanged on case-insensitive platforms.
/// </summary>
public static class PlatformPath
{
	private static readonly PhysicalFileSystem _fs = OperatingSystem.IsLinux()
		? new CaseInsensitivePhysicalFileSystem()
		: new PhysicalFileSystem();

	/// <summary>
	/// Combine <paramref name="parts"/> into a single path and resolve to the casing
	/// actually present on disk. On Windows/macOS this is a pass-through;
	/// on Linux it walks the path through the cached case-insensitive resolver.
	/// </summary>
	public static string Combine( params string[] parts )
	{
		UPath path = System.IO.Path.Combine( parts );
		var resolved = _fs.ConvertPathToInternal( path );
		Log.Info( $"[PlatformPath] {path} -> {resolved}" );
		return resolved;
	}
}
