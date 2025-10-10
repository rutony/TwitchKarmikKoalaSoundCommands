using NAudio.Wave;
using NAudio.Wave.SampleProviders;

public class VolumeSampleProvider : ISampleProvider {
    private readonly ISampleProvider source;
    public float Volume { get; set; }

    public VolumeSampleProvider(ISampleProvider source) {
        this.source = source;
        Volume = 1.0f;
    }

    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count) {
        int samplesRead = source.Read(buffer, offset, count);
        for (int i = 0; i < samplesRead; i++) {
            buffer[offset + i] *= Volume;
        }
        return samplesRead;
    }
}