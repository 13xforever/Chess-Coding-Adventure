using System;

namespace CodingAdventureBot;

public static class Program
{
    public static void Main(string[] args)
    {
        EngineUCI engine = new();
        var command = "";
        while (command != "quit")
        {
            command = Console.ReadLine();
            if (!string.IsNullOrEmpty(command))
                command = engine.ReceiveCommand(command);
        }
    }
}