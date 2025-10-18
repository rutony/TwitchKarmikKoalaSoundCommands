using System;
using System.Collections.Generic;
using System.IO;

public class SettingsManager {
    private string settingsFile = "config/bot_settings.txt";
    public BotSettings Settings { get; private set; }

    public SettingsManager() {
        Settings = new BotSettings();
        LoadSettings();
    }

    public void LoadSettings() {
        if (!Directory.Exists("config")) {
            Directory.CreateDirectory("config");
        }

        if (!File.Exists(settingsFile)) {
            CreateDefaultSettings();
            throw new Exception($"Создан файл настроек {settingsFile}. Заполните его данными перед запуском.");
        }

        try {
            var lines = File.ReadAllLines(settingsFile);

            foreach (var line in lines) {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split('=');
                if (parts.Length == 2) {
                    var key = parts[0].Trim().ToLower();
                    var value = parts[1].Trim();

                    switch (key) {
                        case "bot_username":
                            Settings.BotUsername = value;
                            break;
                        case "oauth_token":
                            Settings.OAuthToken = value.StartsWith("oauth:") ? value : "oauth:" + value;
                            break;
                        case "client_id":
                            Settings.ClientId = value;
                            break;
                        case "channel_name":
                            Settings.ChannelName = value;
                            break;
                        case "sounds_directory":
                            Settings.SoundsDirectory = NormalizePath(value);
                            break;
                        case "cooldown_seconds":
                            if (int.TryParse(value, out int cooldown))
                                Settings.DefaultCooldown = cooldown;
                            break;
                        case "chat_enabled":
                            if (bool.TryParse(value, out bool chatEnabled))
                                Settings.ChatEnabled = chatEnabled;
                            break;
                        case "rewards_enabled":
                            if (bool.TryParse(value, out bool rewardsEnabled))
                                Settings.RewardsEnabled = rewardsEnabled;
                            break;
                        case "debug_mode":
                            if (bool.TryParse(value, out bool debugMode))
                                Settings.DebugMode = debugMode;
                            break;
                        case "volume":
                            if (int.TryParse(value, out int volume))
                                Settings.Volume = Math.Clamp(volume, 0, 100);
                            break;

                        case "enable_vip_reward":
                            if (bool.TryParse(value, out bool enableVipReward))
                                Settings.EnableVipReward = enableVipReward;
                            break;
                        case "vip_reward_cost":
                            if (int.TryParse(value, out int vipRewardCost))
                                Settings.VipRewardCost = vipRewardCost;
                            break;
                        case "vip_cooldown":
                            if (int.TryParse(value, out int vipCooldown))
                                Settings.VipCooldown = vipCooldown;
                            break;
                        case "vip_duration_days":
                            if (int.TryParse(value, out int vipDurationDays))
                                Settings.VipDurationDays = vipDurationDays;
                            break;
                        case "enable_vip_steal_reward":
                            if (bool.TryParse(value, out bool enableVipStealReward))
                                Settings.EnableVipStealReward = enableVipStealReward;
                            break;
                        case "vip_steal_cost":
                            if (int.TryParse(value, out int vipStealCost))
                                Settings.VipStealCost = vipStealCost;
                            break;
                        case "vip_steal_chance":
                            if (int.TryParse(value, out int vipStealChance))
                                Settings.VipStealChance = vipStealChance;
                            break;
                        case "vip_steal_ban_time":
                            if (int.TryParse(value, out int vipStealBanTime))
                                Settings.VipStealBanTime = vipStealBanTime;
                            break;

                        case "music_tracker_enabled":
                            if (bool.TryParse(value, out bool musicTrackerEnabled))
                                Settings.MusicTrackerEnabled = musicTrackerEnabled;
                            break;
                        case "music_tracker_port":
                            if (int.TryParse(value, out int musicTrackerPort))
                                Settings.MusicTrackerPort = musicTrackerPort;
                            break;
                        case "music_command_keywords":
                            Settings.MusicCommandKeywords = value;
                            break;
                        case "music_response_template":
                            Settings.MusicResponseTemplate = value;
                            break;
                        case "no_music_response_template":
                            Settings.NoMusicResponseTemplate = value;
                            break;
                    }
                }
            }

            if (string.IsNullOrEmpty(Settings.BotUsername) ||
                string.IsNullOrEmpty(Settings.OAuthToken) ||
                string.IsNullOrEmpty(Settings.ClientId) ||
                string.IsNullOrEmpty(Settings.ChannelName)) {
                throw new Exception("Не все обязательные поля заполнены в файле настроек!");
            }

            if (!Directory.Exists(Settings.SoundsDirectory)) {
                Directory.CreateDirectory(Settings.SoundsDirectory);
            }
        } catch (Exception ex) {
            throw new Exception($"Ошибка загрузки настроек: {ex.Message}");
        }
    }

    public void SaveSettings() {
        try {
            var lines = new List<string>
            {
                "# Настройки Twitch Sound Bot",
                "# Обязательные поля:",
                $"bot_username={Settings.BotUsername}",
                $"oauth_token={Settings.OAuthToken}",
                $"client_id={Settings.ClientId}",
                $"channel_name={Settings.ChannelName}",
                "",
                "# Дополнительные настройки:",
                $"sounds_directory={Settings.SoundsDirectory}",
                $"cooldown_seconds={Settings.DefaultCooldown}",
                $"chat_enabled={Settings.ChatEnabled}",
                $"rewards_enabled={Settings.RewardsEnabled}",
                $"debug_mode={Settings.DebugMode}",
                $"volume={Settings.Volume}",
                "",
                "# Настройки VIP:",
                $"enable_vip_reward={Settings.EnableVipReward}",
                $"vip_reward_cost={Settings.VipRewardCost}",
                $"vip_cooldown={Settings.VipCooldown}",
                $"vip_duration_days={Settings.VipDurationDays}",
                "",
                "# Настройки кражи VIP:",
                $"enable_vip_steal_reward={Settings.EnableVipStealReward}",
                $"vip_steal_cost={Settings.VipStealCost}",
                $"vip_steal_chance={Settings.VipStealChance}",
                $"vip_steal_ban_time={Settings.VipStealBanTime}",
                "",
                "# Настройки музыки:",
                $"music_tracker_enabled={Settings.MusicTrackerEnabled}",
                $"music_tracker_port={Settings.MusicTrackerPort}",
                $"music_command_keywords={Settings.MusicCommandKeywords}",
                $"music_response_template={Settings.MusicResponseTemplate}",
                $"no_music_response_template={Settings.NoMusicResponseTemplate}"
            };

            File.WriteAllLines(settingsFile, lines);
        } catch (Exception ex) {
            WriteColor($"Ошибка сохранения настроек: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    private void CreateDefaultSettings() {
        var defaultSettings = new[]
        {
            "# Настройки Twitch Sound Bot",
            "# Обязательные поля:",
            "bot_username=your_bot_username",
            "oauth_token=oauth:your_oauth_token_here",
            "client_id=your_client_id_here",
            "channel_name=your_channel_name",
            "",
            "# Дополнительные настройки:",
            "sounds_directory=sounds",
            "cooldown_seconds=30",
            "chat_enabled=true",
            "rewards_enabled=true",
            "debug_mode=false",
            "volume=50",
            "",
            "# Настройки Покупки VIP:",
            "enable_vip_reward=false",
            "vip_reward_cost=150000",
            "vip_cooldown=10", // в минутах
            "vip_duration_days=30",
            "",
            "# Настройки Кражи VIP:",
            "enable_vip_steal_reward=false",
            "vip_steal_cost=50000",
            "vip_steal_chance=5", // в процентах
            "vip_steal_ban_time=180", // в минутах
            "",
            "# Настройки музыки:",
            "music_tracker_enabled=true",
            "music_tracker_port=8080",
            "music_command_keywords=!music;!музыка;!song;!track;!трек",
            "music_response_template=$name, сейчас играет: $trackName $trackLink",
            "no_music_response_template=$name, сейчас ничего не играет"
        };

        File.WriteAllLines(settingsFile, defaultSettings);
    }

    private string NormalizePath(string path) {
        if (Path.IsPathRooted(path)) {
            return path;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), path);
    }

    private void WriteColor(string text, ConsoleColor color) {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = originalColor;
    }
}