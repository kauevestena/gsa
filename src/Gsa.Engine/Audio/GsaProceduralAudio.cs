using Gsa.Core.Mobility;
using Godot;

namespace Gsa.Engine.Audio;

/// <summary>Offline procedural PCM synthesizer for SFX (shutter, clucking, horn, chimes) and 3 fictional radio stations.</summary>
public sealed class GsaProceduralAudio
{
    private const float SampleRate = 22050f;

    private sealed class SynthVoice
    {
        public float FreqStart;
        public float FreqEnd;
        public float Duration;
        public float Elapsed;
        public float Gain;
        public int Wave; // 0=sine, 1=square, 2=noise-burst, 3=saw
        public float Phase;
    }

    private readonly AudioStreamPlayer? _sfxPlayer;
    private readonly AudioStreamPlayer? _radioPlayer;
    private readonly AudioStreamGeneratorPlayback? _sfxPlayback;
    private readonly AudioStreamGeneratorPlayback? _radioPlayback;
    private readonly List<SynthVoice> _voices = [];
    private readonly Random _rng = new(90210);
    private float _radioPhaseA;
    private float _radioPhaseB;
    private float _radioBeatTime;
    private float _cluckTimer;
    private float _subtitleTimer;
    private float _enginePhaseA;
    private float _enginePhaseB;
    private float _screechPhase;
    private float _currentSpeedAbs;
    private LocomotionMode _currentMode = LocomotionMode.Handcart;
    private bool _currentBraking;

    public int RadioStation { get; private set; } // 0=Off, 1=Geodésia FM, 2=Rádio Multicaminho, 3=Onda Portadora
    public float SfxVolume { get; set; } = 0.7f;
    public float MusicVolume { get; set; } = 0.45f;
    public string ActiveRadioTitleKey => RadioStation switch
    {
        1 => "radio.station.geodesia",
        2 => "radio.station.multicaminho",
        3 => "radio.station.onda",
        _ => "radio.station.off",
    };
    public string CurrentRadioSubtitleKey { get; private set; } = "";

    private static readonly string[][] StationLines =
    [
        [],
        ["radio.geodesia.line1", "radio.geodesia.line2", "radio.geodesia.line3"],
        ["radio.multi.line1", "radio.multi.line2", "radio.multi.line3"],
        ["radio.onda.line1", "radio.onda.line2", "radio.onda.line3"],
    ];

    public GsaProceduralAudio(Node parent)
    {
        var headless = string.Equals(DisplayServer.GetName(), "headless", StringComparison.OrdinalIgnoreCase);
        if (headless)
            return;

        (_sfxPlayer, _sfxPlayback) = CreateGeneratorPlayer(parent, "SfxSynthPlayer");
        (_radioPlayer, _radioPlayback) = CreateGeneratorPlayer(parent, "RadioSynthPlayer");
    }

    private static (AudioStreamPlayer?, AudioStreamGeneratorPlayback?) CreateGeneratorPlayer(Node parent, string name)
    {
        try
        {
            var stream = new AudioStreamGenerator
            {
                MixRateMode = AudioStreamGenerator.AudioStreamGeneratorMixRate.Custom,
                MixRate = SampleRate,
                BufferLength = 0.15f,
            };
            var player = new AudioStreamPlayer { Name = name, Stream = stream };
            parent.AddChild(player);
            player.Play();
            return (player, player.GetStreamPlayback() as AudioStreamGeneratorPlayback);
        }
        catch
        {
            return (null, null);
        }
    }

    public int CycleRadioStation()
    {
        RadioStation = (RadioStation + 1) % 4;
        _radioBeatTime = 0f;
        _subtitleTimer = 0f;
        CurrentRadioSubtitleKey = RadioStation == 0
            ? ""
            : StationLines[RadioStation][0];
        return RadioStation;
    }

    public void PlayShutter()
    {
        _voices.Add(new SynthVoice { FreqStart = 1800f, FreqEnd = 420f, Duration = 0.045f, Gain = 0.35f, Wave = 2 });
        _voices.Add(new SynthVoice { FreqStart = 920f, FreqEnd = 1280f, Duration = 0.06f, Gain = 0.22f, Wave = 0 });
    }

    public void PlayHorn(LocomotionMode mode)
    {
        if (mode == LocomotionMode.Bicycle)
        {
            _voices.Add(new SynthVoice { FreqStart = 1560f, FreqEnd = 1520f, Duration = 0.22f, Gain = 0.30f, Wave = 0 });
            _voices.Add(new SynthVoice { FreqStart = 2340f, FreqEnd = 2310f, Duration = 0.18f, Gain = 0.16f, Wave = 0 });
        }
        else if (mode == LocomotionMode.Handcart)
        {
            // Gimbalina honk-cluck!
            PlayChickenCluckVoice(stressFraction: 0.7f);
        }
        else
        {
            // Classic GTA dual-tone car horn
            _voices.Add(new SynthVoice { FreqStart = 392f, FreqEnd = 390f, Duration = 0.28f, Gain = 0.26f, Wave = 3 });
            _voices.Add(new SynthVoice { FreqStart = 494f, FreqEnd = 492f, Duration = 0.28f, Gain = 0.24f, Wave = 3 });
        }
    }

    public void PlayOffloadChime()
    {
        _voices.Add(new SynthVoice { FreqStart = 587f, FreqEnd = 880f, Duration = 0.16f, Gain = 0.28f, Wave = 0 });
    }

    public void PlayPaymentSuccess()
    {
        _voices.Add(new SynthVoice { FreqStart = 523.25f, FreqEnd = 659.25f, Duration = 0.18f, Gain = 0.28f, Wave = 0 });
        _voices.Add(new SynthVoice { FreqStart = 783.99f, FreqEnd = 1046.5f, Duration = 0.28f, Gain = 0.26f, Wave = 0 });
    }

    public void PlayAlert(bool critical = false)
    {
        _voices.Add(new SynthVoice
        {
            FreqStart = critical ? 280f : 440f,
            FreqEnd = critical ? 190f : 360f,
            Duration = 0.20f,
            Gain = 0.28f,
            Wave = 1,
        });
    }

    public void PlayCollisionThud(double deltaV = 4.0)
    {
        var mag = Math.Clamp((float)(deltaV / 8.0), 0.35f, 1.0f);
        _voices.Add(new SynthVoice { FreqStart = 125f, FreqEnd = 38f, Duration = 0.22f, Gain = 0.45f * mag, Wave = 0 });
        _voices.Add(new SynthVoice { FreqStart = 640f, FreqEnd = 160f, Duration = 0.14f, Gain = 0.28f * mag, Wave = 2 });
    }

    public void PlayMissionPassedFanfare()
    {
        // Classic upbeat ascending brass-synth triad fanfare!
        _voices.Add(new SynthVoice { FreqStart = 392.0f, FreqEnd = 392.0f, Duration = 0.16f, Gain = 0.26f, Wave = 3 });
        _voices.Add(new SynthVoice { FreqStart = 523.25f, FreqEnd = 523.25f, Duration = 0.20f, Gain = 0.26f, Wave = 3 });
        _voices.Add(new SynthVoice { FreqStart = 659.25f, FreqEnd = 659.25f, Duration = 0.26f, Gain = 0.28f, Wave = 0 });
        _voices.Add(new SynthVoice { FreqStart = 783.99f, FreqEnd = 783.99f, Duration = 0.48f, Gain = 0.30f, Wave = 0 });
    }

    public void Step(
        double dt,
        double gimbalinaStress,
        bool hasGimbalina,
        bool isRaining,
        double speedMps = 0.0,
        LocomotionMode activeMode = LocomotionMode.Handcart,
        bool isBraking = false)
    {
        var fdt = (float)dt;
        _currentSpeedAbs = (float)Math.Abs(speedMps);
        _currentMode = activeMode;
        _currentBraking = isBraking;

        if (hasGimbalina && gimbalinaStress > 25.0)
        {
            _cluckTimer -= fdt;
            if (_cluckTimer <= 0f)
            {
                var frac = Math.Clamp((float)(gimbalinaStress / 100.0), 0f, 1f);
                PlayChickenCluckVoice(frac);
                _cluckTimer = Math.Max(0.45f, 3.2f - frac * 2.5f);
            }
        }

        if (RadioStation > 0)
        {
            _subtitleTimer += fdt;
            var lines = StationLines[RadioStation];
            var idx = ((int)(_subtitleTimer / 7.5f)) % lines.Length;
            CurrentRadioSubtitleKey = lines[idx];
        }
        else
        {
            CurrentRadioSubtitleKey = "";
        }

        PumpSfxBuffer();
        PumpRadioBuffer(isRaining);
    }

    private void PlayChickenCluckVoice(float stressFraction)
    {
        var basePitch = 520f + stressFraction * 340f;
        _voices.Add(new SynthVoice
        {
            FreqStart = basePitch,
            FreqEnd = basePitch * 1.45f,
            Duration = 0.085f,
            Gain = 0.20f + 0.14f * stressFraction,
            Wave = 3,
        });
    }

    private void PumpSfxBuffer()
    {
        if (_sfxPlayback is null)
        {
            _voices.Clear();
            return;
        }
        var frames = _sfxPlayback.GetFramesAvailable();
        if (frames <= 0)
            return;

        const float dt = 1f / SampleRate;
        for (var i = 0; i < frames; i++)
        {
            var mix = 0f;
            for (var v = _voices.Count - 1; v >= 0; v--)
            {
                var voice = _voices[v];
                var t = voice.Elapsed / voice.Duration;
                if (t >= 1f)
                {
                    _voices.RemoveAt(v);
                    continue;
                }
                var freq = Mathf.Lerp(voice.FreqStart, voice.FreqEnd, t);
                voice.Phase += freq * dt;
                if (voice.Phase >= 1f) voice.Phase -= 1f;

                var env = (1f - t) * MathF.Min(1f, t * 12f);
                var sample = voice.Wave switch
                {
                    1 => voice.Phase < 0.5f ? 0.8f : -0.8f,
                    2 => (float)(_rng.NextDouble() * 2.0 - 1.0),
                    3 => voice.Phase * 2f - 1f,
                    _ => MathF.Sin(voice.Phase * MathF.Tau),
                };
                mix += sample * env * voice.Gain * SfxVolume;
                voice.Elapsed += dt;
            }
                var clamped = Math.Clamp(mix, -0.95f, 0.95f);
                // Continuous vehicle engine purr/revving + brake screech
                if (_currentMode >= LocomotionMode.Scooter && SfxVolume > 0.01f)
                {
                    var maxSpd = Math.Max(8f, (float)PlatformProfile.ForMode(_currentMode).MaxSpeedMetresPerSecond);
                    var speedFrac = Math.Clamp(_currentSpeedAbs / maxSpd, 0f, 1.15f);
                    // Simulate 3-gear RPM sweep!
                    var gearPos = (speedFrac * 2.6f) % 1.0f;
                    var rpmNorm = _currentSpeedAbs < 0.4f ? 0.05f : Math.Clamp(0.28f + gearPos * 0.72f, 0.15f, 1.0f);
                    var baseHz = _currentMode == LocomotionMode.Scooter ? 58f : (_currentMode == LocomotionMode.Van ? 36f : 44f);
                    var engFreq = baseHz + rpmNorm * (_currentMode == LocomotionMode.Scooter ? 95f : 72f);

                    _enginePhaseA = (_enginePhaseA + engFreq * dt) % 1f;
                    _enginePhaseB = (_enginePhaseB + engFreq * 2.0f * dt) % 1f;
                    var rumble = MathF.Sin(_enginePhaseA * MathF.Tau) * 0.6f + (_enginePhaseB * 2f - 1f) * 0.4f;
                    clamped = Math.Clamp(clamped + rumble * (0.045f + 0.04f * rpmNorm) * SfxVolume, -0.95f, 0.95f);

                    if (_currentBraking && _currentSpeedAbs > 4.5f)
                    {
                        _screechPhase = (_screechPhase + 1140f * dt) % 1f;
                        var squeal = MathF.Sin(_screechPhase * MathF.Tau) * 0.075f * SfxVolume;
                        clamped = Math.Clamp(clamped + squeal, -0.95f, 0.95f);
                    }
                }
                _sfxPlayback.PushFrame(new Vector2(clamped, clamped));
            }
        }

        private void PumpRadioBuffer(bool isRaining)
        {
            if (_radioPlayback is null)
                return;
            var frames = _radioPlayback.GetFramesAvailable();
            if (frames <= 0)
                return;

            const float dt = 1f / SampleRate;
            float[] chordBass = RadioStation switch
            {
                1 => [130.81f, 146.83f, 164.81f, 196.00f], // Geodésia FM: upbeat golden-age retro pop-funk
                2 => [110.00f, 130.81f, 146.83f, 98.00f],  // Rádio Multicaminho: driving nightwave MSX synth
                3 => [146.83f, 164.81f, 196.00f, 174.61f], // Onda Portadora: late-night lounge Rhodes groove
                _ => [0f, 0f, 0f, 0f],
            };

            for (var i = 0; i < frames; i++)
            {
                var sample = 0f;
                if (RadioStation > 0 && MusicVolume > 0.01f)
                {
                    _radioBeatTime += dt;
                    var bpmSteps = RadioStation == 2 ? 3.2f : 2.2f;
                    var step = (int)(_radioBeatTime * bpmSteps) % 4;
                    var eighth = (int)(_radioBeatTime * bpmSteps * 2.0f) % 8;
                    var beatFrac = (_radioBeatTime * bpmSteps) % 1.0f;
                    var bassFreq = chordBass[step];
                    var melMult = (eighth % 3 == 0) ? 3.0f : (eighth % 2 == 0 ? 2.5f : 2.0f);
                    var melFreq = bassFreq * melMult;

                    _radioPhaseA = (_radioPhaseA + bassFreq * dt) % 1f;
                    _radioPhaseB = (_radioPhaseB + melFreq * dt) % 1f;

                    var kick = (beatFrac < 0.09f) ? MathF.Sin(beatFrac * 90f) * (1f - beatFrac / 0.09f) * 0.35f : 0f;
                    var hat = (((_radioBeatTime * bpmSteps * 2f) % 1f) < 0.03f) ? (float)(_rng.NextDouble() * 2.0 - 1.0) * 0.14f : 0f;
                    var env = MathF.Exp(-beatFrac * 3.0f);
                    var bass = MathF.Sin(_radioPhaseA * MathF.Tau) * 0.42f;
                    var lead = (RadioStation == 2 ? (_radioPhaseB * 2f - 1f) : MathF.Sin(_radioPhaseB * MathF.Tau)) * 0.24f * env;
                    sample += (bass + lead + kick + hat) * 0.34f * MusicVolume;
                }
            if (isRaining && SfxVolume > 0.01f)
            {
                sample += (float)(_rng.NextDouble() * 2.0 - 1.0) * 0.035f * SfxVolume;
            }
            sample = Math.Clamp(sample, -0.95f, 0.95f);
            _radioPlayback.PushFrame(new Vector2(sample, sample));
        }
    }
}

