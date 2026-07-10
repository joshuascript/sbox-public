using System;

namespace Editor
{
	/// <summary>
	/// A simple button widget.
	/// </summary>
	public class Button : Widget
	{
		internal Native.QPushButton _button;

		public Action Clicked;
		public Action Pressed;
		public Action Released;
		public Action Toggled;

		/// <summary>
		/// Text on the button.
		/// </summary>
		public string Text
		{
			get => _button.text();
			set => _button.setText( value );
		}

		Color _tint = "#48494c";

		/// <summary>
		/// The tint of the button color
		/// </summary>
		public Color Tint
		{
			get => _tint;
			set
			{
				if ( _tint == value ) return;
				_tint = value;
				Update();
			}
		}

		/// <summary>
		/// Whether this button is checked. See <see cref="IsToggle"/>.
		/// </summary>
		public bool IsChecked
		{
			get => _button.isChecked();
			set => _button.setChecked( value );
		}

		/// <summary>
		/// Whether this button can be toggled on or off. See <see cref="IsChecked"/>.
		/// </summary>
		public bool IsToggle
		{
			get => _button.isCheckable();
			set => _button.setCheckable( value );
		}

		internal Button( Native.QPushButton ptr ) : base( false )
		{
			NativeInit( ptr );
		}

		public Button( Widget parent = null ) : base( false )
		{
			Sandbox.InteropSystem.Alloc( this );
			var ptr = CPushButton.CreatePushButton( parent?._widget ?? default, this );
			NativeInit( ptr );

			Cursor = CursorShape.Finger;
		}

		public Button( string title, Widget parent = null ) : this( parent )
		{
			if ( title != null )
				Text = title;
		}
		public Button( string title, string icon, Widget parent = null ) : this( title, parent )
		{
			if ( icon != null )
				Icon = icon;
		}

		internal override void NativeInit( IntPtr ptr )
		{
			_button = ptr;
			base.NativeInit( ptr );
		}
		internal override void NativeShutdown()
		{
			base.NativeShutdown();

			_button = default;
		}

		protected virtual void OnClicked()
		{
			Clicked?.Invoke();
		}

		protected virtual void OnPressed()
		{
			Pressed?.Invoke();
		}

		protected virtual void OnReleased()
		{
			Released?.Invoke();
		}

		protected virtual void OnToggled()
		{
			Toggled?.Invoke();
		}

		string _icon;

		/// <summary>
		/// Sets an icon for the button via a filepath.
		/// </summary>
		public string Icon
		{
			set
			{
				if ( _icon == value )
					return;

				_button.setIcon( value );
				_icon = value;
			}

			get => _icon;
		}

		/// <summary>
		/// Sets an icon for the button via a raw image.
		/// </summary>
		public void SetIcon( Pixmap pixmap )
		{
			_button.setIconFromPixmap( pixmap.ptr );
		}

		public Pixmap GetIcon()
		{
			var ptr = _button.getIconAsPixmap();
			return ptr.IsNull ? null : new Pixmap( ptr );
		}

		internal void InternalOnPressed() => OnPressed();
		internal void InternalOnReleased() => OnReleased();
		internal void InternalOnClicked() => OnClicked();
		internal void InternalOnToggled() => OnToggled();

		protected override void OnPaint()
		{
			var c = Tint.ToHsv();
			var bg = c;

			if ( Enabled )
			{
				if ( Paint.HasPressed )
				{
					bg = c with { Value = (c.Value + 0.1f) };
				}
				else if ( Paint.HasMouseOver )
				{
					bg = c with { Value = (c.Value + 0.2f) };
				}
			}
			else
			{
				bg = c = Theme.SurfaceLightBackground;
			}

			if ( !Enabled || ReadOnly )
			{
				c = c.WithSaturation( 0.1f ).WithAlpha( 0.5f );
				bg = c.WithAlpha( 0.2f );
			}

			if ( bg.Alpha > 0 )
			{
				float radius = 3;
				Paint.Antialiasing = true;

				Paint.ClearPen();
				Paint.SetBrush( bg with { Value = (bg.Value + 0.04f), Saturation = (c.Saturation * 0.8f) } );
				Paint.DrawRect( LocalRect, radius );

				Paint.SetBrushLinear( LocalRect.TopLeft, LocalRect.BottomRight, bg, bg with { Value = (bg.Value - 0.03f) } );
				Paint.DrawRect( LocalRect.Shrink( 1, 1, 1, 1 ), radius );
			}
			else
			{
				c = Color.White.WithAlpha( 0.5f );
			}

			Paint.SetDefaultFont();

			var textSize = Paint.MeasureText( Text );
			float iconSize = 16;
			float spacing = 4;

			float totalWidth = textSize.x;
			if ( !string.IsNullOrWhiteSpace( Icon ) )
			{
				totalWidth += iconSize + spacing;
			}

			float startX = LocalRect.Center.x - (totalWidth / 2);
			float centerY = LocalRect.Center.y;

			Paint.SetPen( c with { Value = 0.99f, Saturation = c.Saturation * 0.20f } );

			if ( !string.IsNullOrWhiteSpace( Icon ) )
			{
				var iconRect = new Rect( startX, centerY - iconSize / 2, iconSize, iconSize );
				Paint.DrawIcon( iconRect, Icon, iconSize );
				startX += iconSize + spacing;
			}

			var textRect = new Rect( startX, LocalRect.Top, textSize.x, LocalRect.Height );
			Paint.DrawText( textRect, Text, TextFlag.Center );
		}

		/// <summary>
		/// A visually distinct button.
		/// </summary>
		public class Primary : Button
		{
			public Primary( string title, Widget parent = null ) : this( title, null, parent )
			{

			}

			public Primary( string title, string icon, Widget parent = null ) : base( title, icon, parent )
			{
				Tint = Theme.Primary;
			}
		}

		public class Clear : Button
		{
			public Clear( string title, Widget parent = null ) : this( title, null, parent )
			{

			}

			public Clear( string title, string icon, Widget parent = null ) : base( title, icon, parent )
			{
				Tint = Color.Transparent;
			}
		}

		public class Danger : Button
		{
			public Danger( string title, Widget parent = null ) : this( title, null, parent )
			{

			}

			public Danger( string title, string icon, Widget parent = null ) : base( title, icon, parent )
			{
				Tint = Theme.Red;
			}
		}
	}
}
