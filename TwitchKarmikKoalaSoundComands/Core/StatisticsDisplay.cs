using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class StatisticsDisplay {
    private readonly TwitchBot _bot;
    private readonly VipManager _vipManager;
    private readonly CommandManager _commandManager;
    private readonly BotSettings _settings;

    // Статистика в реальном времени
    public int TotalSoundActivations { get; private set; }
    public int TotalPointsSpent { get; private set; }
    public string LastSoundActivator { get; private set; } = "нет";
    public string LastSoundCommand { get; private set; } = "нет";
    public DateTime LastSoundActivationTime { get; private set; }

    public int VipCount { get; private set; }
    public string LastVipPurchase { get; private set; } = "нет";
    public DateTime LastVipPurchaseTime { get; private set; }

    public int TotalStealAttempts { get; private set; }
    public string LastFailedStealer { get; private set; } = "нет";
    public string LastSuccessfulSteal { get; private set; } = "нет";
    public string LastStealVictim { get; private set; } = "нет";
    public DateTime LastStealTime { get; private set; }

    private CancellationTokenSource _cancellationTokenSource;
    private bool _isRunning = false;

    public StatisticsDisplay(TwitchBot bot, VipManager vipManager, CommandManager commandManager, BotSettings settings) {
        _bot = bot;
        _vipManager = vipManager;
        _commandManager = commandManager;
        _settings = settings;
    }

    public void Start() {
        if (_isRunning)
            return;

        _isRunning = true;
        _cancellationTokenSource = new CancellationTokenSource();

        Task.Run(async () => await UpdateLoop(_cancellationTokenSource.Token));

        WriteColor("✅ Модуль статистики запущен\n", ConsoleColor.Green);
    }

    public void Stop() {
        if (!_isRunning)
            return;

        _isRunning = false;
        _cancellationTokenSource?.Cancel();
        WriteColor("✅ Модуль статистики остановлен\n", ConsoleColor.Yellow);
    }

    private async Task UpdateLoop(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            try {
                UpdateStatistics();
                await Task.Delay(2000, cancellationToken); // Обновление каждые 2 секунды
            } catch (TaskCanceledException) {
                break;
            } catch (Exception ex) {
                if (_settings.DebugMode) {
                    WriteColor($"❌ Ошибка обновления статистики: {ex.Message}\n", ConsoleColor.Red);
                }
                await Task.Delay(5000, cancellationToken);
            }
        }
    }

    private void UpdateStatistics() {
        try {
            // Проверяем, что менеджеры инициализированы
            if (_vipManager == null) {
                if (_settings.DebugMode) {
                    WriteColor("⚠️ VipManager не инициализирован\n", ConsoleColor.Yellow);
                }
                return;
            }

            // Обновляем статистику VIP
            try {
                VipCount = _vipManager.GetVipUsers().Count;
            } catch (Exception ex) {
                if (_settings.DebugMode) {
                    WriteColor($"⚠️ Ошибка получения VIP статистики: {ex.Message}\n", ConsoleColor.Yellow);
                }
            }

            // Статистика будет обновляться через события
            // Основные данные берем из существующих менеджеров
        } catch (Exception ex) {
            if (_settings.DebugMode) {
                WriteColor($"❌ Ошибка получения статистики: {ex.Message}\n", ConsoleColor.Red);
            }
        }
    }

    // Методы для обновления статистики из других частей программы
    public void RecordSoundActivation(string username, string command, int cost) {
        try {
            TotalSoundActivations++;
            TotalPointsSpent += cost;
            LastSoundActivator = username;
            LastSoundCommand = GetCommandDisplayName(command);
            LastSoundActivationTime = DateTime.Now;
        } catch (Exception ex) {
            if (_settings.DebugMode) {
                WriteColor($"❌ Ошибка записи статистики звуков: {ex.Message}\n", ConsoleColor.Red);
            }
        }
    }

    public void RecordVipPurchase(string username) {
        try {
            if (_vipManager != null) {
                LastVipPurchase = username;
                LastVipPurchaseTime = DateTime.Now;
                VipCount = _vipManager.GetVipUsers().Count;
            }
        } catch (Exception ex) {
            if (_settings.DebugMode) {
                WriteColor($"❌ Ошибка записи статистики VIP: {ex.Message}\n", ConsoleColor.Red);
            }
        }
    }

    public void RecordStealAttempt(string thiefName, bool success, string victimName = null) {
        try {
            if (_vipManager != null) {
                TotalStealAttempts++;

                if (success) {
                    LastSuccessfulSteal = thiefName;
                    LastStealVictim = victimName;
                    LastStealTime = DateTime.Now;
                } else {
                    LastFailedStealer = thiefName;
                }

                VipCount = _vipManager.GetVipUsers().Count;
            }
        } catch (Exception ex) {
            if (_settings.DebugMode) {
                WriteColor($"❌ Ошибка записи статистики краж: {ex.Message}\n", ConsoleColor.Red);
            }
        }
    }

    public void DisplayStatistics(int left, int top) {
        try {
            // Сохраняем текущую позицию курсора
            int originalLeft = Console.CursorLeft;
            int originalTop = Console.CursorTop;

            // Устанавливаем позицию для вывода статистики
            Console.SetCursorPosition(left, top);

            // Очищаем только область статистики (примерно 10 строк)
            ClearArea(left, top, 80, 10);

            // Звуковые команды
            Console.Write("🔊 Звуковые команды: ");
            WriteColor($"{TotalSoundActivations} активаций", ConsoleColor.White);
            Console.Write(" (");
            WriteColor($"{TotalPointsSpent} баллов", ConsoleColor.Yellow);
            Console.WriteLine(")");
            Console.Write("   Последняя: ");
            WriteColor(LastSoundActivator, ConsoleColor.Green);

            // Добавляем название команды/награды
            if (LastSoundCommand != "нет") {
                Console.Write(" (");
                WriteColor(LastSoundCommand, ConsoleColor.Cyan);
                Console.Write(")");
            }

            // Выводим время только если была активация
            if (LastSoundActivationTime > DateTime.MinValue && LastSoundActivator != "нет") {
                Console.Write(" - ");
                WriteColor(LastSoundActivationTime.ToString("dd.MM HH:mm"), ConsoleColor.Gray);
            }
            Console.WriteLine();

            // VIP статистика
            Console.Write("⭐ VIP на канале: ");
            WriteColor($"{VipCount}/5", ConsoleColor.Magenta);
            Console.WriteLine();
            Console.Write("   Последняя покупка: ");
            WriteColor(LastVipPurchase, ConsoleColor.Green);

            // Выводим время только если была покупка
            if (LastVipPurchaseTime > DateTime.MinValue && LastVipPurchase != "нет") {
                Console.Write(" - ");
                WriteColor(LastVipPurchaseTime.ToString("dd.MM HH:mm"), ConsoleColor.Gray);
            }
            Console.WriteLine();

            // Кражи VIP
            Console.Write("🎭 Попыток кражи VIP: ");
            WriteColor($"{TotalStealAttempts}", ConsoleColor.White);
            Console.WriteLine();
            Console.Write("   Последний неудачник: ");
            WriteColor(LastFailedStealer, ConsoleColor.Red);
            Console.WriteLine();
            Console.Write("   Последняя кража: ");
            WriteColor(LastSuccessfulSteal, ConsoleColor.Green);
            if (!string.IsNullOrEmpty(LastStealVictim) && LastStealVictim != "нет") {
                Console.Write(" → ");
                WriteColor(LastStealVictim, ConsoleColor.Yellow);
            }

            // Выводим время только если была кража
            if (LastStealTime > DateTime.MinValue && LastSuccessfulSteal != "нет") {
                Console.Write(" - ");
                WriteColor(LastStealTime.ToString("dd.MM HH:mm"), ConsoleColor.Gray);
            }
            Console.WriteLine();

            // Восстанавливаем позицию курсора
            Console.SetCursorPosition(originalLeft, originalTop);
        } catch (Exception ex) {
            if (_settings.DebugMode) {
                WriteColor($"❌ Ошибка отображения статистики: {ex.Message}\n", ConsoleColor.Red);
            }
        }
    }

    // Метод для получения отображаемого названия команды/награды
    private string GetCommandDisplayName(string command) {
        try {
            if (_commandManager == null)
                return command;

            var soundCommand = _commandManager.GetCommand(command);
            if (soundCommand != null) {
                // Возвращаем название награды, если оно есть, иначе саму команду
                return !string.IsNullOrEmpty(soundCommand.RewardTitle) ?
                    soundCommand.RewardTitle : command;
            }

            // Для VIP команд возвращаем понятные названия
            if (command == "VIP_PURCHASE" || command.Contains("Купить VIP"))
                return "Купить VIP";
            if (command == "VIP_STEAL" || command.Contains("Украсть VIP"))
                return "Украсть VIP";

            return command;
        } catch {
            return command;
        }
    }

    private void ClearArea(int left, int top, int width, int height) {
        try {
            string clearLine = new string(' ', width);
            for (int i = 0; i < height; i++) {
                Console.SetCursorPosition(left, top + i);
                Console.Write(clearLine);
            }
            Console.SetCursorPosition(left, top);
        } catch (Exception ex) {
            // Игнорируем ошибки очистки
            if (_settings.DebugMode) {
                WriteColor($"⚠️ Ошибка очистки области: {ex.Message}\n", ConsoleColor.Yellow);
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