using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

/*
 * 
 * This dll is only called in instances where we want to initialize .net from c++.
 * 
 * We purposefully don't reference any dlls here because it's loaded in its own isolated
 * context, so if we load sandbox.engine, it technically won't be loaded in the main context
 * and stuff will be weird.
 * 
 * Eventually we might not need this. We might make all the exe's dotnet exes - in which case
 * we'll make the exe call Interop.InitializeAll directly to set everything up.
 * 
 * */

internal static class NetCore
{
	[UnmanagedCallersOnly]
	public static int InitializeEngine( IntPtr gameFolderPtr )
	{
		//
		// This should contain the minimal amount of logic needed to call InitializeInterop
		//

		var gameFolder = Marshal.PtrToStringUTF8( gameFolderPtr );
		var separator = System.OperatingSystem.IsWindows() ? "\\" : "/";
		var managedFolder = $"{gameFolder}{separator}bin{separator}managed{separator}";

		var assembly = Assembly.LoadFrom( $"{managedFolder}Sandbox.Engine.dll" );
		var type = assembly.GetTypes().Where( x => x.Name == "NetCore" ).FirstOrDefault();
		var method = type.GetMethod( "InitializeInterop", BindingFlags.Static | BindingFlags.NonPublic );

		method.Invoke( null, new object[] { gameFolder } );

		return 0;
	}
}
