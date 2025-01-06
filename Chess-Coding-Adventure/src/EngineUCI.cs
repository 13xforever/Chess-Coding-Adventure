﻿using Chess.Core;
using System;
using System.IO;

namespace CodingAdventureBot;

// Specification download: https://www.shredderchess.com/download.html
public class EngineUCI
{
	private readonly Bot engine;
	private static readonly bool logToFile = false;

	private static readonly string[] positionLabels = ["position", "fen", "moves"];
	private static readonly string[] goLabels = ["go", "movetime", "wtime", "btime", "winc", "binc", "movestogo"];

	public EngineUCI()
	{
		engine = new();
		engine.OnMoveChosen += OnMoveChosen;
		engine.OnInfo += s => Respond("info " + s);
	}

	public void ReceiveCommand(string message)
	{
		//Console.WriteLine(message);
		LogToFile("Command received: " + message);
		message = message.Trim();
		var messageType = message.Split(' ')[0].ToLower();
		switch (messageType)
		{
			case "uci":
				Respond("id name Coding Adventure 2.0");
				Respond("id author Sebastian Lague");
				Respond("option name UCI_EngineAbout type string default Chess Coding Adventure Bot by Sebastian Lague. See https://github.com/SebLague/Chess-Coding-Adventure for details.");
				Respond("uciok");
				break;
			case "isready":
				Respond("readyok");
				break;
			case "ucinewgame":
				engine.NotifyNewGame();
				break;
			case "position":
				ProcessPositionCommand(message);
				break;
			case "go":
				ProcessGoCommand(message);
				break;
			case "stop":
				if (engine.IsThinking)
				{
					engine.StopThinking();
				}
				break;
			case "quit":
				engine.Quit();
				break;
			case "d":
				Console.WriteLine(engine.GetBoardDiagram());
				break;
			default:
				LogToFile($"Unrecognized command: {messageType}");
				break;
		}
	}

	private void OnMoveChosen(string move)
	{
		LogToFile("OnMoveChosen: book move = " + engine.LatestMoveIsBookMove);
		Respond("bestmove " + move);
	}

	private void ProcessGoCommand(string message)
	{
		if (message.Contains("movetime"))
		{
			var moveTimeMs = TryGetLabelledValueInt(message, "movetime", goLabels, 0);
			engine.ThinkTimed(moveTimeMs);
		}
		else
		{
			var timeRemainingWhiteMs = TryGetLabelledValueInt(message, "wtime", goLabels, 0);
			var timeRemainingBlackMs = TryGetLabelledValueInt(message, "btime", goLabels, 0);
			var incrementWhiteMs = TryGetLabelledValueInt(message, "winc", goLabels, 0);
			var incrementBlackMs = TryGetLabelledValueInt(message, "binc", goLabels, 0);

			var thinkTime = engine.ChooseThinkTime(timeRemainingWhiteMs, timeRemainingBlackMs, incrementWhiteMs, incrementBlackMs);
			LogToFile("Thinking for: " + thinkTime + " ms.");
			engine.ThinkTimed(thinkTime);
		}

	}

	// Format: 'position startpos moves e2e4 e7e5'
	// Or: 'position fen rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1 moves e2e4 e7e5'
	// Note: 'moves' section is optional
	private void ProcessPositionCommand(string message)
	{
		// FEN
		if (message.ToLower().Contains("startpos"))
		{
			engine.SetPosition(FenUtility.StartPositionFEN);
		}
		else if (message.ToLower().Contains("fen")) {
			var customFen = TryGetLabelledValue(message, "fen", positionLabels);
			engine.SetPosition(customFen);
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
			engine.MakeMove(move);
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
