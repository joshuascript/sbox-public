using System;
using System.Runtime;

namespace Sandbox;

public class TestAppSystem : AppSystem
{
	public override void Init()
	{
		GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
		var GameFolder = System.Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE", EnvironmentVariableTarget.Process );
		if ( GameFolder is null ) throw new Exception( "FACEPUNCH_ENGINE not found" );

		NetCore.InitializeInterop( GameFolder );

		var platform = OperatingSystem.IsWindows() ? "win64"
			: OperatingSystem.IsLinux() ? (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "linuxsteamrtarm64" : "linuxsteamrt64")
			: "win64";
		var nativeDllPath = System.IO.Path.Combine( GameFolder, "bin", platform ) + System.IO.Path.DirectorySeparatorChar;
		var path = System.Environment.GetEnvironmentVariable( "PATH" );
		path = $"{nativeDllPath}{(OperatingSystem.IsWindows() ? ";" : ":")}{path}";
		System.Environment.SetEnvironmentVariable( "PATH", path );

		CreateGame();

		var createInfo = new AppSystemCreateInfo()
		{
			Flags = AppSystemFlags.IsGameApp | AppSystemFlags.IsUnitTest
		};

		InitGame( createInfo, "" );
	}
}
