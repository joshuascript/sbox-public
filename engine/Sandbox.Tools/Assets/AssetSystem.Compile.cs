using NativeEngine;
using Sandbox.Resources;
using System;
using System.Runtime.InteropServices;

namespace Editor;

public static partial class AssetSystem
{
	internal unsafe static bool TryManagedCompile( IResourceCompilerContext _context )
	{
		using var context = new ResourceCompileContextImp( _context );

		var filename = context.AbsolutePath;
		var extension = System.IO.Path.GetExtension( filename ).Trim( '.' );

		var assetType = AssetType.Find( extension );
		if ( assetType is null )
		{
			Log.Info( $"Unknown asset type for {extension} - skipping compile!" );
			return false;
		}

		var compilers = EditorTypeLibrary.GetTypes<ResourceCompiler>().Where( x => !x.IsInterface && !x.IsAbstract ).ToArray();
		var chosen = compilers.Where( x => x.GetAttributes<ResourceCompiler.ResourceIdentityAttribute>().Any( y => y.Name == extension ) ).FirstOrDefault();

		// do we have a specific compiler?
		if ( chosen is not null )
		{
			var compiler = chosen.Create<ResourceCompiler>();
			compiler.SetContext( context );
			return compiler.CompileInternal();
		}

		// this is a game resource
		if ( assetType.IsGameResource )
		{
			CompileGameResource( context );
			return true;
		}

		// Nothing!

		return false;
	}

	static void CompileGameResource( ResourceCompileContext context )
	{
		// Get the json contents
		var jsonString = System.IO.File.ReadAllText( context.AbsolutePath );

		//
		// Pre Feb-2023 we saved GameResources to keyvalues. Keep support for loading this
		// format for a while by loading those keyvalues and converting them to json.
		//
		if ( jsonString.StartsWith( '<' ) )
		{
			log.Trace( $"KeyValue format detected ({context.AbsolutePath}) - converting to json" );
			var kv = EngineGlue.LoadKeyValues3( jsonString );
			jsonString = EngineGlue.KeyValues3ToJson( kv.FindOrCreateMember( "data" ) );
			kv.DeleteThis();
		}

		jsonString = context.ScanJson( jsonString );

		context.Data.Write( jsonString );

		// Write binary blob data to BLOB block if companion file exists
		var blobPath = context.AbsolutePath + "_d";
		if ( System.IO.File.Exists( blobPath ) )
		{
			context.AddCompileReference( blobPath );

			var blobData = System.IO.File.ReadAllBytes( blobPath );
			unsafe
			{
				fixed ( byte* ptr = blobData )
				{
					context.WriteBlock( BlobDataSerializer.CompiledBlobName, (IntPtr)ptr, blobData.Length );
				}
			}
		}
	}

	static bool CanUseNativeResourceCompiler( string path )
	{
		// On Linux the Wine-backed resourcecompiler bridge handles all file-based
		// resource types that the Windows resourcecompiler.exe supports.
		return true;
	}

	[UnmanagedFunctionPointer( CallingConvention.Cdecl, CharSet = CharSet.Ansi )]
	delegate int AnvilGenerateResourceFileDelegate( string path );

	static bool triedLinuxResourceCompilerBridge;
	static AnvilGenerateResourceFileDelegate linuxResourceCompilerBridge;

	static bool CompileLinuxResourceWithBridge( string path )
	{
		if ( !OperatingSystem.IsLinux() )
			return false;

		if ( linuxResourceCompilerBridge is null && !triedLinuxResourceCompilerBridge )
		{
			triedLinuxResourceCompilerBridge = true;

			if ( !NativeLibrary.TryLoad( "libresourcecompiler.so", out var handle ) )
			{
				var nativePath = System.IO.Path.GetFullPath( System.IO.Path.Combine( AppContext.BaseDirectory, "..", "linuxsteamrt64", "libresourcecompiler.so" ) );
				NativeLibrary.TryLoad( nativePath, out handle );
			}

			if ( handle != IntPtr.Zero && NativeLibrary.TryGetExport( handle, "AnvilGenerateResourceFile", out var export ) )
			{
				linuxResourceCompilerBridge = Marshal.GetDelegateForFunctionPointer<AnvilGenerateResourceFileDelegate>( export );
			}
		}

		if ( linuxResourceCompilerBridge is null )
		{
			log.Warning( $"Linux resource compiler bridge is unavailable; can't compile {path}" );
			return false;
		}

		var compiled = linuxResourceCompilerBridge( path ) != 0;
		if ( !compiled )
			log.Warning( $"Linux resource compiler bridge failed to compile {path}" );

		return compiled;
	}


	/// <summary>
	/// Compile a resource from text.
	/// </summary>
	public static bool CompileResource( string path, string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) )
			return false;

		if ( !CanUseNativeResourceCompiler( path ) )
			return false;

		if ( OperatingSystem.IsLinux() )
			return CompileLinuxResourceWithBridge( path );

		return IResourceCompilerSystem.GenerateResourceFile( path, text );
	}

	/// <summary>
	/// Compile a resource from binary data.
	/// </summary>
	public static unsafe bool CompileResource( string path, ReadOnlySpan<byte> data )
	{
		if ( data.Length == 0 )
			return false;

		if ( !CanUseNativeResourceCompiler( path ) )
			return false;

		if ( OperatingSystem.IsLinux() )
			return CompileLinuxResourceWithBridge( path );

		fixed ( byte* dataPtr = data )
		{
			return IResourceCompilerSystem.GenerateResourceFile( path, (IntPtr)dataPtr, data.Length );
		}
	}
}
