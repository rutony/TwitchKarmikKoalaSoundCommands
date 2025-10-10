using NAudio.Wave;
using System;
using System.Threading;

public class AudioPlayer {
    private readonly object soundLock = new object();
    private BotSettings settings;

    public AudioPlayer(BotSettings settings) {
        this.settings = settings;
    }

    public void PlaySound(string soundFile, string username, string command) {
        lock (soundLock) {
            try {
                using (var audioFile = new AudioFileReader(soundFile))
                using (var outputDevice = new WaveOutEvent()) {
                    if (settings.Volume != 100) {
                        var volumeProvider = new VolumeSampleProvider(audioFile.ToSampleProvider());
                        volumeProvider.Volume = settings.Volume / 100f;
                        outputDevice.Init(volumeProvider);
                    } else {
                        outputDevice.Init(audioFile);
                    }

                    outputDevice.Play();

                    while (outputDevice.PlaybackState == PlaybackState.Playing) {
                        Thread.Sleep(100);
                    }
                }
            } catch (Exception ex) {
                WriteColor($"Ошибка воспроизведения: {ex.Message}\n", ConsoleColor.Red);
            }
        }
    }

    private void WriteColor(string text, ConsoleColor color) {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = originalColor;
    }
}