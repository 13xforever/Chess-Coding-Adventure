using Chess.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace CodingAdventureBot;

// Specification download: https://www.shredderchess.com/download.html
public class EngineUCI
{
	private Bot? engine = null;
	private static readonly bool logToFile = false;

	private static readonly string[] positionLabels = ["position", "fen", "moves"];
	private static readonly string[] goLabels = ["go", "movetime", "wtime", "btime", "winc", "binc", "movestogo"];
	private static readonly char[] whitespaces = [' ', '\t'];
	private readonly Dictionary<string, string> options;

	public EngineUCI()
	{
		options = new(StringComparer.OrdinalIgnoreCase)
		{
			[Options.HashSize] = Searcher.DefaultTranspositionTableSizeMB.ToString(),
			[Options.Ponder] = "false",
		};
	}

	public void ReceiveCommand(string message)
	{
		if (string.IsNullOrWhiteSpace(message))
			return;

		LogToFile("Command received: " + message);
		Span<string> messageParts = message.Trim().Split(whitespaces, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (messageParts.Length is 0)
			return;
parse:
		switch (messageParts[0].ToLower())
		{
			case "uci":
				Respond("id name Coding Adventure 2.2");
				Respond("id author Sebastian Lague");
				Respond("option name UCI_EngineAbout type string default Chess Coding Adventure Bot by Sebastian Lague. See https://github.com/SebLague/Chess-Coding-Adventure for details.");
				Respond($"option name {Options.HashSize} type spin default {options[Options.HashSize]} min 1 max {Searcher.MaxTranspositionTableSizeMB}");
				Respond($"option name {Options.Ponder} type check default {options[Options.Ponder]}");
				Respond("uciok");
				break;
			case "isready":
				CreateEngineIfNeeded();
				Respond("readyok");
				break;
			case "ucinewgame":
				CreateEngineIfNeeded();
				engine!.NotifyNewGame();
				break;
			case "position":
				CreateEngineIfNeeded();
				ProcessPositionCommand(message);
				break;
			case "go":
				CreateEngineIfNeeded();
				ProcessGoCommand(message);
				break;
			case "ponderhit":
				if (engine is { IsThinking: true })
					engine.StopThinking(true);
				engine!.PonderHit();
				break;
			case "stop":
				if (engine is {IsThinking: true})
				{
					engine.StopThinking(false);
				}
				break;
			case "quit":
				engine?.Quit();
				break;
			case "d":
				if (engine is null)
					Respond("info string Engine is not created yet");
				else
				{
					//Console.WriteLine(engine.GetBoardDiagram());
					Respond($"info string Pondering={engine.IsPondering}, thinking={engine.IsThinking}");
				}
				break;
			case "setoption":
				var (name, value) = GetOptionValue(messageParts[1..]);
				if (options.ContainsKey(name))
				{
					options[name] = value;
					LogToFile($"Set {name} = {value}");
				}
				else
					LogToFile($"Unknown option {name}");
				break;
			default:
				if (messageParts.Length is 1)
					LogToFile($"Unrecognized command: {message}");
				else
				{
					messageParts = messageParts[1..];
					goto parse;
				}
				break;
		}
	}

	private void CreateEngineIfNeeded()
	{
		if (engine is not null)
			return;

		if (!options.TryGetValue(Options.HashSize, out var hashSizeStr)
		    || !int.TryParse(hashSizeStr, out var hashSize)
		    || hashSize < 1 || hashSize > Searcher.MaxTranspositionTableSizeMB)
			hashSize = Searcher.DefaultTranspositionTableSizeMB;
		if (!options.TryGetValue(Options.Ponder, out var ponderStr)
		    || !bool.TryParse(ponderStr, out var canPonder))
			canPonder = false;
		engine = new(hashSize, canPonder);
		engine.OnMoveChosen += OnMoveChosen;
		engine.OnInfo += s => Respond("info " + s);
	}

	private static (string name, string value) GetOptionValue(ReadOnlySpan<string> messageParts)
	{
		string name = "", value = "";
		var state = 0; // 0 = unknown, 1 = name, 2 = value
		
		while (messageParts is [{} part, ..])
		{
			switch (state, part)
			{
				case (_,"name"):
					state = 1;
					break;
				case (_, "value"):
					state = 2;
					break;
				case (1, _):
					name += part + " ";
					break;
				case (2, _):
					value += part + " ";
					break;
			}
			messageParts = messageParts[1..];
		}
		return (name.TrimEnd(), value.TrimEnd());
	}
	
	private void OnMoveChosen(string move, string? ponderMove)
	{
		if (engine!.IsPonderHit)
			return;
		
		LogToFile($"{nameof(OnMoveChosen)}: book move = {engine.LatestMoveIsBookMove}");
		var bestmove = "bestmove " + move;
		if (ponderMove is { Length: > 0 })
			bestmove += $" ponder {ponderMove}";
		Respond(bestmove);
	}

	private void ProcessGoCommand(string message)
	{
		var isPonder = message.Contains("ponder");
		var thinkTime = -1;
		if (message.Contains("infinite"))
		{
			engine!.ThinkTimed(-1);
		}
		else if (message.Contains("movetime"))
		{
			var moveTimeMs = TryGetLabelledValueInt(message, "movetime", goLabels, 0);
			engine!.ThinkTimed(moveTimeMs);
		}
		else
		{
			var timeRemainingWhiteMs = TryGetLabelledValueInt(message, "wtime", goLabels, 0);
			var timeRemainingBlackMs = TryGetLabelledValueInt(message, "btime", goLabels, 0);
			var incrementWhiteMs = TryGetLabelledValueInt(message, "winc", goLabels, 0);
			var incrementBlackMs = TryGetLabelledValueInt(message, "binc", goLabels, 0);

			thinkTime = engine!.ChooseThinkTime(timeRemainingWhiteMs, timeRemainingBlackMs, incrementWhiteMs, incrementBlackMs);
		}
		LogToFile("Thinking for: " + thinkTime + " ms.");
		if (isPonder)
			engine.Ponder(thinkTime);
		else
			engine.ThinkTimed(thinkTime);
	}

	// Format: 'position startpos moves e2e4 e7e5'
	// Or: 'position fen rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1 moves e2e4 e7e5'
	// Note: 'moves' section is optional; last move is the suggested ponder move for 'go ponder' command
	private void ProcessPositionCommand(string message)
	{
		// FEN
		if (message.Contains("startpos", StringComparison.CurrentCultureIgnoreCase))
		{
			engine!.SetPosition(FenUtility.StartPositionFEN);
		}
		else if (message.Contains("fen", StringComparison.CurrentCultureIgnoreCase)) {
			var customFen = TryGetLabelledValue(message, "fen", positionLabels);
			engine!.SetPosition(customFen);
		}
		else
		{
			Console.WriteLine("Invalid position command (expected 'startpos' or 'fen')");
		}

		// Moves
		var allMoves = TryGetLabelledValue(message, "moves", positionLabels);
		if (string.IsNullOrEmpty(allMoves))
			return;
		
		var moveList = allMoves.Split(' ');
		foreach (var move in moveList)
		{
			engine!.MakeMove(move);
		}

		LogToFile($"Make moves after setting position: {moveList.Length}");
	}

	internal static void Respond(string reponse)
	{
		Console.WriteLine(reponse);
		LogToFile("Response sent: " + reponse);
	}

	private static int TryGetLabelledValueInt(string text, string label, string[] allLabels, int defaultValue = 0)
	{
		var valueString = TryGetLabelledValue(text, label, allLabels, defaultValue + "");
		if (int.TryParse(valueString.Split(' ')[0], out var result))
		{
			return result;
		}
		return defaultValue;
	}

	private static string TryGetLabelledValue(string text, string label, string[] allLabels, string defaultValue = "")
	{
		text = text.Trim();
		if (text.Contains(label))
		{
			var valueStart = text.IndexOf(label) + label.Length;
			var valueEnd = text.Length;
			foreach (var otherID in allLabels)
			{
				if (otherID != label && text.Contains(otherID))
				{
					var otherIDStartIndex = text.IndexOf(otherID);
					if (otherIDStartIndex > valueStart && otherIDStartIndex < valueEnd)
					{
						valueEnd = otherIDStartIndex;
					}
				}
			}

			return text.Substring(valueStart, valueEnd - valueStart).Trim();
		}
		return defaultValue;
	}

	private static void LogToFile(string text)
	{
		if (!logToFile)
			return;
		
		Directory.CreateDirectory(AppDataPath);
		var path = Path.Combine(AppDataPath, "UCI_Log.txt");
		using var writer = new StreamWriter(path, true);
		writer.WriteLine(text);
	}

	public static string AppDataPath
	{
		get
		{
			var dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			return Path.Combine(dir, "Chess-Coding-Adventure");
		}
	}
}
