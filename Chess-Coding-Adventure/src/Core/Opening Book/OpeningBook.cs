using System;
using System.Collections.Generic;

namespace Chess.Core;

public class OpeningBook
{
	private readonly Dictionary<string, BookMove[]> movesByPosition;
	private readonly Random rng;

	public OpeningBook(string file)
	{
		rng = new();
		Span<string> entries = file.Trim(new char[] { ' ', '\n' }).Split("pos").AsSpan(1);
		movesByPosition = new(entries.Length);

		for (var i = 0; i < entries.Length; i++)
		{
			string[] entryData = entries[i].Trim('\n').Split('\n');
			var positionFen = entryData[0].Trim();
			Span<string> allMoveData = entryData.AsSpan(1);

			var bookMoves = new BookMove[allMoveData.Length];

			for (var moveIndex = 0; moveIndex < bookMoves.Length; moveIndex++)
			{
				string[] moveData = allMoveData[moveIndex].Split(' ');
				bookMoves[moveIndex] = new(moveData[0], int.Parse(moveData[1]));
			}

			movesByPosition.Add(positionFen, bookMoves);
		}
	}

	public bool HasBookMove(string positionFen)
	{
		return movesByPosition.ContainsKey(RemoveMoveCountersFromFEN(positionFen));
	}

	// WeightPow is a value between 0 and 1.
	// 0 means all moves are picked with equal probablity, 1 means moves are weighted by num times played.
	public bool TryGetBookMove(Board board, out string moveString, double weightPow = 0.5)
	{
		var positionFen = FenUtility.CurrentFen(board, alwaysIncludeEPSquare: false);
		weightPow = Math.Clamp(weightPow, 0, 1);
		if (movesByPosition.TryGetValue(RemoveMoveCountersFromFEN(positionFen), out var moves))
		{
			var totalPlayCount = 0;
			foreach (var move in moves)
			{
				totalPlayCount += WeightedPlayCount(move.numTimesPlayed);
			}

			var weights = new double[moves.Length];
			double weightSum = 0;
			for (var i = 0; i < moves.Length; i++)
			{
				var weight = WeightedPlayCount(moves[i].numTimesPlayed) / (double)totalPlayCount;
				weightSum += weight;
				weights[i] = weight;
			}

			var probCumul = new double[moves.Length];
			for (var i = 0; i < weights.Length; i++)
			{
				var prob = weights[i] / weightSum;
				probCumul[i] = probCumul[Math.Max(0, i - 1)] + prob;
				//string debugString = $"{moves[i].moveString}: {prob * 100:0.00}% (cumul = {probCumul[i]})";
				//UnityEngine.Debug.Log(debugString);
			}


			var random = rng.NextDouble();
			for (var i = 0; i < moves.Length; i++)
			{

				if (random <= probCumul[i])
				{
					moveString = moves[i].moveString;
					return true;
				}
			}
		}

		moveString = "Null";
		return false;

		int WeightedPlayCount(int playCount) => (int)Math.Ceiling(Math.Pow(playCount, weightPow));
	}

	private string RemoveMoveCountersFromFEN(string fen)
	{
		var fenA = fen[..fen.LastIndexOf(' ')];
		return fenA[..fenA.LastIndexOf(' ')];
	}


	public readonly struct BookMove
	{
		public readonly string moveString;
		public readonly int numTimesPlayed;

		public BookMove(string moveString, int numTimesPlayed)
		{
			this.moveString = moveString;
			this.numTimesPlayed = numTimesPlayed;
		}
	}
}