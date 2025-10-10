using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Api.Core;
using TwitchLib.Api.Helix.Models.ChannelPoints;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomReward;
using TwitchLib.Client;
using TwitchLib.Client.Enums;
using TwitchLib.Client.Events;
using TwitchLib.Client.Extensions;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using TwitchLib.PubSub;
using TwitchLib.PubSub.Events;
using TwitchLib.PubSub.Models.Responses.Messages.Redemption;

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

public class TwitchSoundBot {
    private TwitchClient client;
    private TwitchAPI api;
    private Dictionary<string, SoundCommand> soundCommands;
    private TwitchPubSub pubSub;
    private Dictionary<string, string> rewardIdToCommandMap;
    private HashSet<string> activeUsers;
    private readonly object soundLock = new object();
    private string configFile = "config/sound_commands.txt";
    private string settingsFile = "config/bot_settings.txt";
    private string channelId;
    private List<string> createdRewardIds = new List<string>();
    private BotSettings settings;
    private Dictionary<string, int> commandUsage = new Dictionary<string, int>();
    private bool displayLogs = false;
    private List<string> missingFiles = new List<string>();
    private string lastAuthError = "";
    private string lastRewardsError = "";
    private string lastChatError = "";

    // Статистика для отображения
    private int chatEnabledCount = 0;
    private int rewardEnabledCount = 0;
    private int totalUsage = 0;

    public TwitchSoundBot() {
        soundCommands = new Dictionary<string, SoundCommand>();
        activeUsers = new HashSet<string>();
        LoadSettings();
        LoadSoundCommands();
        CheckSoundFiles();
        UpdateStatistics();
    }

    public string GetChannelName() => settings.ChannelName;
    public int GetTotalCommands() => soundCommands.Count;
    public int GetTotalUsage() => totalUsage;
    public Dictionary<string, int> GetCommandUsage() => commandUsage;
    public List<string> GetMissingFiles() => missingFiles;
    public string GetLastAuthError() => lastAuthError;
    public string GetLastRewardsError() => lastRewardsError;
    public string GetLastChatError() => lastChatError;
    public bool ChatEnabled => settings.ChatEnabled;
    public bool RewardsEnabled => settings.RewardsEnabled;
    public int GetChatEnabledCount() => chatEnabledCount;
    public int GetRewardEnabledCount() => rewardEnabledCount;

    public void UpdateStatistics() {
        chatEnabledCount = soundCommands.Count(c => c.Value.ChatEnabled);
        rewardEnabledCount = soundCommands.Count(c => c.Value.RewardEnabled);
        totalUsage = commandUsage.Values.Sum();
    }

    public void ToggleChat() {
        settings.ChatEnabled = !settings.ChatEnabled;
        SaveSettings();
    }

    public void ToggleRewards() {
        settings.RewardsEnabled = !settings.RewardsEnabled;
        SaveSettings();
    }

    public void ToggleDebugMode() {
        settings.DebugMode = !settings.DebugMode;
        SaveSettings();
        WriteColor($"Режим отладки: {(settings.DebugMode ? "ВКЛ" : "ВЫКЛ")}\n",
                   settings.DebugMode ? ConsoleColor.Green : ConsoleColor.Red);
    }

    public void ChangeVolume() {
        settings.Volume += 10;
        if (settings.Volume > 100) {
            settings.Volume = 0;
        }
        SaveSettings();
        WriteColor($"Громкость установлена: {settings.Volume}%\n", ConsoleColor.Cyan);
    }

    public bool IsDebugMode => settings.DebugMode;
    public int GetVolume => settings.Volume;

    private void LoadSettings() {
        if (!Directory.Exists("config")) {
            Directory.CreateDirectory("config");
        }

        if (!File.Exists(settingsFile)) {
            CreateDefaultSettings();
            throw new Exception($"Создан файл настроек {settingsFile}. Заполните его данными перед запуском.");
        }

        try {
            var lines = File.ReadAllLines(settingsFile);
            settings = new BotSettings();

            foreach (var line in lines) {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split('=');
                if (parts.Length == 2) {
                    var key = parts[0].Trim().ToLower();
                    var value = parts[1].Trim();

                    switch (key) {
                        case "bot_username":
                            settings.BotUsername = value;
                            break;
                        case "oauth_token":
                            settings.OAuthToken = value.StartsWith("oauth:") ? value : "oauth:" + value;
                            break;
                        case "client_id":
                            settings.ClientId = value;
                            break;
                        case "channel_name":
                            settings.ChannelName = value;
                            break;
                        case "sounds_directory":
                            settings.SoundsDirectory = NormalizePath(value);
                            break;
                        case "cooldown_seconds":
                            if (int.TryParse(value, out int cooldown))
                                settings.DefaultCooldown = cooldown;
                            break;
                        case "chat_enabled":
                            if (bool.TryParse(value, out bool chatEnabled))
                                settings.ChatEnabled = chatEnabled;
                            break;
                        case "rewards_enabled":
                            if (bool.TryParse(value, out bool rewardsEnabled))
                                settings.RewardsEnabled = rewardsEnabled;
                            break;
                        case "debug_mode":
                            if (bool.TryParse(value, out bool debugMode))
                                settings.DebugMode = debugMode;
                            break;
                        case "volume":
                            if (int.TryParse(value, out int volume))
                                settings.Volume = Math.Clamp(volume, 0, 100);
                            break;
                    }
                }
            }

            // Проверяем обязательные поля
            if (string.IsNullOrEmpty(settings.BotUsername) ||
                string.IsNullOrEmpty(settings.OAuthToken) ||
                string.IsNullOrEmpty(settings.ClientId) ||
                string.IsNullOrEmpty(settings.ChannelName)) {
                throw new Exception("Не все обязательные поля заполнены в файле настроек!");
            }

            // Создаем папку для звуков если её нет
            if (!Directory.Exists(settings.SoundsDirectory)) {
                Directory.CreateDirectory(settings.SoundsDirectory);
            }
        } catch (Exception ex) {
            throw new Exception($"Ошибка загрузки настроек: {ex.Message}");
        }
    }

    private string NormalizePath(string path) {
        if (Path.IsPathRooted(path)) {
            return path;
        }

        // Относительный путь от директории программы
        return Path.Combine(Directory.GetCurrentDirectory(), path);
    }

    private void SaveSettings() {
        try {
            var lines = new List<string>
            {
                "# Настройки Twitch Sound Bot",
                "# Обязательные поля:",
                $"bot_username={settings.BotUsername}",
                $"oauth_token={settings.OAuthToken}",
                $"client_id={settings.ClientId}",
                $"channel_name={settings.ChannelName}",
                "",
                "# Дополнительные настройки:",
                $"sounds_directory={settings.SoundsDirectory}",
                $"cooldown_seconds={settings.DefaultCooldown}",
                $"chat_enabled={settings.ChatEnabled}",
                $"rewards_enabled={settings.RewardsEnabled}",
                $"debug_mode={settings.DebugMode}",
                $"volume={settings.Volume}"
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
            "volume=50"
        };

        File.WriteAllLines(settingsFile, defaultSettings);
    }

    private void CheckSoundFiles() {
        missingFiles.Clear();
        foreach (var command in soundCommands.Values) {
            if (!File.Exists(command.SoundFile)) {
                missingFiles.Add(Path.GetFileName(command.SoundFile));
            }
        }
    }

    public async Task CheckTokenScopes() {
        try {
            var cleanToken = settings.OAuthToken.Replace("oauth:", "");
            var validated = await api.Auth.ValidateAccessTokenAsync(cleanToken);

            if (validated != null) {
                WriteDebug($"Токен действителен для пользователя: {validated.UserId}\n", ConsoleColor.Green);
                WriteDebug($"Scopes токена: {string.Join(", ", validated.Scopes)}\n", ConsoleColor.Yellow);

                var requiredScopes = new[] { "channel:manage:redemptions" };
                var missingScopes = requiredScopes.Where(scope => !validated.Scopes.Contains(scope)).ToList();

                if (missingScopes.Any()) {
                    WriteDebug($"Не хватает scopes для наград: {string.Join(", ", missingScopes)}\n", ConsoleColor.Red);
                } else {
                    WriteDebug("Все необходимые scopes присутствуют\n", ConsoleColor.Green);
                }
            }
        } catch (Exception ex) {
            WriteDebug($"Ошибка проверки токена: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    private void InitializePubSub() {
        pubSub = new TwitchPubSub();

        // Создаем mapping между ID наград и командами
        rewardIdToCommandMap = new Dictionary<string, string>();

        // Подписываемся на события наград
        pubSub.OnRewardRedeemed += OnRewardRedeemed;
        pubSub.OnPubSubServiceConnected += OnPubSubServiceConnected;
        pubSub.OnListenResponse += OnListenResponse;

        // Подключаемся к PubSub
        pubSub.Connect();

        // Ждем подключения и подписываемся на события наград
        Task.Run(async () => {
            await Task.Delay(2000);
            pubSub.ListenToRewards(channelId);
            pubSub.SendTopics(settings.OAuthToken.Replace("oauth:", ""));
        });
    }

    private void OnPubSubServiceConnected(object sender, EventArgs e) {
        WriteDebug("✅ PubSub подключен\n", ConsoleColor.Green);

        // Обновляем mapping наград
        UpdateRewardMappings();
    }

    private async void UpdateRewardMappings() {
        try {
            var rewards = await api.Helix.ChannelPoints.GetCustomRewardAsync(channelId, createdRewardIds);
            rewardIdToCommandMap.Clear();

            foreach (var reward in rewards.Data) {
                // Находим команду по названию награды
                var command = soundCommands.Values.FirstOrDefault(c => c.RewardTitle == reward.Title && c.RewardEnabled);
                if (command != null) {
                    var commandKey = soundCommands.FirstOrDefault(x => x.Value.RewardTitle == reward.Title).Key;
                    rewardIdToCommandMap[reward.Id] = commandKey;
                    WriteDebug($"✅ Сопоставлена награда '{reward.Title}' -> команда '{commandKey}'\n", ConsoleColor.Green);
                }
            }
        } catch (Exception ex) {
            WriteDebug($"❌ Ошибка обновления mapping наград: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    private Dictionary<string, DateTime> lastCommandUsage = new Dictionary<string, DateTime>();
    private Dictionary<string, DateTime> lastRewardUsage = new Dictionary<string, DateTime>();

    private void OnRewardRedeemed(object sender, OnRewardRedeemedArgs e) {
        if (!settings.RewardsEnabled)
            return;

        if (settings.DebugMode) {
            WriteDebug($"🎁 Активирована награда: {e.RewardTitle} пользователем {e.DisplayName}\n", ConsoleColor.Magenta);
        }

        if (rewardIdToCommandMap.TryGetValue(e.RewardId.ToString(), out string command)) {
            var soundCommand = soundCommands[command];

            // Проверяем разрешена ли команда для наград
            if (!soundCommand.RewardEnabled) {
                WriteDebug($"❌ Команда {command} отключена для наград\n", ConsoleColor.Yellow);
                return;
            }

            // ПРОВЕРКА COOLDOWN ДЛЯ НАГРАД
            string userRewardKey = $"{e.DisplayName}_{command}";

            if (lastRewardUsage.ContainsKey(userRewardKey)) {
                var lastUsage = lastRewardUsage[userRewardKey];
                var timeSinceLastUse = DateTime.Now - lastUsage;

                if (timeSinceLastUse.TotalSeconds < soundCommand.Cooldown) {
                    if (settings.DebugMode) {
                        WriteDebug($"⏳ Cooldown для {e.DisplayName}: {soundCommand.Cooldown - timeSinceLastUse.TotalSeconds:F0}с осталось\n", ConsoleColor.Yellow);
                    }
                    return; // Пропускаем из-за cooldown
                }
            }

            if (settings.DebugMode) {
                WriteDebug($"🔊 Обрабатываем команду: {command} для пользователя {e.DisplayName}\n", ConsoleColor.Cyan);
            }

            if (!commandUsage.ContainsKey(command))
                commandUsage[command] = 0;
            commandUsage[command]++;
            UpdateStatistics();

            // Обновляем время использования
            lastRewardUsage[userRewardKey] = DateTime.Now;

            ProcessSoundCommand(command, e.DisplayName, null, true);
        } else {
            WriteDebug($"❌ Не найдено сопоставление для награды ID: {e.RewardId}\n", ConsoleColor.Red);

            // Попробуем найти команду по названию награды
            var matchingCommand = soundCommands.Values.FirstOrDefault(c => c.RewardTitle == e.RewardTitle && c.RewardEnabled);
            if (matchingCommand != null) {
                var commandKey = soundCommands.FirstOrDefault(x => x.Value.RewardTitle == e.RewardTitle).Key;
                WriteDebug($"🔍 Найдено совпадение по названию: {commandKey}\n", ConsoleColor.Yellow);
                rewardIdToCommandMap[e.RewardId.ToString()] = commandKey;

                // ПРОВЕРКА COOLDOWN ДЛЯ НАГРАД (для этого случая тоже)
                string userRewardKey = $"{e.DisplayName}_{commandKey}";
                var soundCommand = matchingCommand;

                if (lastRewardUsage.ContainsKey(userRewardKey)) {
                    var lastUsage = lastRewardUsage[userRewardKey];
                    var timeSinceLastUse = DateTime.Now - lastUsage;

                    if (timeSinceLastUse.TotalSeconds < soundCommand.Cooldown) {
                        if (settings.DebugMode) {
                            WriteDebug($"⏳ Cooldown для {e.DisplayName}: {soundCommand.Cooldown - timeSinceLastUse.TotalSeconds:F0}с осталось\n", ConsoleColor.Yellow);
                        }
                        return;
                    }
                }

                lastRewardUsage[userRewardKey] = DateTime.Now;

                if (!commandUsage.ContainsKey(commandKey))
                    commandUsage[commandKey] = 0;
                commandUsage[commandKey]++;
                UpdateStatistics();

                ProcessSoundCommand(commandKey, e.DisplayName, null, true);
            }
        }
    }

    private void OnListenResponse(object sender, OnListenResponseArgs e) {
        if (!e.Successful) {
            WriteDebug($"❌ Ошибка подписки на тему: {e.Topic}\n", ConsoleColor.Red);
        } else {
            WriteDebug($"✅ Успешная подписка на тему: {e.Topic}\n", ConsoleColor.Green);
        }
    }

    public async Task<(bool authOk, string authError, bool rewardsOk, string rewardsError, bool chatOk, string chatError)> Connect() {
        api = new TwitchAPI();
        api.Settings.ClientId = settings.ClientId;

        // УБИРАЕМ "oauth:" префикс для Helix API
        string apiToken = settings.OAuthToken;
        if (apiToken.StartsWith("oauth:")) {
            apiToken = apiToken.Substring(6); // Убираем "oauth:"
        }
        api.Settings.AccessToken = apiToken;

        // Проверка авторизации
        lastAuthError = "";
        lastRewardsError = "";
        lastChatError = "";

        // Тестируем Helix API с новым форматом токена
        if (settings.DebugMode) {
            WriteDebug("Тестируем Helix API с очищенным токеном...\n", ConsoleColor.Yellow);
        }
        try {
            var users = await api.Helix.Users.GetUsersAsync(logins: new List<string> { settings.ChannelName });
            if (users.Users.Length > 0) {
                channelId = users.Users[0].Id;
                if (settings.DebugMode) {
                    WriteDebug($"✅ Успешно! Получен channelId: {channelId} для канала {settings.ChannelName}\n", ConsoleColor.Green);
                }
            } else {
                lastAuthError = $"Канал {settings.ChannelName} не найден";
                return (false, lastAuthError, false, "", false, "");
            }
        } catch (Exception ex) {
            lastAuthError = $"Ошибка получения channelId: {ex.Message}";
            WriteDebug($"❌ Ошибка: {lastAuthError}\n", ConsoleColor.Red);
            return (false, lastAuthError, false, "", false, "");
        }

        bool chatOk = false;
        bool rewardsOk = false;
        string chatError = "";
        string rewardsError = "";

        // Подключение к чату (ЗДЕСЬ ОСТАВЛЯЕМ токен с "oauth:")
        if (settings.ChatEnabled) {
            try {
                var credentials = new ConnectionCredentials(settings.BotUsername, settings.OAuthToken); // Здесь оставляем с "oauth:"
                var clientOptions = new ClientOptions {
                    MessagesAllowedInPeriod = 750,
                    ThrottlingPeriod = TimeSpan.FromSeconds(30)
                };

                var customClient = new WebSocketClient(clientOptions);
                client = new TwitchClient(customClient);

                client.Initialize(credentials, settings.ChannelName);

                if (!displayLogs) {
                    client.OnLog += (s, e) => { };
                }

                client.OnJoinedChannel += OnJoinedChannel;
                client.OnMessageReceived += OnMessageReceived;

                client.Connect();

                chatOk = await WaitForChatConnection();
                if (!chatOk) {
                    chatError = "Таймаут подключения к чату";
                    lastChatError = chatError;
                } else {
                    WriteDebug("✅ Чат подключен успешно\n", ConsoleColor.Green);
                }
            } catch (Exception ex) {
                chatError = $"Ошибка чата: {ex.Message}";
                lastChatError = chatError;
            }
        }

        // Создание наград
        if (settings.RewardsEnabled) {
            try {
                rewardsOk = await CreateCustomRewards();
                if (!rewardsOk) {
                    rewardsError = lastRewardsError ?? "Не удалось создать награды";
                }
            } catch (Exception ex) {
                rewardsError = $"Ошибка наград: {ex.Message}";
                lastRewardsError = rewardsError;
            }
        }

        if (settings.RewardsEnabled && rewardsOk) {
            try {
                InitializePubSub();
                WriteDebug("✅ PubSub для наград инициализирован\n", ConsoleColor.Green);
            } catch (Exception ex) {
                WriteDebug($"❌ Ошибка инициализации PubSub: {ex.Message}\n", ConsoleColor.Red);
            }
        }

        return (true, "", rewardsOk, rewardsError, chatOk, chatError);
    }

    private async Task<bool> WaitForChatConnection() {
        for (int i = 0; i < 10; i++) {
            if (client?.IsConnected == true)
                return true;
            await Task.Delay(1000);
        }
        return client?.IsConnected == true;
    }

    private void OnJoinedChannel(object sender, OnJoinedChannelArgs e) {
        //client.SendMessage(e.Channel, "Бот звуков подключен! Используйте !звуки для списка команд.");
    }

    public string GetAuthUrl() {
        var scopes = "channel:manage:redemptions chat:edit chat:read";
        var encodedScopes = Uri.EscapeDataString(scopes);
        return $"https://id.twitch.tv/oauth2/authorize?client_id={settings.ClientId}&redirect_uri=http://localhost&response_type=token&scope={encodedScopes}";
    }

    public void DebugAuthInfo() {
        Console.WriteLine($"BotUsername: {settings.BotUsername}");
        Console.WriteLine($"ClientId: {settings.ClientId}");
        Console.WriteLine($"ChannelName: {settings.ChannelName}");
        Console.WriteLine($"OAuthToken length: {settings.OAuthToken?.Length}");
        Console.WriteLine($"Token starts with: {settings.OAuthToken?.Substring(0, Math.Min(10, settings.OAuthToken.Length))}...");
    }

    private void OnMessageReceived(object sender, OnMessageReceivedArgs e) {
        if (!settings.ChatEnabled)
            return;

        var message = e.ChatMessage.Message.ToLower();
        var username = e.ChatMessage.Username;

        if (message == "!звуки" || message == "!sounds") {
            ShowSoundList(e.ChatMessage.Channel);
            return;
        }

        if (message == "!reload" && e.ChatMessage.IsBroadcaster) {
            LoadSoundCommands();
            CheckSoundFiles();
            UpdateStatistics();
            if (settings.RewardsEnabled)
                _ = CreateCustomRewards();
            return;
        }

        if (message == "!status" && e.ChatMessage.IsBroadcaster) {
            return;
        }

        if (soundCommands.ContainsKey(message.ToLower())) {
            var soundCommand = soundCommands[message.ToLower()];

            // Проверяем разрешена ли команда для чата
            if (!soundCommand.ChatEnabled) {
                return;
            }

            // ПРОВЕРКА COOLDOWN ДЛЯ ЧАТ-КОМАНД
            string userCommandKey = $"{username}_{message.ToLower()}";

            if (lastCommandUsage.ContainsKey(userCommandKey)) {
                var lastUsage = lastCommandUsage[userCommandKey];
                var timeSinceLastUse = DateTime.Now - lastUsage;

                if (timeSinceLastUse.TotalSeconds < soundCommand.Cooldown) {
                    int remainingSeconds = (int)(soundCommand.Cooldown - timeSinceLastUse.TotalSeconds);
                    //client.SendMessage(e.ChatMessage.Channel, $"{username}, подождите {remainingSeconds}с!");
                    return;
                }
            }

            if (!commandUsage.ContainsKey(message.ToLower()))
                commandUsage[message.ToLower()] = 0;
            commandUsage[message.ToLower()]++;
            UpdateStatistics();

            // Обновляем время использования
            lastCommandUsage[userCommandKey] = DateTime.Now;

            ProcessSoundCommand(message.ToLower(), username, e.ChatMessage.Channel, false);
        }
    }

    private void LoadSoundCommands() {
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

                    // Убеждаемся, что команда начинается с !
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

    private void CreateDefaultConfig() {
        var defaultConfig = new[]
        {
            "# Формат: ChatEnabled|RewardEnabled|Команда|НазваниеНаграды|Файл|Стоимость|Cooldown",
            "true|true|!аттеншен|Аттеншен!|attention.mp3|500|180",
            "true|true|!ааа|А-а-а-а-а|aaaa.mp3|1500|180"
        };

        File.WriteAllLines(configFile, defaultConfig);
    }

    private async Task<bool> CreateCustomRewards() {
        try {
            WriteDebug($"=== ДИАГНОСТИКА СОЗДАНИЯ НАГРАД ===\n", ConsoleColor.Cyan);
            WriteDebug($"ChannelId: {channelId}\n", ConsoleColor.Yellow);

            // Получаем ВСЕ существующие награды канала
            WriteDebug($"\nПолучаем существующие награды...\n", ConsoleColor.White);
            var existingRewardsResponse = await api.Helix.ChannelPoints.GetCustomRewardAsync(channelId, new List<string>(), true);

            if (existingRewardsResponse == null || existingRewardsResponse.Data == null) {
                lastRewardsError = "Не удалось получить список существующих наград";
                return false;
            }

            var existingRewards = existingRewardsResponse.Data;
            var existingTitles = existingRewards.Select(r => r.Title.ToLower()).ToHashSet();

            WriteDebug($"✅ Найдено существующих наград: {existingRewards.Length}\n", ConsoleColor.Green);

            // Выводим список существующих наград для отладки
            if (existingRewards.Length > 0) {
                WriteDebug($"Существующие награды:\n", ConsoleColor.Yellow);
                foreach (var reward in existingRewards) {
                    WriteDebug($"  - '{reward.Title}' (ID: {reward.Id}, Вкл: {reward.IsEnabled})\n", ConsoleColor.Gray);
                }
            }

            int createdCount = 0;
            int updatedCount = 0;
            int enabledCount = 0;

            // Обрабатываем команды с включенными наградами
            var rewardCommands = soundCommands.Values.Where(c => c.RewardEnabled).ToList();
            WriteColor($"\nОбрабатываем {rewardCommands.Count} команд с наградами...\n", ConsoleColor.White);

            foreach (var soundCommand in rewardCommands) {
                var rewardTitle = soundCommand.RewardTitle;
                WriteDebug($"Обрабатываем: '{rewardTitle}'\n", ConsoleColor.Cyan);

                // Ищем существующую награду
                var existingReward = existingRewards.FirstOrDefault(r =>
                    r.Title.ToLower() == rewardTitle.ToLower());

                if (existingReward != null) {
                    // Награда существует - ОБНОВЛЯЕМ и ВКЛЮЧАЕМ
                    WriteDebug($"  ✅ Награда существует, обновляю...\n", ConsoleColor.Green);

                    try {
                        var updateRequest = new UpdateCustomRewardRequest {
                            Cost = soundCommand.Cost,
                            IsEnabled = true, // ВКЛЮЧАЕМ награду
                            GlobalCooldownSeconds = ConvertCooldownToMinutes(soundCommand.Cooldown),
                            IsGlobalCooldownEnabled = true
                        };

                        var updatedReward = await api.Helix.ChannelPoints.UpdateCustomRewardAsync(
                            channelId, existingReward.Id, updateRequest);

                        if (updatedReward != null) {
                            createdRewardIds.Add(existingReward.Id);
                            updatedCount++;
                            WriteColor($"  ✅ Награда '{rewardTitle}' обновлена и включена\n", ConsoleColor.Green);
                        } else {
                            WriteColor($"  ⚠️ Не удалось обновить награду '{rewardTitle}'\n", ConsoleColor.Yellow);
                        }
                    } catch (Exception ex) {
                        WriteColor($"  ❌ Ошибка обновления награды '{rewardTitle}': {ex.Message}\n", ConsoleColor.Red);
                        lastRewardsError = $"Ошибка обновления наград: {ex.Message}";
                    }
                } else {
                    // Награды нет - СОЗДАЕМ новую
                    WriteDebug($"  ➕ Создаю новую награду...\n", ConsoleColor.Yellow);

                    try {
                        var request = new CreateCustomRewardsRequest {
                            Title = rewardTitle,
                            Cost = soundCommand.Cost,
                            IsEnabled = true,
                            BackgroundColor = "#00FF00",
                            IsUserInputRequired = false,
                            ShouldRedemptionsSkipRequestQueue = false,
                            GlobalCooldownSeconds = ConvertCooldownToMinutes(soundCommand.Cooldown),
                            IsGlobalCooldownEnabled = true
                        };

                        var result = await api.Helix.ChannelPoints.CreateCustomRewardsAsync(channelId, request);

                        if (result != null && result.Data.Length > 0) {
                            createdRewardIds.Add(result.Data[0].Id);
                            createdCount++;
                            WriteColor($"  ✅ Награда '{rewardTitle}' создана\n", ConsoleColor.Green);
                        } else {
                            WriteColor($"  ⚠️ Не удалось создать награду '{rewardTitle}'\n", ConsoleColor.Yellow);
                        }
                    } catch (Exception ex) {
                        WriteColor($"  ❌ Ошибка создания награды '{rewardTitle}': {ex.Message}\n", ConsoleColor.Red);
                        lastRewardsError = $"Ошибка создания наград: {ex.Message}";
                    }
                }

                await Task.Delay(1000); // Задержка между запросами
            }

            // ВКЛЮЧАЕМ все наши награды (на случай если они были отключены)
            WriteDebug($"\nВключаем награды...\n", ConsoleColor.White);
            foreach (var rewardId in createdRewardIds.ToList()) {
                try {
                    var updateRequest = new UpdateCustomRewardRequest {
                        IsEnabled = true
                    };
                    await api.Helix.ChannelPoints.UpdateCustomRewardAsync(channelId, rewardId, updateRequest);
                    enabledCount++;
                    await Task.Delay(500);
                } catch (Exception ex) {
                    WriteDebug($"  ❌ Ошибка включения награды {rewardId}: {ex.Message}\n", ConsoleColor.Red);
                }
            }

            WriteDebug($"\n=== РЕЗУЛЬТАТ ===\n", ConsoleColor.Cyan);
            WriteDebug($"Создано новых: {createdCount}\n", ConsoleColor.Green);
            WriteDebug($"Обновлено: {updatedCount}\n", ConsoleColor.Yellow);
            WriteDebug($"Включено: {enabledCount}\n", ConsoleColor.Blue);
            WriteDebug($"Всего обработано: {rewardCommands.Count}\n", ConsoleColor.White);

            // Успехом считаем если:
            // - есть команды с наградами И мы обработали хотя бы одну ИЛИ
            // - нет команд с наградами
            bool success = (rewardCommands.Count > 0 && (createdCount + updatedCount) > 0) ||
                          (rewardCommands.Count == 0);

            if (!success) {
                lastRewardsError = $"Не удалось создать или обновить ни одной награды. " +
                                 $"Обработано: {createdCount + updatedCount} из {rewardCommands.Count}";
            }

            return success;
        } catch (Exception ex) {
            lastRewardsError = $"Критическая ошибка: {ex.Message}";
            WriteDebug($"❌ Критическая ошибка в CreateCustomRewards: {ex.Message}\n", ConsoleColor.Red);
            return false;
        }
    }

    private int ConvertCooldownToMinutes(int cooldownSeconds) {
        // Округляем в меньшую сторону до минут
        int minutes = cooldownSeconds / 60;
        // Минимальное значение 1 минута, максимальное - 180 минут (3 часа)
        return Math.Clamp(minutes, 1, 180) * 60; // Возвращаем в секундах
    }

    private void ShowSoundList(string channel) {
        var availableCommands = soundCommands.Where(c => c.Value.ChatEnabled).ToList();

        if (availableCommands.Count == 0) {
            client.SendMessage(channel, "Нет доступных звуков.");
            return;
        }

        var message = "Звуки: ";
        foreach (var command in availableCommands.Take(3)) {
            message += $"{command.Key} ";
        }

        if (availableCommands.Count > 3) {
            message += $"... (+{availableCommands.Count - 3})";
        }

        client.SendMessage(channel, message.Trim());
    }

    private void ProcessSoundCommand(string command, string username, string channel, bool isReward) {
        var soundCommand = soundCommands[command];

        if (!File.Exists(soundCommand.SoundFile)) {
            if (!isReward) // Для чата сообщаем об ошибке, для наград - тихо пропускаем
            {
                client.SendMessage(channel, $"{username}, файл не найден!");
            }
            return;
        }

        Task.Run(() => PlaySound(soundCommand.SoundFile, username, command));

        if (!isReward && channel != null) {
            //client.SendMessage(channel, $"{username} активировал {command}!");
        }
    }

    private void PlaySound(string soundFile, string username, string command) {
        lock (soundLock) {
            try {
                using (var audioFile = new AudioFileReader(soundFile))
                using (var outputDevice = new WaveOutEvent()) {
                    // Применяем громкость
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

    private async Task RemoveFromCooldownAfterDelay(string username, int delaySeconds) {
        await Task.Delay(delaySeconds * 1000);
        activeUsers.Remove(username);
    }

    public async Task Disconnect(bool disableRewards = true) {
        try {
            if (disableRewards) {
                await DisableCustomRewards();
                WriteDebug("Награды отключены\n", ConsoleColor.Yellow);
            } else {
                WriteDebug("Награды остаются включенными\n", ConsoleColor.Yellow);
            }
        } catch (Exception ex) {
            WriteDebug($"Ошибка при отключении наград: {ex.Message}\n", ConsoleColor.Red);
        }

        client?.Disconnect();
        pubSub?.Disconnect();
    }

    private async Task DisableCustomRewards() {
        if (createdRewardIds.Count == 0)
            return;

        try {
            // Получаем текущие награды по их ID
            var currentRewards = await api.Helix.ChannelPoints.GetCustomRewardAsync(channelId, createdRewardIds);

            foreach (var reward in currentRewards.Data) {
                try {
                    var updateRequest = new UpdateCustomRewardRequest {
                        IsEnabled = false
                    };
                    await api.Helix.ChannelPoints.UpdateCustomRewardAsync(channelId, reward.Id, updateRequest);
                    WriteDebug($"Награда '{reward.Title}' отключена\n", ConsoleColor.Yellow);
                    await Task.Delay(200);
                } catch (Exception ex) {
                    WriteDebug($"Ошибка отключения награды '{reward.Title}': {ex.Message}\n", ConsoleColor.Red);
                }
            }
        } catch (Exception ex) {
            WriteDebug($"Ошибка при отключении наград: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    private async Task EnableCustomRewards() {
        if (createdRewardIds.Count == 0)
            return;

        try {
            var currentRewards = await api.Helix.ChannelPoints.GetCustomRewardAsync(channelId, createdRewardIds);

            foreach (var reward in currentRewards.Data) {
                try {
                    var updateRequest = new UpdateCustomRewardRequest {
                        IsEnabled = true
                    };
                    await api.Helix.ChannelPoints.UpdateCustomRewardAsync(channelId, reward.Id, updateRequest);
                    await Task.Delay(200);
                } catch { }
            }
        } catch { }
    }

    public void ShowStatistics() {
        Console.Clear();
        WriteColor("=== СТАТИСТИКА КОМАНД ===\n", ConsoleColor.Cyan);
        Console.WriteLine();

        WriteColor($"Всего команд: {soundCommands.Count}\n", ConsoleColor.White);
        WriteColor($"Для чата: {chatEnabledCount}\n", ConsoleColor.Green);
        WriteColor($"Для наград: {rewardEnabledCount}\n", ConsoleColor.Yellow);
        WriteColor($"Всего использований: {totalUsage}\n", ConsoleColor.Cyan);
        Console.WriteLine();

        if (commandUsage.Count == 0) {
            WriteColor("Команды еще не использовались\n", ConsoleColor.Yellow);
        } else {
            WriteColor("Топ команд по использованию:\n", ConsoleColor.White);
            foreach (var cmd in commandUsage.OrderByDescending(x => x.Value).Take(10)) {
                var command = soundCommands[cmd.Key];
                Console.Write($"{cmd.Key}: {cmd.Value} раз");
                Console.Write($" [Чат: {(command.ChatEnabled ? "✓" : "✗")}]");
                Console.Write($" [Награды: {(command.RewardEnabled ? "✓" : "✗")}]");
                Console.WriteLine();
            }
        }

        Console.WriteLine();
        WriteColor("b - Назад\n", ConsoleColor.Gray);
        Console.WriteLine();
    }

    public void ShowPreferences() {
        Console.Clear();
        WriteColor("=== НАСТРОЙКИ ===\n", ConsoleColor.Cyan);
        Console.WriteLine();

        Console.Write("1 - Команды в чате: ");
        WriteColor(settings.ChatEnabled ? "ВКЛ" : "ВЫКЛ", settings.ChatEnabled ? ConsoleColor.Green : ConsoleColor.Red);
        Console.WriteLine();

        Console.Write("2 - Награды Channel Points: ");
        WriteColor(settings.RewardsEnabled ? "ВКЛ" : "ВЫКЛ", settings.RewardsEnabled ? ConsoleColor.Green : ConsoleColor.Red);
        Console.WriteLine();

        Console.Write("t - Режим отладки: ");
        WriteColor(settings.DebugMode ? "ВКЛ" : "ВЫКЛ", settings.DebugMode ? ConsoleColor.Green : ConsoleColor.Red);
        Console.WriteLine();

        Console.Write("v - Громкость: ");
        WriteColor($"{settings.Volume}%\n", ConsoleColor.Cyan);

        Console.WriteLine();
        WriteColor("a - Открыть ссылку авторизации в браузере\n", ConsoleColor.Blue);
        Console.WriteLine();
        WriteColor("b - Назад\n", ConsoleColor.Gray);
        Console.WriteLine();
    }

    private void WriteColor(string text, ConsoleColor color) {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = originalColor;
    }

    private void WriteDebug(string text, ConsoleColor color) {
        if (settings.DebugMode) {
            WriteColor(text, color);
        }
    }
}

public class SoundCommand {
    public string SoundFile { get; set; }
    public int Cost { get; set; }
    public int Cooldown { get; set; }
    public string RewardTitle { get; set; }
    public bool ChatEnabled { get; set; }
    public bool RewardEnabled { get; set; }

    public SoundCommand(string soundFile, int cost, int cooldown, string rewardTitle, bool chatEnabled, bool rewardEnabled) {
        SoundFile = soundFile;
        Cost = cost;
        Cooldown = cooldown;
        RewardTitle = rewardTitle;
        ChatEnabled = chatEnabled;
        RewardEnabled = rewardEnabled;
    }
}

public class BotSettings {
    public string BotUsername { get; set; } = "";
    public string OAuthToken { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string SoundsDirectory { get; set; } = "sounds";
    public int DefaultCooldown { get; set; } = 30;
    public bool ChatEnabled { get; set; } = true;
    public bool RewardsEnabled { get; set; } = true;
    public bool DebugMode { get; set; } = false;
    public int Volume { get; set; } = 50;
}

class Program {
    private static TwitchSoundBot bot;
    private static bool running = true;
    private static bool alreadyDisconnected = false;

    static async Task Main(string[] args) {
        Console.Title = "Twitch Sound Bot (с) RUTONY 2025";

        try {
            bot = new TwitchSoundBot();

            WriteColor("=== Twitch Sound Bot with Rewards ===\n", ConsoleColor.Cyan);

            WriteColor("Подключение...\n", ConsoleColor.Yellow);

            var (authOk, authError, rewardsOk, rewardsError, chatOk, chatError) = await bot.Connect();

            if (bot.IsDebugMode) {
                await bot.CheckTokenScopes();
                Console.WriteLine("Нажмите любую клавишу чтобы продолжить...");
                Console.ReadKey();
            }

            Console.Clear();
            DisplayStatus(authOk, authError, rewardsOk, rewardsError, chatOk, chatError);

            while (running) {
                var key = Console.ReadKey(true);

                switch (key.KeyChar) {
                    case 's':
                    case 'S':
                    case 'ы':
                    case 'Ы':
                        bot.ShowStatistics();
                        WaitForBack();
                        Console.Clear();
                        DisplayStatus(authOk, authError, rewardsOk, rewardsError, chatOk, chatError);
                        break;

                    case 'p':
                    case 'P':
                    case 'з':
                    case 'З':
                        HandlePreferences();
                        Console.Clear();
                        DisplayStatus(authOk, authError, rewardsOk, rewardsError, chatOk, chatError);
                        break;

                    case 'r':
                    case 'R':
                    case 'к':
                    case 'К':
                        Console.Clear();
                        WriteColor("Перезагрузка...\n", ConsoleColor.Yellow);
                        await bot.Disconnect(true); // Отключаем награды при перезагрузке
                        bot = new TwitchSoundBot();
                        (authOk, authError, rewardsOk, rewardsError, chatOk, chatError) = await bot.Connect();
                        Console.Clear();
                        DisplayStatus(authOk, authError, rewardsOk, rewardsError, chatOk, chatError);
                        break;

                    case 'q':
                    case 'Q':
                    case 'й':
                    case 'Й':
                        if (!alreadyDisconnected) {
                            alreadyDisconnected = true;
                            running = false;
                            WriteColor("Выход без отключения наград...\n", ConsoleColor.Yellow);
                            await bot.Disconnect(false);
                        }
                        break;

                    case 'w':
                    case 'W':
                    case 'ц':
                    case 'Ц':
                        if (!alreadyDisconnected) {
                            alreadyDisconnected = true;
                            running = false;
                            WriteColor("Выход с отключением наград...\n", ConsoleColor.Yellow);
                            await bot.Disconnect(true);
                        }
                        break;
                }
            }

            WriteColor("Программа завершена.\n", ConsoleColor.Yellow);
        } catch (Exception ex) {
            WriteColor($"Ошибка: {ex.Message}\n", ConsoleColor.Red);
            WriteColor("Нажмите любую клавишу...\n", ConsoleColor.Gray);
            Console.ReadKey();
        }
    }

    static void DisplayStatus(bool authOk, string authError, bool rewardsOk, string rewardsError, bool chatOk, string chatError) {
        WriteColor("=== Twitch Sound Bot with Rewards ===\n", ConsoleColor.Cyan);
        Console.WriteLine();
        Console.WriteLine($"Канал: {bot.GetChannelName()}");

        Console.Write("Авторизация: ");
        if (authOk) {
            WriteColor("OK\n", ConsoleColor.Green);
        } else {
            WriteColor("Ошибка\n", ConsoleColor.Red);
            if (!string.IsNullOrEmpty(authError)) {
                WriteColor($"  {authError}\n", ConsoleColor.Yellow);
            }
        }

        Console.WriteLine($"Количество звуковых команд: {bot.GetTotalCommands()}");
        Console.WriteLine($"  - Для чата: {bot.GetChatEnabledCount()}");
        Console.WriteLine($"  - Для наград: {bot.GetRewardEnabledCount()}");

        // Показываем отсутствующие файлы
        var missingFiles = bot.GetMissingFiles();
        if (missingFiles.Count > 0) {
            WriteColor($"Отсутствуют файлы: {missingFiles.Count}\n", ConsoleColor.Yellow);
            foreach (var file in missingFiles) {
                WriteColor($"  {file}\n", ConsoleColor.Yellow);
            }
        }

        Console.Write("Награды: ");
        if (bot.RewardsEnabled) {
            WriteColor(rewardsOk ? "OK" : "Ошибка", rewardsOk ? ConsoleColor.Green : ConsoleColor.Red);
            Console.WriteLine($" ({(bot.RewardsEnabled ? "ВКЛ" : "ВЫКЛ")})");
            if (!rewardsOk) {
                // ВЫВОДИМ ДЕТАЛИ ОШИБКИ
                string errorDetails = bot.GetLastRewardsError();
                if (!string.IsNullOrEmpty(errorDetails)) {
                    WriteColor($"  {errorDetails}\n", ConsoleColor.Yellow);
                } else {
                    WriteColor($"  Неизвестная ошибка при создании/обновлении наград\n", ConsoleColor.Yellow);
                }
            }
        } else {
            WriteColor("ВЫКЛ\n", ConsoleColor.Gray);
        }

        Console.Write("Команды в чате: ");
        if (bot.ChatEnabled) {
            WriteColor(chatOk ? "OK" : "Ошибка", chatOk ? ConsoleColor.Green : ConsoleColor.Red);
            Console.WriteLine($" ({(bot.ChatEnabled ? "ВКЛ" : "ВЫКЛ")})");
            if (!chatOk && !string.IsNullOrEmpty(chatError)) {
                WriteColor($"  {chatError}\n", ConsoleColor.Yellow);
            }
        } else {
            WriteColor("ВЫКЛ\n", ConsoleColor.Gray);
        }

        Console.WriteLine($"Всего использований: {bot.GetTotalUsage()}");
        Console.WriteLine();
        WriteColor("s - Статистика по командам\n", ConsoleColor.White);
        WriteColor("p - Настройки\n", ConsoleColor.White);
        WriteColor("r - Перезагрузить\n", ConsoleColor.White);
        WriteColor("w - Выход с отключением наград\n", ConsoleColor.White);
        WriteColor("q - Выход\n", ConsoleColor.White);
        Console.WriteLine();
    }

    static void HandlePreferences() {
        bot.ShowPreferences();

        while (true) {
            var key = Console.ReadKey(true);
            switch (key.KeyChar) {
                case '1':
                    bot.ToggleChat();
                    bot.ShowPreferences();
                    break;

                case '2':
                    bot.ToggleRewards();
                    bot.ShowPreferences();
                    break;

                case 't':
                case 'T':
                case 'е':
                case 'Е':
                    bot.ToggleDebugMode();
                    bot.ShowPreferences();
                    break;

                case 'v':
                case 'V':
                case 'м':
                case 'М':
                    bot.ChangeVolume();
                    bot.ShowPreferences();
                    break;

                case 'a':
                case 'A':
                case 'ф':
                case 'Ф':
                    OpenAuthInBrowser();
                    bot.ShowPreferences();
                    break;

                case 'b':
                case 'B':
                case 'и':
                case 'И':
                    return;
            }
        }
    }

    static void OpenAuthInBrowser() {
        try {
            var authUrl = bot.GetAuthUrl();
            WriteColor($"Открываю ссылку авторизации в браузере...\n", ConsoleColor.Yellow);
            WriteColor($"Ссылка: {authUrl}\n", ConsoleColor.Cyan);

            // Пытаемся открыть в браузере по умолчанию
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                FileName = authUrl,
                UseShellExecute = true
            });

            WriteColor("\nИнструкция:\n", ConsoleColor.White);
            WriteColor("1. Авторизуйтесь в Twitch\n", ConsoleColor.Gray);
            WriteColor("2. Скопируйте токен из адресной строки (часть после 'access_token=')\n", ConsoleColor.Gray);
            WriteColor("3. Обновите файл bot_settings.txt с новым токеном\n", ConsoleColor.Gray);
            WriteColor("4. Перезапустите бота (клавиша 'r')\n", ConsoleColor.Gray);
            WriteColor("\nНажмите любую клавишу чтобы продолжить...\n", ConsoleColor.Yellow);
            Console.ReadKey();
        } catch (Exception ex) {
            WriteColor($"Ошибка открытия браузера: {ex.Message}\n", ConsoleColor.Red);
            WriteColor($"Скопируйте ссылку вручную: {bot.GetAuthUrl()}\n", ConsoleColor.Cyan);
            WriteColor("Нажмите любую клавишу чтобы продолжить...\n", ConsoleColor.Yellow);
            Console.ReadKey();
        }
    }

    static void WaitForBack() {
        while (true) {
            var key = Console.ReadKey(true);
            if (key.KeyChar == 'b' || key.KeyChar == 'B' || key.KeyChar == 'и' || key.KeyChar == 'И')
                break;
        }
    }

    static void WriteColor(string text, ConsoleColor color) {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = originalColor;
    }
}