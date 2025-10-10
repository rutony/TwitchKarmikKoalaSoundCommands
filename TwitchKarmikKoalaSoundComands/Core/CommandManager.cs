using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class CommandManager {
    private Dictionary<string, SoundCommand> soundCommands;
    private Dictionary<string, int> commandUsage = new Dictionary<string, int>();
    private Dictionary<string, DateTime> lastCommandUsage = new Dictionary<string, DateTime>();
    private Dictionary<string, DateTime> lastRewardUsage = new Dictionary<string, DateTime>();
    private BotSettings settings;
    private string configFile = "config/sound_commands.txt";

    public int ChatEnabledCount { get; private set; }
    public int RewardEnabledCount { get; private set; }
    public int TotalUsage { get; private set; }

    public CommandManager(BotSettings settings) {
        this.settings = settings;
        soundCommands = new Dictionary<string, SoundCommand>();
        LoadSoundCommands();
        UpdateStatistics();
    }

    public void LoadSoundCommands() {
        if (!File.Exists(configFile)) {
            CreateDefaultConfig();
            return;
        }

        try {
            var lines = File.ReadAllLines(configFile);
            soundCommands.Clear();

            foreach (var line in lines) {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split('|');
                if (parts.Length >= 7) {
                    bool chatEnabled = bool.Parse(parts[0].Trim());
                    bool rewardEnabled = bool.Parse(parts[1].Trim());
                    string commandName = parts[2].Trim();
                    string rewardTitle = parts[3].Trim();
                    string soundFile = parts[4].Trim();
                    int cost = int.Parse(parts[5].Trim());
                    int cooldown = int.Parse(parts[6].Trim());

                    if (!commandName.StartsWith("!")) {
                        commandName = "!" + commandName;
                    }

                    var fullSoundPath = Path.Combine(settings.SoundsDirectory, soundFile);

                    soundCommands[commandName.ToLower()] = new SoundCommand(
                        fullSoundPath,
                        cost,
                        cooldown,
                        rewardTitle,
                        chatEnabled,
                        rewardEnabled
                    );
                } else {
                    WriteColor($"Пропущена строка с неправильным форматом: {line}\n", ConsoleColor.Yellow);
                }
            }
        } catch (Exception ex) {
            throw new Exception($"Ошибка загрузки конфигурации: {ex.Message}");
        }
    }

    public bool ProcessChatCommand(string command, string username) {
        if (!soundCommands.ContainsKey(command.ToLower()))
            return false;

        var soundCommand = soundCommands[command.ToLower()];

        if (!soundCommand.ChatEnabled)
            return false;

        string userCommandKey = $"{username}_{command.ToLower()}";

        if (lastCommandUsage.ContainsKey(userCommandKey)) {
            var lastUsage = lastCommandUsage[userCommandKey];
            var timeSinceLastUse = DateTime.Now - lastUsage;

            if (timeSinceLastUse.TotalSeconds < soundCommand.Cooldown) {
                return false;
            }
        }

        if (!commandUsage.ContainsKey(command.ToLower()))
            commandUsage[command.ToLower()] = 0;
        commandUsage[command.ToLower()]++;

        lastCommandUsage[userCommandKey] = DateTime.Now;
        UpdateStatistics();

        return true;
    }

    public bool ProcessRewardCommand(string command, string username) {
        if (!soundCommands.ContainsKey(command))
            return false;

        var soundCommand = soundCommands[command];

        if (!soundCommand.RewardEnabled)
            return false;

        string userRewardKey = $"{username}_{command}";

        if (lastRewardUsage.ContainsKey(userRewardKey)) {
            var lastUsage = lastRewardUsage[userRewardKey];
            var timeSinceLastUse = DateTime.Now - lastUsage;

            if (timeSinceLastUse.TotalSeconds < soundCommand.Cooldown) {
                return false;
            }
        }

        if (!commandUsage.ContainsKey(command))
            commandUsage[command] = 0;
        commandUsage[command]++;

        lastRewardUsage[userRewardKey] = DateTime.Now;
        UpdateStatistics();

        return true;
    }

    public void UpdateStatistics() {
        ChatEnabledCount = soundCommands.Count(c => c.Value.ChatEnabled);
        RewardEnabledCount = soundCommands.Count(c => c.Value.RewardEnabled);
        TotalUsage = commandUsage.Values.Sum();
    }

    public SoundCommand GetCommand(string command) {
        return soundCommands.ContainsKey(command.ToLower()) ? soundCommands[command.ToLower()] : null;
    }

    public Dictionary<string, SoundCommand> GetAllCommands() {
        return soundCommands;
    }

    public Dictionary<string, int> GetCommandUsage() {
        return commandUsage;
    }

    private void CreateDefaultConfig() {
        var defaultConfig = new[]
        {
            "# Формат: ChatEnabled|RewardEnabled|Команда|НазваниеНаграды|Файл|Стоимость|Cooldown",
            "true|true|!аттеншен|Аттеншен!|attention.mp3|500|180",
            "true|true|!ааа|А-а-а-а-а|aaaa.mp3|1500|180"
        };

        File.WriteAllLines(configFile, defaultConfig);
    }

    private void WriteColor(string text, ConsoleColor color) {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = originalColor;
    }
}