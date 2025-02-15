﻿using System.Text;

namespace Chess.Core;

public static class PGNCreator
{

	public static string CreatePGN(Move[] moves)
	{
		return CreatePGN(moves, GameResult.InProgress, FenUtility.StartPositionFEN);
	}

	public static string CreatePGN(Board board, GameResult result, string whiteName = "", string blackName = "")
	{
		return CreatePGN(board.AllGameMoves.ToArray(), result, board.GameStartFEN, whiteName, blackName);
	}

	public static string CreatePGN(Move[] moves, GameResult result, string startFen, string whiteName = "", string blackName = "")
	{
		startFen = startFen.Replace("\n", "").Replace("\r", "");

		StringBuilder pgn = new();
		var board = new Board();
		board.LoadPosition(startFen);
		// Headers
		if (!string.IsNullOrEmpty(whiteName))
		{
			pgn.AppendLine($"[White \"{whiteName}\"]");
		}
		if (!string.IsNullOrEmpty(blackName))
		{
			pgn.AppendLine($"[Black \"{blackName}\"]");
		}

		if (startFen != FenUtility.StartPositionFEN)
		{
			pgn.AppendLine($"[FEN \"{startFen}\"]");
		}
		if (result is not GameResult.NotStarted or GameResult.InProgress)
		{
			pgn.AppendLine($"[Result \"{result}\"]");
		}

		for (var plyCount = 0; plyCount < moves.Length; plyCount++)
		{
			var moveString = MoveUtility.GetMoveNameSAN(moves[plyCount], board);
			board.MakeMove(moves[plyCount]);

			if (plyCount % 2 == 0)
			{
				pgn.Append((plyCount / 2 + 1) + ". ");
			}
			pgn.Append(moveString + " ");
		}

		return pgn.ToString();
	}

}