namespace Chess.Core;

using System.Linq;

public static class Arbiter
{
	public static bool IsDrawResult(GameResult result)
	{
		return result is GameResult.DrawByArbiter or GameResult.FiftyMoveRule or
			GameResult.Repetition or GameResult.Stalemate or GameResult.InsufficientMaterial;
	}

	public static bool IsWinResult(GameResult result)
	{
		return IsWhiteWinsResult(result) || IsBlackWinsResult(result);
	}

	public static bool IsWhiteWinsResult(GameResult result)
	{
		return result is GameResult.BlackIsMated or GameResult.BlackTimeout or GameResult.BlackIllegalMove;
	}

	public static bool IsBlackWinsResult(GameResult result)
	{
		return result is GameResult.WhiteIsMated or GameResult.WhiteTimeout or GameResult.WhiteIllegalMove;
	}


	public static GameResult GetGameState(Board board)
	{
		var moveGenerator = new MoveGenerator();
		var moves = moveGenerator.GenerateMoves(board);

		// Look for mate/stalemate
		if (moves.Length == 0)
		{
			if (moveGenerator.InCheck())
			{
				return (board.IsWhiteToMove) ? GameResult.WhiteIsMated : GameResult.BlackIsMated;
			}
			return GameResult.Stalemate;
		}

		// Fifty move rule
		if (board.FiftyMoveCounter >= 100)
		{
			return GameResult.FiftyMoveRule;
		}

		// Threefold repetition
		var repCount = board.RepetitionPositionHistory.Count((x => x == board.ZobristKey));
		if (repCount == 3)
		{
			return GameResult.Repetition;
		}

		// Look for insufficient material
		if (InsufficentMaterial(board))
		{
			return GameResult.InsufficientMaterial;
		}
		return GameResult.InProgress;
	}

	// Test for insufficient material (Note: not all cases are implemented)
	public static bool InsufficentMaterial(Board board)
	{
		// Can't have insufficient material with pawns on the board
		if (board.Pawns[Board.WhiteIndex].Count > 0 || board.Pawns[Board.BlackIndex].Count > 0)
		{
			return false;
		}

		// Can't have insufficient material with queens/rooks on the board
		if (board.FriendlyOrthogonalSliders != 0 || board.EnemyOrthogonalSliders != 0)
		{
			return false;
		}

		// If no pawns, queens, or rooks on the board, then consider knight and bishop cases
		var numWhiteBishops = board.Bishops[Board.WhiteIndex].Count;
		var numBlackBishops = board.Bishops[Board.BlackIndex].Count;
		var numWhiteKnights = board.Knights[Board.WhiteIndex].Count;
		var numBlackKnights = board.Knights[Board.BlackIndex].Count;
		var numWhiteMinors = numWhiteBishops + numWhiteKnights;
		var numBlackMinors = numBlackBishops + numBlackKnights;
		var numMinors = numWhiteMinors + numBlackMinors;

		// Lone kings or King vs King + single minor: is insuffient
		if (numMinors <= 1)
		{
			return true;
		}

		// Bishop vs bishop: is insufficient when bishops are same colour complex
		if (numMinors == 2 && numWhiteBishops == 1 && numBlackBishops == 1)
		{
			var whiteBishopIsLightSquare = BoardHelper.LightSquare(board.Bishops[Board.WhiteIndex][0]);
			var blackBishopIsLightSquare = BoardHelper.LightSquare(board.Bishops[Board.BlackIndex][0]);
			return whiteBishopIsLightSquare == blackBishopIsLightSquare;
		}

		return false;


	}
}