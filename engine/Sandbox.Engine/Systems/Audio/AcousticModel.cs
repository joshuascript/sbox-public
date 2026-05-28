namespace Sandbox.Audio;

class AcousticModel : IDisposable
{
	/// <summary>
	/// Per-listener low-pass filter state. Mix thread only.
	/// </summary>
	LowPassProcessor _lowPass;

	public Transform Transform { get; private set; }

	internal AcousticModel() { }

	~AcousticModel() => Dispose();

	public void Dispose()
	{
		_lowPass = null;
		GC.SuppressFinalize( this );
	}

	// Main-thread-only smoothing state

	float? _occlusion;
	float _targetOcclusion = 1.0f;

	/// <summary>
	/// Time tracking for occlusion updates, managed by SoundOcclusionSystem.
	/// </summary>
	internal RealTimeUntil TimeUntilNextOcclusionCalc { get; set; } = 0;

	public void Update( Transform position )
	{
		Transform = position;

		// Smooth damp towards target occlusion (set by SoundOcclusionSystem).
		if ( Occlusion && !ListenLocal )
		{
			if ( _occlusion.HasValue )
				_occlusion = MathX.ExponentialDecay( _occlusion.Value, _targetOcclusion, 0.05f, RealTime.Delta );
			else
				Snap();
		}
		else
		{
			_targetOcclusion = 1.0f;
			Snap();
		}
	}

	/// <summary>Stop any lerping and jump straight to the target occlusion.</summary>
	public void Snap() => _occlusion = _targetOcclusion;

	/// <summary>Set the target occlusion value. Main thread only.</summary>
	internal void SetTargetOcclusion( float value ) => _targetOcclusion = value;

	// Snapshot

	/// <summary>
	/// Capture all fields the mix thread needs into an immutable struct.
	/// Called by the main thread in BuildVoiceState, before the snapshot is published.
	/// </summary>
	internal AcousticModelParams GetParams() => new()
	{
		Position = Transform.Position,
		Distance = Distance,
		Falloff = Falloff,
		Occlusion = _occlusion ?? 1.0f,
		DistanceAttenuation = DistanceAttenuation,
		OcclusionEnabled = Occlusion,
		Transmission = Transmission,
		AirAbsorption = AirAbsorption,
	};

	// Properties (main-thread-only)

	[ConVar] public static float snd_lowpass_power { get; set; } = 4;
	[ConVar] public static float snd_lowpass_trans { get; set; } = 0.40f;
	[ConVar] public static float snd_lowpass_dist { get; set; } = 0.85f;
	[ConVar] public static float snd_gain_trans { get; set; } = 0.8f;

	public bool ListenLocal { get; set; } = false;
	public bool AirAbsorption { get; set; } = true;
	public bool Transmission { get; set; } = true;
	public bool Occlusion { get; set; } = true;
	public bool DistanceAttenuation { get; set; } = true;
	public float OcclusionSize { get; set; } = 16.0f;

	public float Distance { get; set; } = 15_000f;
	public Curve Falloff { get; set; } = new Curve( new( 0, 1, 0, -1.8f ), new( 0.05f, 0.22f, 3.5f, -3.5f ), new( 0.2f, 0.04f, 0.16f, -0.16f ), new( 1, 0 ) );

	// Mix thread

	/// <summary>
	/// Apply distance attenuation, occlusion, and air-absorption low-pass to the input buffer.
	/// All source parameters come from <paramref name="p"/> (snapshotted at BuildVoiceState time).
	/// Only <see cref="_lowPass"/> is kept here as it holds per-frame IIR filter state that must persist across frames.
	/// </summary>
	public void Apply( in Listener listener, MultiChannelBuffer input, MultiChannelBuffer output,
		float occlusionMultiplier, float inputGain, in AcousticModelParams p )
	{
		Vector3 listenerPos = listener.MixTransform.Position;
		float distanceInUnits = p.Position.Distance( listenerPos );

		float curveVal = MathX.Clamp( distanceInUnits / p.Distance, 0f, 1f );
		float distanceAtten = p.Falloff.Evaluate( curveVal );

		// TODO: directivity
		float directivity = 1.0f;

		float occlusion = p.Occlusion + (1 - occlusionMultiplier).Clamp( 0, 1 );
		float transmission = 0;

		if ( !p.DistanceAttenuation ) distanceAtten = 1;
		if ( !p.OcclusionEnabled ) occlusion = 1;

		float lowPass = 0;

		if ( p.Transmission )
		{
			transmission = (1 - occlusion).Clamp( 0, 1 );
			lowPass += transmission * snd_lowpass_trans;
		}

		if ( p.AirAbsorption )
		{
			lowPass += curveVal.Remap( 0, 1, 0, 1 ) * snd_lowpass_dist;
		}

		lowPass = lowPass.Remap( 0, 1, -0.5f, 1f );

		if ( lowPass > 0 )
		{
			_lowPass ??= new LowPassProcessor();
			_lowPass.Cutoff = MathF.Pow( (1 - lowPass).Clamp( 0.005f, 1.0f ), snd_lowpass_power );
			_lowPass.SetListener( listener );
			_lowPass.ProcessInPlace( input );
		}

		// Reduce the volume of sounds coming through surfaces.
		transmission = snd_gain_trans;

		var gain = distanceAtten * directivity * (occlusion + transmission);

		output.CopyFromUpmix( input );
		output.Scale( gain.Clamp( 0, 1 ) * inputGain );
	}
}
