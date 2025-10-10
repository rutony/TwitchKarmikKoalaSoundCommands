using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.ChannelPoints;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomReward;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class RewardManager {
    private TwitchAPI api;
    private string channelId;
    private BotSettings settings;
    private List<string> createdRewardIds = new List<string>();
    private Dictionary<string, string> rewardTitleToIdMap = new Dictionary<string, string>();

    public string LastError { get; private set; } = "";

    public RewardManager(TwitchAPI api, string channelId, BotSettings settings) {
        this.api = api;
        this.channelId = channelId;
        this.settings = settings;
    }

    public async Task<Dictionary<string, string>> CreateCustomRewards(Dictionary<string, SoundCommand> soundCommands) {
        try {
            WriteDebug($"=== СОЗДАНИЕ НАГРАД ===\n", ConsoleColor.Cyan);
            rewardTitleToIdMap.Clear();
            createdRewardIds.Clear();

            var existingRewardsResponse = await api.Helix.ChannelPoints.GetCustomRewardAsync(channelId, new List<string>(), true);

            if (existingRewardsResponse == null || existingRewardsResponse.Data == null) {
                LastError = "Не удалось получить список существующих наград";
                return rewardTitleToIdMap;
            }

            var existingRewards = existingRewardsResponse.Data;
            WriteDebug($"✅ Найдено существующих наград: {existingRewards.Length}\n", ConsoleColor.Green);

            int createdCount = 0;
            int updatedCount = 0;
            int skippedCount = 0;

            var rewardCommands = soundCommands.Values.Where(c => c.RewardEnabled).ToList();
            WriteColor($"Обрабатываем {rewardCommands.Count} команд с наградами...\n", ConsoleColor.White);

            foreach (var soundCommand in rewardCommands) {
                var rewardTitle = soundCommand.RewardTitle;
                WriteDebug($"Обрабатываем: '{rewardTitle}'\n", ConsoleColor.Cyan);

                var existingReward = existingRewards.FirstOrDefault(r =>
                    r.Title.ToLower() == rewardTitle.ToLower());

                if (existingReward != null) {
                    bool needsUpdate = false;
                    string updateReason = "";

                    if (existingReward.Cost != soundCommand.Cost) {
                        needsUpdate = true;
                        updateReason += $"стоимость ({existingReward.Cost} -> {soundCommand.Cost}) ";
                    }

                    var expectedCooldown = ConvertCooldownToMinutes(soundCommand.Cooldown);
                    var currentCooldown = existingReward.GlobalCooldownSetting?.GlobalCooldownSeconds ?? 0;
                    var isCooldownEnabled = existingReward.GlobalCooldownSetting?.IsEnabled ?? false;

                    if (currentCooldown != expectedCooldown || !isCooldownEnabled) {
                        needsUpdate = true;
                        updateReason += $"cooldown ({currentCooldown} -> {expectedCooldown}) ";
                    }

                    if (existingReward.IsEnabled != true) {
                        needsUpdate = true;
                        updateReason += "включение ";
                    }

                    if (needsUpdate) {
                        WriteDebug($"  ✅ Награда существует, обновляю... ({updateReason})\n", ConsoleColor.Green);

                        try {
                            var updateRequest = new UpdateCustomRewardRequest {
                                Cost = soundCommand.Cost,
                                IsEnabled = true,
                                GlobalCooldownSeconds = expectedCooldown,
                                IsGlobalCooldownEnabled = true
                            };

                            var updatedReward = await api.Helix.ChannelPoints.UpdateCustomRewardAsync(
                                channelId, existingReward.Id, updateRequest);

                            if (updatedReward != null) {
                                createdRewardIds.Add(existingReward.Id);
                                rewardTitleToIdMap[rewardTitle] = existingReward.Id;
                                updatedCount++;
                                WriteColor($"  ✅ Награда '{rewardTitle}' обновлена\n", ConsoleColor.Green);
                            }
                        } catch (Exception ex) {
                            WriteColor($"  ❌ Ошибка обновления награды '{rewardTitle}': {ex.Message}\n", ConsoleColor.Red);
                        }
                    } else {
                        createdRewardIds.Add(existingReward.Id);
                        rewardTitleToIdMap[rewardTitle] = existingReward.Id;
                        skippedCount++;
                        WriteDebug($"  ✅ Награда '{rewardTitle}' уже актуальна\n", ConsoleColor.Green);
                    }
                } else {
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
                            rewardTitleToIdMap[rewardTitle] = result.Data[0].Id;
                            createdCount++;
                            WriteColor($"  ✅ Награда '{rewardTitle}' создана\n", ConsoleColor.Green);
                        }
                    } catch (Exception ex) {
                        WriteColor($"  ❌ Ошибка создания награды '{rewardTitle}': {ex.Message}\n", ConsoleColor.Red);
                    }
                }

                await Task.Delay(500);
            }

            WriteDebug($"\n=== РЕЗУЛЬТАТ ===\n", ConsoleColor.Cyan);
            WriteDebug($"Создано новых: {createdCount}\n", ConsoleColor.Green);
            WriteDebug($"Обновлено: {updatedCount}\n", ConsoleColor.Yellow);
            WriteDebug($"Пропущено (актуальных): {skippedCount}\n", ConsoleColor.Blue);
            WriteDebug($"Всего обработано: {rewardCommands.Count}\n", ConsoleColor.White);

            return rewardTitleToIdMap;
        } catch (Exception ex) {
            LastError = $"Критическая ошибка: {ex.Message}";
            WriteDebug($"❌ Ошибка в CreateCustomRewards: {ex.Message}\n", ConsoleColor.Red);
            return new Dictionary<string, string>();
        }
    }

    public string GetRewardIdByTitle(string rewardTitle) {
        return rewardTitleToIdMap.ContainsKey(rewardTitle) ? rewardTitleToIdMap[rewardTitle] : null;
    }

    public async Task DisableCustomRewards() {
        if (createdRewardIds.Count == 0)
            return;

        try {
            var currentRewards = await api.Helix.ChannelPoints.GetCustomRewardAsync(channelId, createdRewardIds);

            foreach (var reward in currentRewards.Data) {
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
            WriteDebug($"Ошибка при отключении наград: {ex.Message}\n", ConsoleColor.Red);
        }
    }

    private int ConvertCooldownToMinutes(int cooldownSeconds) {
        int minutes = cooldownSeconds / 60;
        return Math.Clamp(minutes, 1, 180) * 60;
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