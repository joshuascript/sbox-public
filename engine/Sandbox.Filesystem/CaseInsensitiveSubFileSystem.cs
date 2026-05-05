using Zio;
using Zio.FileSystems;

namespace Sandbox;

/// <summary>
/// A <see cref="SubFileSystem"/> that performs the delegate-path prefix check using
/// <see cref="StringComparison.OrdinalIgnoreCase"/>, so paths bubbling up from a
/// case-sensitive disk (Linux) won't fail the rooting check just because their casing
/// doesn't match the SubPath the mount was constructed with.
/// </summary>
internal sealed class CaseInsensitiveSubFileSystem : SubFileSystem
{
	public CaseInsensitiveSubFileSystem( IFileSystem fileSystem, UPath subPath, bool owned = true )
		: base( fileSystem, subPath, owned ) { }

	protected override UPath ConvertPathFromDelegate( UPath path )
	{
		// almost identical to the base method, but

		// compares first path string of sub fs path
		// to partial path in physical fs

		// CreateAndMount gives manual paths
		// and folder might not match what engine has or what directories bootstrap rebuilds
		// it varies very often

		// i.e. disk is /menu/code/AvatarEditor/UI/Workshop/WorkshopPackageList.razor.scss but got /menu/Code
		//Log.Info($"{path.FullName}");
		var fullPath = path.FullName;
		var sub = SubPath.FullName;

		if ( !fullPath.StartsWith( sub, StringComparison.OrdinalIgnoreCase )
			|| (fullPath.Length > sub.Length && fullPath[sub.Length] != UPath.DirectorySeparator) ) // if the sub path at least equals the full path
		{
			throw new InvalidOperationException( $"The path `{path}` returned by the delegate filesystem is not rooted to the subpath `{SubPath}`" );
		}

		var remainder = fullPath.Substring( sub.Length );
		var result = remainder.Length == 0 ? UPath.Root : new UPath( remainder );
		//Log.Info( $"[Linux SFS] ConvertPathFromDelegate sub='{sub}' full='{fullPath}' -> '{result}'" );
		return result;
	}
}
