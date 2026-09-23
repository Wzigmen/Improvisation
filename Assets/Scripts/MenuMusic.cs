using UnityEngine;

// Background music for the main menu. There is no audio file: a calm ~20 second loop (soft pad chords, a plucked
// bass, an arpeggio and a little pentatonic melody over Am - F - C - G) is synthesised in code once at start-up.
// It fades in while the menu is showing and fades out as soon as a game begins.
public class MenuMusic : MonoBehaviour
{
    const int SampleRate = 22050;
    const float Bpm = 96f;
    const int Bars = 8;
    const float TargetVolume = 0.3f;
    const float FadeSpeed = 0.6f;   // volume per second

    AudioSource source;

    // One chord per bar: bass note and the three chord tones (MIDI numbers).
    static readonly int[] BassNotes = { 45, 41, 48, 43, 45, 41, 48, 43 };
    static readonly int[][] Chords =
    {
        new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 55, 60, 64 }, new[] { 55, 59, 62 },
        new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 55, 60, 64 }, new[] { 55, 59, 62 },
    };

    // Melody per bar: { start beat, MIDI note, length in beats }.
    static readonly float[][][] Melody =
    {
        new[] { new[] { 0f, 76f, 1.5f }, new[] { 1.5f, 74f, 0.5f }, new[] { 2f, 72f, 1f }, new[] { 3f, 69f, 1f } },
        new[] { new[] { 0f, 72f, 2f }, new[] { 2f, 74f, 1f }, new[] { 3f, 72f, 1f } },
        new[] { new[] { 0f, 76f, 1f }, new[] { 1f, 79f, 1f }, new[] { 2f, 76f, 1.5f }, new[] { 3.5f, 74f, 0.5f } },
        new[] { new[] { 0f, 74f, 2f }, new[] { 2f, 72f, 1f }, new[] { 3f, 74f, 1f } },
        new[] { new[] { 0f, 69f, 1f }, new[] { 1f, 72f, 1f }, new[] { 2f, 76f, 2f } },
        new[] { new[] { 0f, 74f, 1.5f }, new[] { 1.5f, 72f, 0.5f }, new[] { 2f, 69f, 2f } },
        new[] { new[] { 0f, 72f, 1f }, new[] { 1f, 76f, 1f }, new[] { 2f, 79f, 1f }, new[] { 3f, 76f, 1f } },
        new[] { new[] { 0f, 74f, 1f }, new[] { 1f, 72f, 1f }, new[] { 2f, 69f, 1f }, new[] { 3f, 67f, 1f } },
    };

    void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.clip = BuildClip();
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.ignoreListenerPause = true;
    }

    void Update()
    {
        float target = NetworkGame.InGame ? 0f : TargetVolume;
        source.volume = Mathf.MoveTowards(source.volume, target, FadeSpeed * Time.unscaledDeltaTime);

        if (target > 0f && !source.isPlaying) source.Play();
        else if (target <= 0f && source.volume <= 0f && source.isPlaying) source.Pause();
    }

    // ---- synthesis ------------------------------------------------------------------------------------

    static AudioClip BuildClip()
    {
        float beat = 60f / Bpm;
        float bar = beat * 4f;
        int length = Mathf.RoundToInt(bar * Bars * SampleRate);
        var mix = new float[length];

        for (int b = 0; b < Bars; b++)
        {
            float t0 = b * bar;
            int[] chord = Chords[b];

            // pad: the whole chord, swelling slowly through the bar
            foreach (int note in chord)
                AddNote(mix, t0, bar * 1.15f, Hz(note), 0.05f, Timbre.Pad);

            // bass: root on beats 1 and 3, a short pluck in between
            AddNote(mix, t0, beat * 1.6f, Hz(BassNotes[b]), 0.22f, Timbre.Pluck);
            AddNote(mix, t0 + beat * 2f, beat * 1.6f, Hz(BassNotes[b]), 0.18f, Timbre.Pluck);
            AddNote(mix, t0 + beat * 3.5f, beat * 0.5f, Hz(BassNotes[b] + 7), 0.08f, Timbre.Pluck);

            // arpeggio: eighth notes up and down the chord, an octave higher
            int[] order = { 0, 1, 2, 1, 0, 1, 2, 1 };
            for (int i = 0; i < 8; i++)
                AddNote(mix, t0 + i * beat * 0.5f, beat * 1.2f, Hz(chord[order[i]] + 12), 0.07f, Timbre.Pluck);

            // melody
            foreach (var n in Melody[b])
                AddNote(mix, t0 + n[0] * beat, n[2] * beat + 0.6f, Hz(Mathf.RoundToInt(n[1])), 0.15f, Timbre.Bell);
        }

        // a soft echo, wrapped around the loop so it stays seamless
        int delay = Mathf.RoundToInt(beat * 0.75f * SampleRate);
        var output = new float[length];
        for (int i = 0; i < length; i++)
        {
            int j1 = (i - delay + length) % length;
            int j2 = (i - 2 * delay + 2 * length) % length;
            output[i] = mix[i] + 0.30f * mix[j1] + 0.14f * mix[j2];
        }

        float peak = 0.0001f;
        for (int i = 0; i < length; i++) peak = Mathf.Max(peak, Mathf.Abs(output[i]));
        float gain = 0.8f / peak;
        for (int i = 0; i < length; i++) output[i] *= gain;

        var clip = AudioClip.Create("MenuMusic", length, 1, SampleRate, false);
        clip.SetData(output, 0);
        return clip;
    }

    enum Timbre { Pad, Pluck, Bell }

    static float Hz(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

    // Renders one note into the loop buffer; anything that runs past the end wraps around to the start.
    static void AddNote(float[] buffer, float start, float duration, float frequency, float amplitude, Timbre timbre)
    {
        int first = Mathf.RoundToInt(start * SampleRate);
        int count = Mathf.RoundToInt(duration * SampleRate);
        float w = 2f * Mathf.PI * frequency / SampleRate;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float phase = w * i;
            float sample;
            float envelope;

            switch (timbre)
            {
                case Timbre.Pad:
                {
                    float attack = Mathf.Clamp01(t / 0.6f);
                    float release = Mathf.Clamp01((duration - t) / 0.8f);
                    envelope = attack * release;
                    sample = Mathf.Sin(phase) + 0.35f * Mathf.Sin(phase * 2f + 0.5f) + 0.15f * Mathf.Sin(phase * 3f);
                    break;
                }
                case Timbre.Pluck:
                {
                    envelope = Mathf.Min(t / 0.008f, 1f) * Mathf.Exp(-t * 4.5f);
                    sample = Mathf.Sin(phase) + 0.4f * Mathf.Sin(phase * 2f) + 0.15f * Mathf.Sin(phase * 3f);
                    break;
                }
                default:   // Bell: a soft music-box tone with a touch of vibrato
                {
                    float vibrato = 1f + 0.004f * Mathf.Sin(t * 5.5f * 2f * Mathf.PI);
                    envelope = Mathf.Min(t / 0.02f, 1f) * Mathf.Exp(-t * 1.6f) * Mathf.Clamp01((duration - t) / 0.15f);
                    sample = Mathf.Sin(phase * vibrato) + 0.25f * Mathf.Sin(phase * 2f) + 0.1f * Mathf.Sin(phase * 4f);
                    break;
                }
            }

            buffer[(first + i) % buffer.Length] += sample * envelope * amplitude;
        }
    }
}
