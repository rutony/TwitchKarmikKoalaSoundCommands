using System;
using System.Collections.Generic;
using System.Linq;

public class StatisticsService {
    private CommandManager commandManager;

    public StatisticsService(CommandManager commandManager) {
        this.commandManager = commandManager;
    }

    public void ShowStatistics() {
        Console.Clear();
        WriteColor("=== СТАТИСТИКА КОМАНД ===\n", ConsoleColor.Cyan);
        Console.WriteLine();

        var commands = commandManager.GetAllCommands();
        var usage = commandManager.GetCommandUsage();

        WriteColor($"Всего команд: {commands.Count}\n", ConsoleColor.White);
        WriteColor($"Для чата: {commandManager.ChatEnabledCount}\n", ConsoleColor.Green);
        WriteColor($"Для наград: {commandManager.RewardEnabledCount}\n", ConsoleColor.Yellow);
        WriteColor($"Всего использований: {commandManager.TotalUsage}\n", ConsoleColor.Cyan);
        Console.WriteLine();

        if (usage.Count == 0) {
            WriteColor("Команды еще не использовались\n", ConsoleColor.Yellow);
        } else {
            WriteColor("Топ команд по использованию:\n", ConsoleColor.White);
            foreach (var cmd in usage.OrderByDescending(x => x.Value).Take(10)) {
                var command = commands[cmd.Key];
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

    private void WriteColor(string text, ConsoleColor color) {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = originalColor;
    }
}