using System.IO;

namespace Sandbox;

/// <summary>
/// Holds the active project's on-disk root and a <see cref="LocalFileSystem"/>
/// rooted at it. Wire <see cref="RootDirectory"/> from your project bootstrap
/// (e.g. <c>Project.LoadMinimal</c>); callers can then route raw filesystem
/// operations through <see cref="FileSystem"/>, or use <see cref="Combine"/>
/// to resolve raw paths to the casing actually present on disk.
/// </summary>
public static class PlatformFileSystem
{
	private static DirectoryInfo _rootDirectory;
	private static LocalFileSystem _fileSystem;

	private static readonly StringComparison _pathComparison = OperatingSystem.IsLinux()
		? StringComparison.OrdinalIgnoreCase
		: StringComparison.Ordinal;

	/// <summary>
	/// The on-disk root that <see cref="FileSystem"/> resolves paths against.
	/// Assigning a new value rebuilds <see cref="FileSystem"/> and disposes the
	/// previous one. Assigning <see langword="null"/> tears it down.
	/// </summary>
	public static DirectoryInfo RootDirectory
	{
		get => _rootDirectory;
		set
		{
			if ( _rootDirectory?.FullName == value?.FullName ) return;

			_fileSystem?.Dispose();
			_rootDirectory = value;
			_fileSystem = value is null ? null : new LocalFileSystem( value.FullName );
		}
	}

	/// <summary>
	/// The <see cref="LocalFileSystem"/> rooted at <see cref="RootDirectory"/>,
	/// or <see langword="null"/> if <see cref="RootDirectory"/> hasn't been set.
	/// </summary>
	public static BaseFileSystem FileSystem => _fileSystem;

	/// <summary>
	/// Combine <paramref name="parts"/> into a single path and resolve to the casing
	/// actually present on disk. On Windows/macOS this is a pass-through; on Linux,
	/// when the combined path falls under <see cref="RootDirectory"/>, it walks the
	/// path through <see cref="FileSystem"/>'s case-insensitive resolver.
	///
	/// If <see cref="RootDirectory"/> isn't set, or the combined path falls outside
	/// of it, the raw combined path is returned unresolved.
	/// </summary>
	public static string Combine( params string[] parts )
	{
		var combined = System.IO.Path.Combine( parts );

		if ( _fileSystem is null || _rootDirectory is null )
			return combined;

		var root = _rootDirectory.FullName;
		if ( !combined.StartsWith( root, _pathComparison ) )
			return combined;

		var relative = combined.Substring( root.Length );
		var resolved = _fileSystem.GetFullPath( relative );
		Log.Info( $"[PlatformFileSystem] {combined} -> {resolved}" );
		return resolved;
	}
}
