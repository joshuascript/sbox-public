{
	description = "s&box Linux development environment";

	inputs = {
		nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
		flake-utils.url = "github:numtide/flake-utils";
	};

	outputs = { self, nixpkgs, flake-utils }:
		flake-utils.lib.eachSystem [ "x86_64-linux" ] ( system:
			let
				pkgs = import nixpkgs {
					inherit system;
				};

				dotnet = pkgs.dotnet-sdk_10;

				runtimeLibraries = with pkgs; [
					gcc.cc.lib
					zlib
					pcre2
					glib
					dbus
					util-linux
					fontconfig
					freetype
					harfbuzz
					libjpeg_turbo.out
					libpng
					libxkbcommon
					libx11
					libxext
					libxcb
					libsm
					libice
					xcbutilwm
					xcbutilimage
					xcbutilkeysyms
					xcbutilrenderutil
					libxrandr
					libxrender
					libxfixes
					libxi
					vulkan-loader
				];
			in
			{
				devShells.default = pkgs.mkShell {
					packages = with pkgs; [
						dotnet
						bashInteractive
						git
						curl
						cacert
						pkg-config
						gnumake
						gcc
						glibc.bin
						vulkan-tools
					] ++ runtimeLibraries;

					shellHook = ''
						export DOTNET_ROOT="${dotnet}/share/dotnet"
						export SSL_CERT_FILE="${pkgs.cacert}/etc/ssl/certs/ca-bundle.crt"
						export LD_LIBRARY_PATH="${pkgs.lib.makeLibraryPath runtimeLibraries}''${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"

						for opengl_driver in /run/opengl-driver/lib /run/opengl-driver-32/lib; do
							if [ -d "$opengl_driver" ]; then
								export LD_LIBRARY_PATH="$opengl_driver:$LD_LIBRARY_PATH"
							fi
						done
					'';
				};
			}
		);
}
