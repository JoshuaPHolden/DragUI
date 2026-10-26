using System.Runtime.InteropServices;

namespace DragUI.Services;

// A soft 4-second chime, synthesised as a WAV and played through Windows (winmm PlaySound). The game
// plays its own audio through FMOD, so a Unity AudioSource may not be heard at all; this works whatever
// the game's audio setup, at Settings.AlarmVolume (not tied to the game's volume sliders).
internal static class AlarmSound
{
    const int SampleRate = 22050;
    const double Seconds = 4;

    const uint SND_ASYNC = 0x0001;
    const uint SND_NODEFAULT = 0x0002;
    const uint SND_MEMORY = 0x0004;

    [DllImport("winmm.dll", SetLastError = true)]
    static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

    // Async playback reads the WAV after PlaySound returns, so it lives in unmanaged memory (a managed
    // array could be moved by the GC mid-play) until the next play or Stop.
    static IntPtr _buffer;
    static float _bufferVolume = -1;

    public static void Play()
    {
        try
        {
            float volume = Settings.AlarmVolume;
            if (volume <= 0) return;
            if (_buffer == IntPtr.Zero || Math.Abs(volume - _bufferVolume) > 0.001f)
            {
                Stop();
                byte[] wav = BuildWav(volume);
                _buffer = Marshal.AllocHGlobal(wav.Length);
                Marshal.Copy(wav, 0, _buffer, wav.Length);
                _bufferVolume = volume;
            }
            PlaySound(_buffer, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"Couldn't play the boss alarm: {ex.Message}");
        }
    }

    /// <summary>Stop any playing alarm and free its buffer.</summary>
    public static void Stop()
    {
        try
        {
            PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
        }
        catch { }
        if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
        _buffer = IntPtr.Zero;
        _bufferVolume = -1;
    }

    // Four soft two-note chimes (E5 then A5), one per second, each with a quick fade-in and a gentle
    // exponential decay, plus a quiet octave for a bell-like tone.
    static byte[] BuildWav(float volume)
    {
        int samples = (int)(SampleRate * Seconds);
        var pcm = new short[samples];
        double amplitude = 0.5 * Math.Clamp(volume, 0f, 1f);

        for (int beat = 0; beat < 4; beat++)
        {
            AddNote(pcm, beat * 1.0, 659.25, amplitude);
            AddNote(pcm, beat * 1.0 + 0.22, 880.0, amplitude);
        }

        using var stream = new MemoryStream(44 + samples * 2);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + samples * 2);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);                 // fmt chunk size
        writer.Write((short)1);           // PCM
        writer.Write((short)1);           // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);     // byte rate
        writer.Write((short)2);           // block align
        writer.Write((short)16);          // bits per sample
        writer.Write("data"u8.ToArray());
        writer.Write(samples * 2);
        foreach (short s in pcm) writer.Write(s);
        writer.Flush();
        return stream.ToArray();
    }

    static void AddNote(short[] pcm, double start, double frequency, double amplitude)
    {
        const double Attack = 0.015, Length = 0.75, Decay = 5.0;
        int first = (int)(start * SampleRate);
        int count = Math.Min((int)(Length * SampleRate), pcm.Length - first);
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / SampleRate;
            double envelope = Math.Min(1.0, t / Attack) * Math.Exp(-Decay * t);
            double wave = Math.Sin(2 * Math.PI * frequency * t) + 0.25 * Math.Sin(4 * Math.PI * frequency * t);
            int value = pcm[first + i] + (int)(short.MaxValue * amplitude * 0.8 * envelope * wave);
            pcm[first + i] = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
        }
    }
}
