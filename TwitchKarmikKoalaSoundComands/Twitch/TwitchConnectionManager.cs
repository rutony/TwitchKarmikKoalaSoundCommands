using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using TwitchLib.PubSub;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class TwitchConnectionManager {
    private TwitchClient client;
    private TwitchAPI api;
    private TwitchPubSub pubSub;
    private BotSettings settings;
    private string? channelId;
    private string? botId;

    public event EventHandler<(string username, string message)> OnChatCommand;
    public event EventHandler<(string command, string username)> OnRewardRedeemed;
    public event EventHandler<(string rewardId, string rewardTitle)> OnRewardMappingUpdated;

    private Dictionary<string, string> rewardIdToCommandMap;
    private List<string> createdRewardIds = new List<string>();

    // Добавляем свойства для доступа к приватным полям
    public TwitchAPI Api => api;
    public string ChannelId => channelId;

    public TwitchConnectionManager(BotSettings settings) {
        this.settings = settings;
        rewardIdToCommandMap = new Dictionary<string, string>();
    }

    public async Task ReinitializeApi() {
        if (api != null) {
            api.Settings.ClientId = settings.ClientId;

            string apiToken = settings.OAuthToken;
            if (apiToken.StartsWith("oauth:")) {
                apiToken = apiToken.Substring(6);
            }
            api.Settings.AccessToken = apiToken;
        }
    }

    public void SendMessage(string message) {
        if (client != null && client.IsConnected) {
            client.SendMessage(settings.ChannelName, message);
        }
    }

    public async Task BanUser(string username, int durationMinutes) {
        try {
            // Получаем ID пользователя
            var users = await api.Helix.Users.GetUsersAsync(logins: new List<string> { username });
            if (users.Users.Length == 0) {
                WriteDebug($"❌ Пользователь {username} не найден\n", ConsoleColor.Red);
                return;
            }

            var userId = users.Users[0].Id;

            // Используем broadcasterId как moderatorId, так как это один и тот же аккаунт
            var banRequest = new TwitchLib.Api.Helix.Models.Moderation.BanUser.BanUserRequest {
                UserId = userId,
                Duration = durationMinutes * 60,
                Reason = "Неудачная попытка кражи VIP"
            };

            await api.Helix.Moderation.BanUserAsync(
                broadcasterId: channelId,
                moderatorId: channelId, // используем channelId как moderatorId
                banUserRequest: banRequest
            );

            if (settings.DebugMode) {
                WriteDebug($"🔨 Пользователь {username} забанен на {durationMinutes} минут\n", ConsoleColor.Red);
            }
        } catch (Exception ex) {
            WriteDebug($"❌ Ошибка бана пользователя {username}: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    public async Task<(bool authOk, string authError, bool rewardsOk, string rewardsError, bool chatOk, string chatError)> Connect() {
        
        if (api == null) {
            api = new TwitchAPI();
        }
        await ReinitializeApi();

        api.Settings.ClientId = settings.ClientId;

        string apiToken = settings.OAuthToken;
        if (apiToken.StartsWith("oauth:")) {
            apiToken = apiToken.Substring(6);
        }
        api.Settings.AccessToken = apiToken;

        // Получаем ID бота и канала
        string authError = "";
        try {
            // Получаем ID бота
            var botUsers = await api.Helix.Users.GetUsersAsync(logins: new List<string> { settings.BotUsername });
            if (botUsers.Users.Length > 0) {
                botId = botUsers.Users[0].Id;
                if (settings.DebugMode) {
                    WriteDebug($"✅ Получен botId: {botId} для бота {settings.BotUsername}\n", ConsoleColor.Green);
                }
            }

            // Получаем ID канала
            var channelUsers = await api.Helix.Users.GetUsersAsync(logins: new List<string> { settings.ChannelName });
            if (channelUsers.Users.Length > 0) {
                channelId = channelUsers.Users[0].Id;
                if (settings.DebugMode) {
                    WriteDebug($"✅ Получен channelId: {channelId} для канала {settings.ChannelName}\n", ConsoleColor.Green);
                }
            } else {
                authError = $"Канал {settings.ChannelName} не найден";
                return (false, authError, false, "", false, "");
            }

            // Дополнительная проверка аутентификации
            try {
                var validation = await api.Auth.ValidateAccessTokenAsync(api.Settings.AccessToken);
                if (validation != null) {
                    WriteDebug($"✅ Аутентификация подтверждена: {validation.UserId}\n", ConsoleColor.Green);
                }
            } catch (Exception authEx) {
                WriteDebug($"❌ Ошибка аутентификации: {authEx.Message}\n", ConsoleColor.Red);
            }

        } catch (Exception ex) {
            authError = $"Ошибка получения ID: {ex.Message}";
            WriteDebug($"❌ Ошибка: {authError}\n", ConsoleColor.Red);
            return (false, authError, false, "", false, "");
        }

        WriteDebug($"🔍 Диагностика: BotUsername='{settings.BotUsername}', ChannelName='{settings.ChannelName}'\n", ConsoleColor.Yellow);
        WriteDebug($"🔍 Диагностика: BotId='{botId}', ChannelId='{channelId}'\n", ConsoleColor.Yellow);

        bool chatOk = false;
        bool rewardsOk = false;
        string chatError = "";
        string rewardsError = "";

        if (settings.ChatEnabled) {
            try {
                var credentials = new ConnectionCredentials(settings.BotUsername, settings.OAuthToken);
                var clientOptions = new ClientOptions {
                    MessagesAllowedInPeriod = 750,
                    ThrottlingPeriod = TimeSpan.FromSeconds(30)
                };

                var customClient = new WebSocketClient(clientOptions);
                client = new TwitchClient(customClient);

                client.Initialize(credentials, settings.ChannelName);
                client.OnJoinedChannel += OnJoinedChannel;
                client.OnMessageReceived += OnMessageReceived;

                client.Connect();
                chatOk = await WaitForChatConnection();
                if (!chatOk) {
                    chatError = "Таймаут подключения к чату";
                } else {
                    WriteDebug("✅ Чат подключен успешно\n", ConsoleColor.Green);
                }
            } catch (Exception ex) {
                chatError = $"Ошибка чата: {ex.Message}";
            }
        }

        if (settings.RewardsEnabled) {
            // Награды будут создаваться позже через RewardManager
            rewardsOk = true;
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

    private void OnMessageReceived(object sender, OnMessageReceivedArgs e) {
        if (!settings.ChatEnabled)
            return;

        var message = e.ChatMessage.Message.ToLower();
        var username = e.ChatMessage.Username;
        OnChatCommand?.Invoke(this, (username, message));
    }

    private void InitializePubSub() {
        pubSub = new TwitchPubSub();
        pubSub.OnRewardRedeemed += OnRewardRedeemedHandler;
        pubSub.OnPubSubServiceConnected += OnPubSubServiceConnected;
        pubSub.OnListenResponse += OnListenResponse;

        pubSub.Connect();

        Task.Run(async () => {
            await Task.Delay(2000);
            pubSub.ListenToRewards(channelId);
            pubSub.SendTopics(settings.OAuthToken.Replace("oauth:", ""));
        });
    }

    private void OnRewardRedeemedHandler(object sender, TwitchLib.PubSub.Events.OnRewardRedeemedArgs e) {
        if (!settings.RewardsEnabled)
            return;

        if (settings.DebugMode) {
            WriteDebug($"🎁 Активирована награда: '{e.RewardTitle}' пользователем {e.DisplayName}\n", ConsoleColor.Magenta);
        }

        // Сначала проверяем маппинг по ID
        if (rewardIdToCommandMap.TryGetValue(e.RewardId.ToString(), out string command)) {
            OnRewardRedeemed?.Invoke(this, (command, e.DisplayName));
        }
        // Затем проверяем VIP награды по названию
        else if (e.RewardTitle == "Купить VIP") {
            OnRewardRedeemed?.Invoke(this, ("VIP_PURCHASE", e.DisplayName));
        } else if (e.RewardTitle == "Украсть VIP") {
            OnRewardRedeemed?.Invoke(this, ("VIP_STEAL", e.DisplayName));
        } else {
            // Если не нашли по ID, пробуем найти по названию для обычных команд
            OnRewardMappingUpdated?.Invoke(this, (e.RewardId.ToString(), e.RewardTitle));
        }
    }

    private void OnPubSubServiceConnected(object sender, EventArgs e) {
        WriteDebug("✅ PubSub подключен\n", ConsoleColor.Green);
    }

    private void OnListenResponse(object sender, TwitchLib.PubSub.Events.OnListenResponseArgs e) {
        if (!e.Successful) {
            WriteDebug($"❌ Ошибка подписки на тему: {e.Topic}\n", ConsoleColor.Red);
        } else {
            WriteDebug($"✅ Успешная подписка на тему: {e.Topic}\n", ConsoleColor.Green);
        }
    }

    public async Task Disconnect(bool disableRewards = true) {
        client?.Disconnect();
        pubSub?.Disconnect();
        WriteDebug("Соединение закрыто\n", ConsoleColor.Yellow);
    }

    // Метод для добавления сопоставления наград
    public void AddRewardMapping(string rewardId, string command) {
        rewardIdToCommandMap[rewardId] = command;
        if (settings.DebugMode) {
            WriteDebug($"✅ Сопоставлена награда ID '{rewardId}' -> команда '{command}'\n", ConsoleColor.Green);
        }
    }

    private void WriteDebug(string text, ConsoleColor color) {
        if (settings.DebugMode) {
            WriteColor(text, color);
        }
    }

    private void WriteColor(string text, ConsoleColor color) {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = originalColor;
    }
}