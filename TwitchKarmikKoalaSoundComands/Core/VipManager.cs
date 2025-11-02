using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.ChannelPoints;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomReward;
using System.Net.Http;
using System.Text;

public class VipManager {
    private TwitchAPI api;
    private string channelId;
    private BotSettings settings;
    private string vipListFile = "config/vip_list.json";
    private List<VipItem> vipList = new List<VipItem>();
    private Random random = new Random();

    private List<string> successfulStealMessages = new List<string>();
    private List<string> failedStealMessages = new List<string>();

    private DateTime lastSyncTime = DateTime.MinValue;
    private readonly TimeSpan syncCooldown = TimeSpan.FromMinutes(5);

    public string LastError { get; private set; } = "";
    private const int MAX_REWARDS = 50;
    private const int MAX_VIP_LIMIT = 100;

    public VipManager(TwitchAPI api, string channelId, BotSettings settings) {
        this.api = api;
        this.channelId = channelId;
        this.settings = settings;

        if (!Directory.Exists("config")) {
            Directory.CreateDirectory("config");
        }

        LoadVipList();
        LoadStealMessages();
    }

    public async Task<bool> CreateVipRewards() {
        try {
            WriteDebug($"=== СОЗДАНИЕ VIP НАГРАД ===\n", ConsoleColor.Cyan);

            if (string.IsNullOrEmpty(channelId)) {
                LastError = "ChannelId не доступен";
                WriteColor($"❌ {LastError}\n", ConsoleColor.Red);
                return false;
            }

            var existingRewardsResponse = await api.Helix.ChannelPoints.GetCustomRewardAsync(channelId, new List<string>(), true);

            if (existingRewardsResponse == null || existingRewardsResponse.Data == null) {
                LastError = "Не удалось получить список существующих наград";
                WriteColor($"❌ {LastError}\n", ConsoleColor.Red);
                return false;
            }

            var existingRewards = existingRewardsResponse.Data;
            WriteDebug($"✅ Найдено существующих наград: {existingRewards.Length}\n", ConsoleColor.Green);

            bool vipPurchaseCreated = false;
            bool vipStealCreated = false;

            await Task.Delay(500);
            if (settings.EnableVipReward) {
                vipPurchaseCreated = await CreateOrUpdateVipReward(
                    "Купить VIP",
                    settings.VipRewardCost,
                    settings.VipCooldown * 60,
                    "#FFD700",
                    existingRewards
                );
            }

            await Task.Delay(500);
            if (settings.EnableVipStealReward) {
                vipStealCreated = await CreateOrUpdateVipReward(
                    "Украсть VIP",
                    settings.VipStealCost,
                    600,
                    "#FF0000",
                    existingRewards
                );
            }

            WriteDebug($"\n=== РЕЗУЛЬТАТ СОЗДАНИЯ VIP НАГРАД ===\n", ConsoleColor.Cyan);
            if (settings.EnableVipReward) {
                WriteDebug($"Купить VIP: {(vipPurchaseCreated ? "✅" : "❌")}\n",
                          vipPurchaseCreated ? ConsoleColor.Green : ConsoleColor.Red);
            }
            if (settings.EnableVipStealReward) {
                WriteDebug($"Украсть VIP: {(vipStealCreated ? "✅" : "❌")}\n",
                          vipStealCreated ? ConsoleColor.Green : ConsoleColor.Red);
            }

            return (settings.EnableVipReward ? vipPurchaseCreated : true) &&
                   (settings.EnableVipStealReward ? vipStealCreated : true);

        } catch (Exception ex) {
            LastError = $"Критическая ошибка создания VIP наград: {ex.Message}";
            WriteDebug($"❌ {LastError}\n", ConsoleColor.Red);
            return false;
        }
    }

    private async Task<bool> CreateOrUpdateVipReward(string rewardTitle, int cost, int cooldownSeconds = 0, string color = "", CustomReward[] existingRewards = null) {
        try {
            WriteDebug($"🔍 Обрабатываем VIP награду: '{rewardTitle}'\n", ConsoleColor.Cyan);

            var existingReward = existingRewards?.FirstOrDefault(r =>
                r.Title.ToLower() == rewardTitle.ToLower());

            if (existingReward != null) {
                WriteDebug($"  ✅ Награда существует, обновляю...\n", ConsoleColor.Green);

                var updateRequest = new UpdateCustomRewardRequest {
                    Cost = cost,
                    IsEnabled = true
                };

                try {
                    var updatedReward = await api.Helix.ChannelPoints.UpdateCustomRewardAsync(
                        channelId, existingReward.Id, updateRequest);

                    if (updatedReward != null) {
                        WriteColor($"  ✅ Награда '{rewardTitle}' обновлена\n", ConsoleColor.Green);
                        return true;
                    }
                } catch (Exception updateEx) {
                    WriteColor($"  ❌ Ошибка обновления награды '{rewardTitle}': {updateEx.Message}\n", ConsoleColor.Red);
                }
            } else {
                WriteDebug($"  ➕ Создаю новую VIP награду...\n", ConsoleColor.Yellow);
                return await CreateNewVipReward(rewardTitle, cost, cooldownSeconds, color);
            }

            return false;
        } catch (Exception ex) {
            WriteColor($"  ❌ Критическая ошибка обработки награды '{rewardTitle}': {ex.Message}\n", ConsoleColor.Red);
            return false;
        }
    }

    private async Task<bool> CreateNewVipReward(string rewardTitle, int cost, int cooldownSeconds, string color) {
        try {
            var createRequest = new CreateCustomRewardsRequest {
                Title = rewardTitle,
                Cost = cost,
                IsEnabled = true,
                BackgroundColor = color,
                IsUserInputRequired = false
            };

            var result = await api.Helix.ChannelPoints.CreateCustomRewardsAsync(channelId, createRequest);

            if (result != null && result.Data.Length > 0) {
                WriteColor($"  ✅ Награда '{rewardTitle}' создана\n", ConsoleColor.Green);
                return true;
            } else {
                WriteColor($"  ❌ Награда '{rewardTitle}' не создана - пустой ответ\n", ConsoleColor.Red);
            }
        } catch (Exception createEx) {
            WriteColor($"  ❌ Ошибка создания награды '{rewardTitle}': {createEx.Message}\n", ConsoleColor.Red);
        }

        return false;
    }

    public async Task DisableVipRewards() {
        try {
            if (string.IsNullOrEmpty(channelId)) {
                WriteColor("❌ ChannelId не доступен для отключения VIP наград\n", ConsoleColor.Red);
                return;
            }

            var rewards = await api.Helix.ChannelPoints.GetCustomRewardAsync(channelId, onlyManageableRewards: true);
            var vipRewards = rewards.Data.Where(r => r.Title == "Купить VIP" || r.Title == "Украсть VIP").ToList();

            foreach (var reward in vipRewards) {
                try {
                    var updateRequest = new UpdateCustomRewardRequest { IsEnabled = false };
                    await api.Helix.ChannelPoints.UpdateCustomRewardAsync(channelId, reward.Id, updateRequest);
                    WriteDebug($"Награда '{reward.Title}' отключена\n", ConsoleColor.Yellow);
                    await Task.Delay(200);
                } catch (Exception ex) {
                    WriteDebug($"Ошибка отключения награды '{reward.Title}': {ex.Message}\n", ConsoleColor.Red);
                }
            }
        } catch (Exception ex) {
            WriteDebug($"Ошибка при отключении VIP наград: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    // ОСНОВНОЙ МЕТОД: Получение VIP через Twitch API с проверкой прав
    private async Task<List<string>> GetRealVipUsersFromTwitchAsync() {
        try {
            WriteDebug($"🔍 Запрос списка VIP с Twitch API...\n", ConsoleColor.Cyan);

            using (var httpClient = new HttpClient()) {
                // Устанавливаем заголовки для Twitch API
                httpClient.DefaultRequestHeaders.Add("Client-ID", api.Settings.ClientId);
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {api.Settings.AccessToken}");

                string url = $"https://api.twitch.tv/helix/channels/vips?broadcaster_id={channelId}&first=100";
                WriteDebug($"📤 URL: {url}\n", ConsoleColor.Yellow);

                var response = await httpClient.GetAsync(url);
                var responseContent = await response.Content.ReadAsStringAsync();

                WriteDebug($"📥 Статус: {response.StatusCode}\n", ConsoleColor.Yellow);

                if (settings.DebugMode) {
                    WriteDebug($"📄 Ответ: {responseContent}\n", ConsoleColor.Gray);
                }

                if (response.IsSuccessStatusCode) {
                    using (JsonDocument doc = JsonDocument.Parse(responseContent)) {
                        var vips = new List<string>();

                        if (doc.RootElement.TryGetProperty("data", out JsonElement dataElement)) {
                            foreach (JsonElement vipElement in dataElement.EnumerateArray()) {
                                string username = null;

                                // Пробуем разные возможные поля с именем
                                if (vipElement.TryGetProperty("user_login", out JsonElement loginElement)) {
                                    username = loginElement.GetString();
                                } else if (vipElement.TryGetProperty("user_name", out JsonElement nameElement)) {
                                    username = nameElement.GetString();
                                } else if (vipElement.TryGetProperty("login", out JsonElement loginElement2)) {
                                    username = loginElement2.GetString();
                                }

                                if (!string.IsNullOrEmpty(username)) {
                                    vips.Add(username.ToLower());
                                    WriteDebug($"✅ VIP: {username}\n", ConsoleColor.Green);
                                }
                            }
                        }

                        WriteDebug($"✅ Получено VIP: {vips.Count}\n", ConsoleColor.Green);
                        return vips;
                    }
                } else {
                    WriteDebug($"❌ Ошибка API: {response.StatusCode}\n", ConsoleColor.Red);

                    // Анализируем ошибку
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) {
                        WriteDebug($"🔐 Ошибка 401: Неверный токен\n", ConsoleColor.Red);
                    } else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
                        WriteDebug($"🚫 Ошибка 403: Недостаточно прав. Нужен scope: channel:read:vips\n", ConsoleColor.Red);
                    } else if (response.StatusCode == System.Net.HttpStatusCode.NotFound) {
                        WriteDebug($"🔍 Ошибка 404: Endpoint не найден\n", ConsoleColor.Red);
                    } else {
                        WriteDebug($"❌ Другая ошибка: {responseContent}\n", ConsoleColor.Red);
                    }
                }
            }
        } catch (Exception ex) {
            WriteDebug($"❌ Исключение при запросе VIP: {ex.Message}\n", ConsoleColor.Red);
        }

        return new List<string>();
    }

    // МЕТОД СИНХРОНИЗАЦИИ С ДЕТАЛЬНЫМ ЛОГИРОВАНИЕМ
    public async Task SyncWithRealVipList() {
        try {
            // Увеличиваем интервал синхронизации до 10 минут чтобы избежать спама
            if (DateTime.Now - lastSyncTime < TimeSpan.FromMinutes(10)) {
                return;
            }

            WriteColor($"🔄 Синхронизация VIP списка...\n", ConsoleColor.Cyan);
            lastSyncTime = DateTime.Now;

            // Получаем реальных VIP с Twitch
            var realVips = await GetRealVipUsersFromTwitchAsync();

            if (!realVips.Any()) {
                WriteColor($"⚠️ Не удалось получить VIP с Twitch или их нет\n", ConsoleColor.Yellow);
                return;
            }

            WriteDebug($"📋 Найдено VIP на канале: {realVips.Count}\n", ConsoleColor.Green);

            // Очищаем некорректные записи (старая логика)
            var invalidEntries = vipList.Where(v =>
                v.Username.StartsWith("[{\"") ||
                v.Username.Contains("\\u") ||
                string.IsNullOrWhiteSpace(v.Username)).ToList();

            foreach (var invalid in invalidEntries) {
                vipList.Remove(invalid);
                WriteDebug($"🗑️ Удалена некорректная запись: {invalid.Username}\n", ConsoleColor.Yellow);
            }

            // ОСНОВНОЕ ИСПРАВЛЕНИЕ: Обновляем даты для существующих VIP
            int updatedCount = 0;
            int addedCount = 0;

            foreach (var vipUsername in realVips) {
                var normalizedUsername = vipUsername.ToLower();
                var existingVip = vipList.FirstOrDefault(v =>
                    v.Username.Equals(normalizedUsername, StringComparison.OrdinalIgnoreCase));

                if (existingVip != null) {
                    // ОБНОВЛЯЕМ дату окончания если VIP просрочен или дата некорректная
                    if (existingVip.IsExpired || existingVip.ExpiryDate.Year < 2024) {
                        existingVip.GrantDate = DateTime.Now;
                        existingVip.ExpiryDate = DateTime.Now.AddDays(settings.VipDurationDays);
                        updatedCount++;
                        WriteDebug($"🔄 Обновлен срок VIP: {normalizedUsername}\n", ConsoleColor.Yellow);
                    }
                } else {
                    // Добавляем нового VIP
                    var newVip = new VipItem(normalizedUsername, DateTime.Now, settings.VipDurationDays);
                    vipList.Add(newVip);
                    addedCount++;
                    WriteDebug($"➕ Добавлен VIP: {normalizedUsername}\n", ConsoleColor.Green);
                }
            }

            // Удаляем VIP которых нет на канале (опционально)
            var vipUsernames = realVips.Select(v => v.ToLower()).ToList();
            var missingVips = vipList.Where(v => !vipUsernames.Contains(v.Username.ToLower())).ToList();

            foreach (var missing in missingVips) {
                vipList.Remove(missing);
                WriteDebug($"➖ Удален отсутствующий VIP: {missing.Username}\n", ConsoleColor.Gray);
            }

            // Сохраняем обновленный список
            SaveVipList();

            var activeVips = vipList.Count(v => !v.IsExpired);
            WriteColor($"✅ Синхронизация завершена:\n", ConsoleColor.Green);
            WriteColor($"   Активных VIP: {activeVips}\n", ConsoleColor.White);
            WriteColor($"   Добавлено: {addedCount}\n", ConsoleColor.White);
            WriteColor($"   Обновлено: {updatedCount}\n", ConsoleColor.White);
            WriteColor($"   Удалено: {missingVips.Count}\n", ConsoleColor.White);

            if (settings.DebugMode && activeVips > 0) {
                var vipNames = vipList.Where(v => !v.IsExpired).Select(v => v.Username).ToList();
                WriteDebug($"📋 Текущие VIP: {string.Join(", ", vipNames)}\n", ConsoleColor.Cyan);
            }

        } catch (Exception ex) {
            WriteColor($"❌ Ошибка синхронизации: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    public async Task UpdateVipPurchaseRewardAvailability() {
        try {
            if (!settings.EnableVipReward || string.IsNullOrEmpty(channelId))
                return;

            var activeVipCount = await GetActiveVipCountAsync();
            var isAvailable = activeVipCount < settings.MaxVipCount;

            // Получаем существующие награды
            var existingRewardsResponse = await api.Helix.ChannelPoints.GetCustomRewardAsync(channelId, new List<string>(), true);
            var existingRewards = existingRewardsResponse?.Data ?? Array.Empty<CustomReward>();

            var vipPurchaseReward = existingRewards.FirstOrDefault(r =>
                r.Title.ToLower() == "купить vip");

            if (vipPurchaseReward != null) {
                // Обновляем доступность награды
                var updateRequest = new UpdateCustomRewardRequest {
                    IsEnabled = isAvailable
                };

                await api.Helix.ChannelPoints.UpdateCustomRewardAsync(channelId, vipPurchaseReward.Id, updateRequest);

                if (settings.DebugMode) {
                    WriteColor($"✅ Награда 'Купить VIP': {(isAvailable ? "ДОСТУПНА" : "НЕДОСТУПНА")} (VIP: {activeVipCount}/{settings.MaxVipCount})\n",
                        isAvailable ? ConsoleColor.Green : ConsoleColor.Yellow);
                }
            }
        } catch (Exception ex) {
            WriteColor($"❌ Ошибка обновления награды покупки VIP: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    public async Task<bool> RemoveAllVips(bool confirm = false) {
        if (!confirm) {
            WriteColor("⚠️  Для удаления всех VIP требуется подтверждение!\n", ConsoleColor.Yellow);
            return false;
        }

        try {
            WriteColor("🗑️  Удаление всех VIP...\n", ConsoleColor.Red);

            // Получаем реальных VIP с Twitch
            var realVips = await GetRealVipUsersFromTwitchAsync();

            if (!realVips.Any()) {
                WriteColor("ℹ️  Нет VIP для удаления\n", ConsoleColor.Yellow);
                return true;
            }

            // Очищаем локальный список
            vipList.Clear();
            SaveVipList();

            WriteColor($"✅ Удалено {realVips.Count} VIP из локального списка\n", ConsoleColor.Green);
            WriteColor("ℹ️  Для полного удаления VIP с канала используйте панель управления Twitch\n", ConsoleColor.Yellow);

            return true;
        } catch (Exception ex) {
            WriteColor($"❌ Ошибка удаления VIP: {ex.Message}\n", ConsoleColor.Red);
            return false;
        }
    }

    public void ManuallyAddVipUsers(List<string> usernames) {
        try {
            WriteColor($"🔄 Ручное добавление VIP...\n", ConsoleColor.Cyan);

            foreach (var username in usernames) {
                var normalizedUsername = username.ToLower();
                var existingVip = vipList.FirstOrDefault(v =>
                    v.Username.Equals(normalizedUsername, StringComparison.OrdinalIgnoreCase));

                if (existingVip == null) {
                    var newVip = new VipItem(normalizedUsername, DateTime.Now, settings.VipDurationDays);
                    vipList.Add(newVip);
                    WriteColor($"✅ Добавлен VIP: {normalizedUsername}\n", ConsoleColor.Green);
                } else {
                    existingVip.GrantDate = DateTime.Now;
                    existingVip.ExpiryDate = DateTime.Now.AddDays(settings.VipDurationDays);
                    WriteColor($"✅ Обновлен VIP: {normalizedUsername}\n", ConsoleColor.Yellow);
                }
            }

            SaveVipList();
            WriteColor($"✅ Ручное добавление завершено. Всего VIP: {vipList.Count(v => !v.IsExpired)}\n", ConsoleColor.Green);
        } catch (Exception ex) {
            WriteColor($"❌ Ошибка ручного добавления: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    public async Task<bool> PurchaseVip(string username) {
        try {
            await SyncWithRealVipList();

            var currentVips = await GetActiveVipCountAsync();

            // Проверяем лимит
            if (currentVips >= settings.MaxVipCount) {
                WriteColor($"❌ Достигнут лимит VIP ({currentVips}/{settings.MaxVipCount})\n", ConsoleColor.Red);
                await UpdateVipPurchaseRewardAvailability();
                return false;
            }

            // Проверяем, не является ли пользователь уже VIP
            if (await IsVipAsync(username)) {
                WriteColor($"❌ {username} уже VIP\n", ConsoleColor.Red);
                return false;
            }

            // Выдаем VIP
            var existingVip = vipList.FirstOrDefault(v => v.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (existingVip != null) {
                // Продлеваем существующий VIP
                existingVip.GrantDate = DateTime.Now;
                existingVip.ExpiryDate = DateTime.Now.AddDays(settings.VipDurationDays);
                WriteColor($"✅ {username} продлил VIP\n", ConsoleColor.Green);
            } else {
                // Создаем новый VIP
                var vipItem = new VipItem(username, DateTime.Now, settings.VipDurationDays);
                vipList.Add(vipItem);
                WriteColor($"✅ {username} стал VIP\n", ConsoleColor.Green);
            }

            SaveVipList();

            // Обновляем доступность награды
            await UpdateVipPurchaseRewardAvailability();

            return true;

        } catch (Exception ex) {
            WriteColor($"❌ Ошибка покупки VIP: {ex.Message}\n", ConsoleColor.Red);
            return false;
        }
    }

    public async Task<int> GetActiveVipCountAsync() {
        try {
            await SyncWithRealVipList();
            return vipList.Count(v => !v.IsExpired);
        } catch (Exception ex) {
            WriteDebug($"❌ Ошибка получения количества VIP: {ex.Message}\n", ConsoleColor.Red);
            return vipList.Count(v => !v.IsExpired);
        }
    }

    public int GetActiveVipCount() {
        try {
            var task = Task.Run(async () => await GetActiveVipCountAsync());
            task.Wait(TimeSpan.FromSeconds(5));
            return task.IsCompleted ? task.Result : vipList.Count(v => !v.IsExpired);
        } catch {
            return vipList.Count(v => !v.IsExpired);
        }
    }

    public async Task<List<string>> GetVipUsersAsync() {
        try {
            await SyncWithRealVipList();
            return vipList.Where(v => !v.IsExpired).Select(v => v.Username).ToList();
        } catch (Exception ex) {
            WriteDebug($"❌ Ошибка получения списка VIP: {ex.Message}\n", ConsoleColor.Red);
            return vipList.Where(v => !v.IsExpired).Select(v => v.Username).ToList();
        }
    }

    public List<string> GetVipUsers() {
        try {
            var task = Task.Run(async () => await GetVipUsersAsync());
            task.Wait(TimeSpan.FromSeconds(5));
            return task.IsCompleted ? task.Result : vipList.Where(v => !v.IsExpired).Select(v => v.Username).ToList();
        } catch {
            return vipList.Where(v => !v.IsExpired).Select(v => v.Username).ToList();
        }
    }

    public async Task<bool> IsVipAsync(string username) {
        try {
            await SyncWithRealVipList();
            return vipList.Any(v => v.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && !v.IsExpired);
        } catch (Exception ex) {
            WriteDebug($"❌ Ошибка проверки VIP: {ex.Message}\n", ConsoleColor.Red);
            return vipList.Any(v => v.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && !v.IsExpired);
        }
    }

    public bool IsVip(string username) {
        try {
            var task = Task.Run(async () => await IsVipAsync(username));
            task.Wait(TimeSpan.FromSeconds(5));
            return task.IsCompleted ? task.Result : vipList.Any(v => v.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && !v.IsExpired);
        } catch {
            return vipList.Any(v => v.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && !v.IsExpired);
        }
    }

    public async Task<(bool success, string stolenFrom)> StealVipAsync(string thiefName) {
        try {
            await SyncWithRealVipList();

            if (random.Next(100) >= settings.VipStealChance) {
                WriteColor($"❌ {thiefName} не смог украсть VIP (шанс {settings.VipStealChance}%)\n", ConsoleColor.Red);
                return (false, null);
            }

            var availableVictims = vipList
                .Where(v => !v.IsExpired && !v.Username.Equals(thiefName, StringComparison.OrdinalIgnoreCase))
                .Select(v => v.Username)
                .ToList();

            if (availableVictims.Count == 0) {
                WriteColor($"❌ Нет жертв для кражи\n", ConsoleColor.Red);
                return (false, null);
            }

            var stolenFrom = availableVictims[random.Next(availableVictims.Count)];

            var victimVip = vipList.FirstOrDefault(v => v.Username.Equals(stolenFrom, StringComparison.OrdinalIgnoreCase));
            if (victimVip != null) {
                victimVip.ExpiryDate = DateTime.Now.AddMinutes(-1);
            }

            var existingThiefVip = vipList.FirstOrDefault(v => v.Username.Equals(thiefName, StringComparison.OrdinalIgnoreCase));
            if (existingThiefVip != null) {
                existingThiefVip.GrantDate = DateTime.Now;
                existingThiefVip.ExpiryDate = DateTime.Now.AddDays(settings.VipDurationDays);
            } else {
                var thiefVip = new VipItem(thiefName, DateTime.Now, settings.VipDurationDays);
                vipList.Add(thiefVip);
            }

            SaveVipList();

            WriteColor($"✅ {thiefName} украл VIP у {stolenFrom}\n", ConsoleColor.Green);
            return (true, stolenFrom);

        } catch (Exception ex) {
            WriteColor($"❌ Ошибка кражи VIP: {ex.Message}\n", ConsoleColor.Red);
            return (false, null);
        }
    }

    public (bool success, string stolenFrom) StealVip(string thiefName) {
        try {
            var task = Task.Run(async () => await StealVipAsync(thiefName));
            task.Wait(TimeSpan.FromSeconds(5));
            return task.IsCompleted ? task.Result : (false, null);
        } catch {
            return (false, null);
        }
    }

    public string GetRandomSuccessfulStealMessage(string thiefName, string preyName) {
        if (successfulStealMessages.Count == 0)
            return $"{thiefName} украл VIP у {preyName}!";

        var message = successfulStealMessages[random.Next(successfulStealMessages.Count)];
        return message.Replace("$thiefName", thiefName).Replace("$preyName", preyName);
    }

    public string GetRandomFailedStealMessage(string thiefName) {
        if (failedStealMessages.Count == 0)
            return $"{thiefName} попытался украсть VIP и был наказан!";

        var message = failedStealMessages[random.Next(failedStealMessages.Count)];
        return message.Replace("$thiefName", thiefName);
    }

    private void LoadVipList() {
        if (File.Exists(vipListFile)) {
            try {
                string json = File.ReadAllText(vipListFile);
                vipList = JsonSerializer.Deserialize<List<VipItem>>(json) ?? new List<VipItem>();
                WriteColor($"✅ Загружено VIP записей: {vipList.Count}\n", ConsoleColor.Green);

                var invalidEntries = vipList.Where(v =>
                    v.Username.StartsWith("[{\"") ||
                    v.Username.Contains("\\u") ||
                    string.IsNullOrWhiteSpace(v.Username)).ToList();

                foreach (var invalid in invalidEntries) {
                    vipList.Remove(invalid);
                    WriteDebug($"🗑️ Удалена некорректная запись: {invalid.Username}\n", ConsoleColor.Yellow);
                }

                if (invalidEntries.Count > 0) {
                    SaveVipList();
                }
            } catch (Exception ex) {
                WriteColor($"❌ Ошибка загрузки VIP: {ex.Message}\n", ConsoleColor.Red);
                vipList = new List<VipItem>();
            }
        } else {
            WriteColor("ℹ️ Файл VIP не найден, создан новый\n", ConsoleColor.Yellow);
            vipList = new List<VipItem>();
        }
    }

    private void SaveVipList() {
        try {
            string json = JsonSerializer.Serialize(vipList, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(vipListFile, json);
        } catch (Exception ex) {
            WriteColor($"❌ Ошибка сохранения VIP: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    private void LoadStealMessages() {
        try {
            if (File.Exists("config/successful_steal_messages.txt")) {
                successfulStealMessages = File.ReadAllLines("config/successful_steal_messages.txt")
                    .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                    .ToList();
            } else {
                successfulStealMessages = new List<string>
                {
                    "$thiefName коварно украл VIP у $preyName!",
                    "VIP перешел от $preyName к $thiefName в результате дерзкой кражи!",
                    "$thiefName стащил VIP прямо из-под носа $preyName!",
                    "Невероятно! $thiefName украл VIP статус у $preyName!"
                };
                File.WriteAllLines("config/successful_steal_messages.txt", successfulStealMessages);
            }

            if (File.Exists("config/failed_steal_messages.txt")) {
                failedStealMessages = File.ReadAllLines("config/failed_steal_messages.txt")
                    .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                    .ToList();
            } else {
                failedStealMessages = new List<string>
                {
                    "$thiefName попытался украсть VIP, но был пойман!",
                    "Кража VIP $thiefName провалилась!",
                    "$thiefName не смог украсть VIP и будет наказан!",
                    "Провал! $thiefName был замечен при попытке кражи VIP!"
                };
                File.WriteAllLines("config/failed_steal_messages.txt", failedStealMessages);
            }
        } catch (Exception ex) {
            WriteColor($"❌ Ошибка загрузки фраз: {ex.Message}\n", ConsoleColor.Red);
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