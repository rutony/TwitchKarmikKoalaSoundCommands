using System.Collections.Generic;
using System.IO;
using System.Linq;

public class FileManager {
    private string soundsDirectory;
    private List<string> missingFiles = new List<string>();

    public FileManager(string soundsDirectory) {
        this.soundsDirectory = soundsDirectory;
    }

    public List<string> CheckSoundFiles(Dictionary<string, SoundCommand> soundCommands) {
        missingFiles.Clear();
        foreach (var command in soundCommands.Values) {
            if (!File.Exists(command.SoundFile)) {
                missingFiles.Add(Path.GetFileName(command.SoundFile));
            }
        }
        return missingFiles;
    }

    public bool ValidateSoundFile(string soundFile) {
        return File.Exists(soundFile);
    }

    public List<string> GetMissingFiles() {
        return missingFiles;
    }
}