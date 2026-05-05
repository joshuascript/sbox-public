using System.Collections.Concurrent;
using System.IO;
using Zio;
using Zio.FileSystems;

namespace Sandbox;

/// <summary>
/// A physical filesystem that resolves paths case-insensitively on Linux.
/// </summary>
internal sealed class CaseInsensitivePhysicalFileSystem : PhysicalFileSystem
{
	/// <summary>
	/// Real directory path -> case-insensitive name lookup (name -> actual on-disk name).
	/// </summary>
	private readonly ConcurrentDictionary<string, Dictionary<string, string>> _directoryCache = new( StringComparer.Ordinal );


	protected override string ConvertPathToInternalImpl( UPath path )
	{
		var resolved = ResolvePathCasing( base.ConvertPathToInternalImpl( path ) );
		//Log.Info( $"[Linux CIPhys] ConvertPathToInternal {path} -> {resolved}" );
		return resolved;
	}

	/// <summary>
	/// Walk each component of <paramref name="path"/> and resolve it to the actual
	/// on-disk casing. Returns the original path if any component can't be matched,
	/// letting the OS produce a normal "file not found" error.
	/// </summary>
	private string ResolvePathCasing( string path )
	{
		if ( path.Length < 2 )
			return path;

		// Fast path: if the OS finds it with the casing as-given, no resolution needed.
		// One stat() (warm dentry cache: sub-microsecond) beats the per-component walk
		// for any path that's already correctly cased.
		if ( File.Exists( path ) || Directory.Exists( path ) )
		{
			//Log.Info( $"[Linux CIPhys] ResolvePathCasing exists-fastpath {path}" );
			return path;
		}

		var components = path.Split( '/', StringSplitOptions.RemoveEmptyEntries );
		var resolvedDir = "/";

		for ( var i = 0; i < components.Length; i++ )
		{
			var entries = GetDirectoryEntries( resolvedDir );
			if ( entries is null || !entries.TryGetValue( components[i], out var realName ) )
			{
				//Log.Info( $"[Linux CIPhys] ResolvePathCasing unresolved at component '{components[i]}' under '{resolvedDir}' for {path}" );
				return path;
			}

			resolvedDir = resolvedDir == "/"
				? $"/{realName}"
				: $"{resolvedDir}/{realName}";
		}

		//Log.Info( $"[Linux CIPhys] ResolvePathCasing resolved {path} -> {resolvedDir}" );
		return resolvedDir;
	}

	/// <summary>
	/// Returns a case-insensitive lookup of names in <paramref name="directory"/>,
	/// mapping each name to its real on-disk casing. Returns null if the directory
	/// doesn't exist.
	/// </summary>
	private Dictionary<string, string> GetDirectoryEntries( string directory )
	{
		if ( _directoryCache.TryGetValue( directory, out var entries ) )
			return entries;

		if ( !Directory.Exists( directory ) )
		{
			//Log.Info( $"[Linux CIPhys] GetDirectoryEntries miss (does-not-exist) {directory}" );
			return null;
		}

		try
		{
			var infos = new DirectoryInfo( directory ).GetFileSystemInfos();
			var lookup = new Dictionary<string, string>( infos.Length, StringComparer.OrdinalIgnoreCase );

			foreach ( var info in infos )
				lookup.TryAdd( info.Name, info.Name );

			_directoryCache.TryAdd( directory, lookup );
			//Log.Info( $"[Linux CIPhys] GetDirectoryEntries scanned {directory} ({infos.Length} entries)" );
			return lookup;
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[Linux CIPhys] GetDirectoryEntries exception for {directory}: {ex.Message}" );
			return null;
		}
	}

	//
	// Cache invalidation for mutations
	//

	private void InvalidateParent( string resolvedPath )
	{
		var parent = Path.GetDirectoryName( resolvedPath );

		if ( parent is not null )
			_directoryCache.TryRemove( parent, out _ );
	}

	protected override void CreateDirectoryImpl( UPath path )
	{
		base.CreateDirectoryImpl( path );
		var resolved = ConvertPathToInternal( path );
		InvalidateParent( resolved );
		_directoryCache.TryRemove( resolved, out _ );
	}

	protected override void DeleteDirectoryImpl( UPath path, bool isRecursive )
	{
		var resolved = ConvertPathToInternal( path );
		base.DeleteDirectoryImpl( path, isRecursive );
		InvalidateParent( resolved );
		_directoryCache.TryRemove( resolved, out _ );

		// Recursive delete also wipes every subdirectory under `resolved`. Drop their
		// cached listings so we don't return entries for paths that no longer exist.
		if ( isRecursive )
		{
			var prefix = resolved + "/";
			foreach ( var key in _directoryCache.Keys )
			{
				if ( key.StartsWith( prefix, StringComparison.Ordinal ) )
					_directoryCache.TryRemove( key, out _ );
			}
		}
	}

	protected override void DeleteFileImpl( UPath path )
	{
		var resolved = ConvertPathToInternal( path );
		base.DeleteFileImpl( path );
		InvalidateParent( resolved );
	}

	protected override void MoveDirectoryImpl( UPath srcPath, UPath destPath )
	{
		var resolvedSrc = ConvertPathToInternal( srcPath );
		base.MoveDirectoryImpl( srcPath, destPath );
		InvalidateParent( resolvedSrc );
		InvalidateParent( ConvertPathToInternal( destPath ) );
		_directoryCache.TryRemove( resolvedSrc, out _ );
	}

	protected override void MoveFileImpl( UPath srcPath, UPath destPath )
	{
		var resolvedSrc = ConvertPathToInternal( srcPath );
		base.MoveFileImpl( srcPath, destPath );
		InvalidateParent( resolvedSrc );
		InvalidateParent( ConvertPathToInternal( destPath ) );
	}

	protected override void CopyFileImpl( UPath srcPath, UPath destPath, bool overwrite )
	{
		base.CopyFileImpl( srcPath, destPath, overwrite );
		InvalidateParent( ConvertPathToInternal( destPath ) );
	}

	protected override Stream OpenFileImpl( UPath path, FileMode mode, FileAccess access, FileShare share )
	{
		var stream = base.OpenFileImpl( path, mode, access, share );

		if ( mode is FileMode.Create or FileMode.CreateNew or FileMode.OpenOrCreate )
			InvalidateParent( ConvertPathToInternal( path ) );

		return stream;
	}

	/// <summary>
	/// Clear all caches (e.g. after external file changes).
	/// </summary>
	internal void InvalidateCache()
	{
		_directoryCache.Clear();
	}
}
