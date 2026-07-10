using System;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Editor;

partial class StandaloneExporter
{
	class IconUpdater
	{
	[DllImport( "kernel32.dll", SetLastError = true, CharSet = CharSet.Auto )]
	private static extern IntPtr BeginUpdateResource( string pFileName, [MarshalAs( UnmanagedType.Bool )] bool bDeleteExistingResources );

	[DllImport( "kernel32.dll", SetLastError = true, CharSet = CharSet.Auto )]
	[return: MarshalAs( UnmanagedType.Bool )]
	private static extern bool UpdateResource( IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, byte[] lpData, uint cbData );

	[DllImport( "kernel32.dll", SetLastError = true )]
	[return: MarshalAs( UnmanagedType.Bool )]
	private static extern bool EndUpdateResource( IntPtr hUpdate, [MarshalAs( UnmanagedType.Bool )] bool fDiscard );

		private const int RT_ICON = 3;
		private const int RT_GROUP_ICON = 14;

		public static void UpdateExeIcon( string exePath, string icoPath )
		{
			byte[] icoBytes = File.ReadAllBytes( icoPath );
			var iconData = ParseIcoFile( icoBytes );

			IntPtr hUpdate = BeginUpdateResource( exePath, false );
			if ( hUpdate == IntPtr.Zero )
				throw new Win32Exception();

			try
			{
				// Update the group icon
				if ( !UpdateResource( hUpdate, new IntPtr( RT_GROUP_ICON ), new IntPtr( 1 ), 0, iconData.GroupData, (uint)iconData.GroupData.Length ) )
					throw new Win32Exception();

				// Update individual icons
				for ( int i = 0; i < iconData.IconData.Length; i++ )
				{
					if ( !UpdateResource( hUpdate, new IntPtr( RT_ICON ), new IntPtr( i + 1 ), 0, iconData.IconData[i], (uint)iconData.IconData[i].Length ) )
						throw new Win32Exception();
				}

				if ( !EndUpdateResource( hUpdate, false ) )
					throw new Win32Exception();
			}
			catch
			{
				EndUpdateResource( hUpdate, true );
				throw;
			}
		}

		private class IconResources
		{
			public byte[] GroupData { get; set; }
			public byte[][] IconData { get; set; }
		}

		private static IconResources ParseIcoFile( byte[] icoBytes )
		{
			using ( var ms = new MemoryStream( icoBytes ) )
			using ( var reader = new BinaryReader( ms ) )
			{
				// Read ICO header
				reader.ReadInt16(); // Reserved (0)
				var type = reader.ReadInt16(); // Type (1 for ICO)
				var count = reader.ReadInt16(); // Number of images

				if ( type != 1 )
					throw new ArgumentException( "Invalid ICO file" );

				var entries = new IcoDirectoryEntry[count];
				var images = new byte[count][];

				// Read directory entries
				for ( int i = 0; i < count; i++ )
				{
					entries[i] = new IcoDirectoryEntry
					{
						Width = reader.ReadByte(),
						Height = reader.ReadByte(),
						ColorCount = reader.ReadByte(),
						Reserved = reader.ReadByte(),
						Planes = reader.ReadInt16(),
						BitCount = reader.ReadInt16(),
						BytesInRes = reader.ReadInt32(),
						ImageOffset = reader.ReadInt32()
					};
				}

				// Read image data
				for ( int i = 0; i < count; i++ )
				{
					var entry = entries[i];
					images[i] = new byte[entry.BytesInRes];
					var currentPosition = ms.Position;
					ms.Position = entry.ImageOffset;
					ms.Read( images[i], 0, entry.BytesInRes );
					ms.Position = currentPosition;
				}

				// Create group icon data
				using ( var groupMs = new MemoryStream() )
				using ( var writer = new BinaryWriter( groupMs ) )
				{
					writer.Write( (short)0 ); // Reserved
					writer.Write( (short)1 ); // Type
					writer.Write( (short)count ); // Count

					for ( int i = 0; i < count; i++ )
					{
						writer.Write( entries[i].Width );
						writer.Write( entries[i].Height );
						writer.Write( entries[i].ColorCount );
						writer.Write( entries[i].Reserved );
						writer.Write( entries[i].Planes );
						writer.Write( entries[i].BitCount );
						writer.Write( entries[i].BytesInRes );
						writer.Write( (short)(i + 1) ); // ID
					}

					return new IconResources
					{
						GroupData = groupMs.ToArray(),
						IconData = images
					};
				}
			}
		}

		private class IcoDirectoryEntry
		{
			public byte Width { get; set; }
			public byte Height { get; set; }
			public byte ColorCount { get; set; }
			public byte Reserved { get; set; }
			public short Planes { get; set; }
			public short BitCount { get; set; }
			public int BytesInRes { get; set; }
			public int ImageOffset { get; set; }
		}
	}
}
