namespace Chess.Core;

public class MoveOrdering
{

	int[] moveScores;
	const int maxMoveCount = 218;

	const int squareControlledByOpponentPawnPenalty = 350;
	const int capturedPieceValueMultiplier = 100;

	TranspositionTable transpositionTable;
	Move invalidMove;

	public Killers[] killerMoves;
	public int[,,] History;
	public const int maxKillerMovePly = 32;

	const int million = 1000000;
	const int hashMoveScore = 100 * million;
	const int winningCaptureBias = 8 * million;
	const int promoteBias = 6 * million;
	const int killerBias = 4 * million;
	const int losingCaptureBias = 2 * million;
	const int regularBias = 0;

	public MoveOrdering(MoveGenerator m, TranspositionTable tt)
	{
		moveScores = new int[maxMoveCount];
		transpositionTable = tt;
		invalidMove = Move.NullMove;
		killerMoves = new Killers[maxKillerMovePly];
		ClearHistory();
	}

	public void ClearHistory()
	{
		History = new int[2, 64, 64];
	}

	public void ClearKillers()
	{
		killerMoves = new Killers[maxKillerMovePly];
	}

	public void Clear()
	{
		ClearKillers();
		ClearHistory();
	}



	public void OrderMoves(Move hashMove, Board board, System.Span<Move> moves, ulong oppAttacks, ulong oppPawnAttacks, bool inQSearch, int ply)
	{
		//Move hashMove = inQSearch ? invalidMove : transpositionTable.GetStoredMove();


		var oppPieces = board.EnemyDiagonalSliders | board.EnemyOrthogonalSliders | board.PieceBitboards[Piece.MakePiece(Piece.Knight, board.OpponentColour)];
		var pawnAttacks = board.IsWhiteToMove ? BitBoardUtility.WhitePawnAttacks : BitBoardUtility.BlackPawnAttacks;
		//bool danger = board.queens[1 - board.MoveColourIndex].Count > 0 || board.rooks[1 - board.MoveColourIndex].Count > 1;

		for (var i = 0; i < moves.Length; i++)
		{

			var move = moves[i];

			if (Move.SameMove(move, hashMove))
			{
				moveScores[i] = hashMoveScore;
				continue;
			}
			var score = 0;
			var startSquare = move.StartSquare;
			var targetSquare = move.TargetSquare;

			var movePiece = board.Square[startSquare];
			var movePieceType = Piece.PieceType(movePiece);
			var capturePieceType = Piece.PieceType(board.Square[targetSquare]);
			var isCapture = capturePieceType != Piece.None;
			var flag = moves[i].MoveFlag;
			var pieceValue = GetPieceValue(movePieceType);

			if (isCapture)
			{
				// Order moves to try capturing the most valuable opponent piece with least valuable of own pieces first
				var captureMaterialDelta = GetPieceValue(capturePieceType) - pieceValue;
				var opponentCanRecapture = BitBoardUtility.ContainsSquare(oppPawnAttacks | oppAttacks, targetSquare);
				if (opponentCanRecapture)
				{
					score += (captureMaterialDelta >= 0 ? winningCaptureBias : losingCaptureBias) + captureMaterialDelta;
				}
				else
				{
					score += winningCaptureBias + captureMaterialDelta;
				}
			}

			if (movePieceType == Piece.Pawn)
			{
				if (flag == Move.PromoteToQueenFlag && !isCapture)
				{
					score += promoteBias;
				}
			}
			else if (movePieceType == Piece.King)
			{
			}
			else
			{
				var toScore = PieceSquareTable.Read(movePiece, targetSquare);
				var fromScore = PieceSquareTable.Read(movePiece, startSquare);
				score += toScore - fromScore;

				if (BitBoardUtility.ContainsSquare(oppPawnAttacks, targetSquare))
				{
					score -= 50;
				}
				else if (BitBoardUtility.ContainsSquare(oppAttacks, targetSquare))
				{
					score -= 25;
				}

			}

			if (!isCapture)
			{
				//score += regularBias;
				var isKiller = !inQSearch && ply < maxKillerMovePly && killerMoves[ply].Match(move);
				score += isKiller ? killerBias : regularBias;
				score += History[board.MoveColourIndex, move.StartSquare, move.TargetSquare];
			}

			moveScores[i] = score;
		}

		//Sort(moves, moveScores);
		Quicksort(moves, moveScores, 0, moves.Length - 1);
	}

	static int GetPieceValue(int pieceType)
	{
		switch (pieceType)
		{
			case Piece.Queen:
				return Evaluation.QueenValue;
			case Piece.Rook:
				return Evaluation.RookValue;
			case Piece.Knight:
				return Evaluation.KnightValue;
			case Piece.Bishop:
				return Evaluation.BishopValue;
			case Piece.Pawn:
				return Evaluation.PawnValue;
			default:
				return 0;
		}
	}

	public string GetScore(int index)
	{
		var score = moveScores[index];

		int[] scoreTypes = [hashMoveScore, winningCaptureBias, losingCaptureBias, promoteBias, killerBias, regularBias];
		string[] typeNames = ["Hash Move", "Good Capture", "Bad Capture", "Promote", "Killer Move", "Regular"];
		var typeName = "";
		var closest = int.MaxValue;

		for (var i = 0; i < scoreTypes.Length; i++)
		{
			var delta = System.Math.Abs(score - scoreTypes[i]);
			if (delta < closest)
			{
				closest = delta;
				typeName = typeNames[i];
			}
		}

		return $"{score} ({typeName})";
	}

	public static void Sort(System.Span<Move> moves, int[] scores)
	{
		// Sort the moves list based on scores
		for (var i = 0; i < moves.Length - 1; i++)
		{
			for (var j = i + 1; j > 0; j--)
			{
				var swapIndex = j - 1;
				if (scores[swapIndex] < scores[j])
				{
					(moves[j], moves[swapIndex]) = (moves[swapIndex], moves[j]);
					(scores[j], scores[swapIndex]) = (scores[swapIndex], scores[j]);
				}
			}
		}
	}

	public static void Quicksort(System.Span<Move> values, int[] scores, int low, int high)
	{
		if (low < high)
		{
			var pivotIndex = Partition(values, scores, low, high);
			Quicksort(values, scores, low, pivotIndex - 1);
			Quicksort(values, scores, pivotIndex + 1, high);
		}
	}

	static int Partition(System.Span<Move> values, int[] scores, int low, int high)
	{
		var pivotScore = scores[high];
		var i = low - 1;

		for (var j = low; j <= high - 1; j++)
		{
			if (scores[j] > pivotScore)
			{
				i++;
				(values[i], values[j]) = (values[j], values[i]);
				(scores[i], scores[j]) = (scores[j], scores[i]);
			}
		}
		(values[i + 1], values[high]) = (values[high], values[i + 1]);
		(scores[i + 1], scores[high]) = (scores[high], scores[i + 1]);

		return i + 1;
	}
}


public struct Killers
{
	public Move moveA;
	public Move moveB;

	public void Add(Move move)
	{
		if (move.Value != moveA.Value)
		{
			moveB = moveA;
			moveA = move;
		}
	}

	public bool Match(Move move) => move.Value == moveA.Value || move.Value == moveB.Value;

}