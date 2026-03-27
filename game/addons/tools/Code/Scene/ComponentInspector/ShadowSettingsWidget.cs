namespace Editor;

using Editor.SoundEditor;
using Sandbox.Engine;
using Sandbox.Rendering;
using static Sandbox.DirectionalLight;

[CustomEditor( typeof( DirectionalLight.CascadeVisualizer ) )]
public class ShadowCascadVisualizerControlWidget : ControlWidget
{
	public override bool IncludeLabel => false;

	private static readonly Color[] CascadeColors =
	{
		new Color(0.6f, 0.5f, 0.5f, 1.0f),
		new Color(0.5f, 0.6f, 0.5f, 1.0f),
		new Color(0.5f, 0.5f, 0.6f, 1.0f),
		new Color(0.6f, 0.6f, 0.5f, 1.0f),
	};

	public ShadowCascadVisualizerControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Column();

		Rebuild();

		var a = property.GetValue<DirectionalLight.CascadeVisualizer>();
		a.Update += Update;
	}

	protected override void OnPaint()
	{
		var cascadeCount = SerializedProperty.Parent.GetProperty( "ShadowCascadeCount" ).GetValue<int>();
		var splitRatio = SerializedProperty.Parent.GetProperty( "ShadowCascadeSplitRatio" ).GetValue<float>();
		var firstCascadeSize = SerializedProperty.Parent.GetProperty( "ShadowFirstCascadeSize" ).GetValue<float>();
		var far = 15000.0f;

		var splits = new float[4] { 0.1f, 0.2f, 0.3f, 0.4f }; // ShadowMapper.CalculateSplitDistances( cascadeCount, 1.0f, far, firstCascadeSize, splitRatio );

		var width = LocalRect.Width / cascadeCount;
		var hieght = LocalRect.Height;

		float x = 0;
		float prevSplit = 0;
		for ( int i = 0; i < splits.Length; i++ )
		{
			var split = splits[i];

			var w = (split - prevSplit) * LocalRect.Width;
			var rect = new Rect( x, 0, w, hieght );

			Paint.SetPen( Theme.Border );
			Paint.SetBrush( CascadeColors[i] );
			Paint.DrawRect( rect );

			Paint.ClearBrush();
			Paint.SetPen( Theme.Text );
			var cascadeNear = prevSplit * far;
			var cascadeFar = split * far;
			Paint.DrawText( rect, $"{i}\n{cascadeNear:F0}-{cascadeFar:F0}" );

			x += w;
			prevSplit = split;
		}
	}

	public void Rebuild()
	{
		Layout.Clear( true );
		Layout.Spacing = 2;

		Layout.Margin = 8;
		FixedHeight = 48;
	}

	protected override void OnValueChanged()
	{
		Rebuild();
	}
}
