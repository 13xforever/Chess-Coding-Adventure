using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using CodingAdventureBot;
using static System.Math;

namespace Chess.Core;

public class Searcher
{
	// Constants
	private const int transpositionTableSizeMB = 256;
	private const int maxExtentions = 16;

	private const int immediateMateScore = 100000;
	private const int positiveInfinity = 9999999;
	private const int negativeInfinity = -positiveInfinity;

	public event Action<Move>? OnSearchComplete;
	public event Action<string>? OnInfo;

	// State
	public int CurrentDepth;
	public Move BestMoveSoFar => bestMove;
	public int BestEvalSoFar => bestEval;
	private bool isPlayingWhite;
	private Move bestMoveThisIteration;
	private int bestEvalThisIteration;
	private string bestPvThisEvaluation;
	private Move bestMove;
	private int bestEval;
	private bool hasSearchedAtLeastOneMove;
	private bool searchCancelled;

	private int currMoveNumber, nodeCount, lastNodeCount, maxDepth;
	private Stopwatch timer = new();

	// Diagnostics
	public SearchDiagnostics searchDiagnostics;
	private int currentIterationDepth;
	private Stopwatch searchIterationTimer;
	private Stopwatch searchTotalTimer;
	public string debugInfo;

	// References
	private readonly TranspositionTable transpositionTable;
	private readonly RepetitionTable repetitionTable;
	private readonly MoveGenerator moveGenerator;
	private readonly MoveOrdering moveOrderer;
	private readonly Evaluation evaluation;
	private readonly Board board;

	public Searcher(Board board)
	{
		this.board = board;

		evaluation = new();
		moveGenerator = new();
		transpositionTable = new(board, transpositionTableSizeMB);
		moveOrderer = new(moveGenerator, transpositionTable);
		repetitionTable = new();

		moveGenerator.promotionsToGenerate = MoveGenerator.PromotionMode.QueenAndKnight;

		// Run a depth 1 search so that JIT doesn't run during actual search (and mess up timing stats in editor)
		Search(1, 0, negativeInfinity, positiveInfinity);
	}

	public void StartSearch()
	{
		// Initialize search
		bestEvalThisIteration = bestEval = 0;
		bestMoveThisIteration = bestMove = Move.NullMove;

		isPlayingWhite = board.IsWhiteToMove;

		moveOrderer.ClearHistory();
		repetitionTable.Init(board);

		// Initialize debug info
		CurrentDepth = 0;
		debugInfo = "Starting search with FEN " + FenUtility.CurrentFen(board);
		searchCancelled = false;
		searchDiagnostics = new();
		searchIterationTimer = new();
		searchTotalTimer = System.Diagnostics.Stopwatch.StartNew();

		// Search
		RunIterativeDeepeningSearch();


		// Finish up
		// In the unlikely event that the search is cancelled before a best move can be found, take any move
		if (bestMove.IsNull)
		{
			bestMove = moveGenerator.GenerateMoves(board)[0];
		}
		OnSearchComplete?.Invoke(bestMove);
		searchCancelled = false;
	}

	// Run iterative deepening. This means doing a full search with a depth of 1, then with a depth of 2, and so on.
	// This allows the search to be cancelled at any time and still yield a useful result.
	// Thanks to the transposition table and move ordering, this idea is not nearly as terrible as it sounds.
	private void RunIterativeDeepeningSearch()
	{
		currMoveNumber = 1;
		nodeCount = 0;
		lastNodeCount = 0;
		maxDepth = 0;
		timer.Restart();
		searchTotalTimer.Restart();
		for (var searchDepth = 1; searchDepth <= 256; searchDepth++)
		{
			hasSearchedAtLeastOneMove = false;
			debugInfo += "\nStarting Iteration: " + searchDepth;
			searchIterationTimer.Restart();
			currentIterationDepth = searchDepth;
			try
			{
				Search(searchDepth, 0, negativeInfinity, positiveInfinity);
			}
			catch (InvalidOperationException e)
			{
				EngineUCI.Respond("info string Repro pv: " + e.Message);
				throw;
			}

			if (timer.ElapsedMilliseconds > 100 && (bestEvalThisIteration != int.MinValue || bestEval != int.MinValue))
			{
				var (curEval, curMove) = (bestEvalThisIteration, bestMoveThisIteration);
				if (curEval == int.MinValue)
					(curEval, curMove) = (bestEval, bestMove);
				var curMoveNotation = MoveUtility.GetMoveNameUCI(curMove).Replace("=", "");
				OnInfo?.Invoke($"currmove {curMoveNotation} currmovenumber {currMoveNumber}");

				var selDepth = "";
				if (maxDepth > currentIterationDepth)
					selDepth = $"seldepth {maxDepth}";
				var score = $"cp {curEval}";
				if (IsMateScore(curEval))
					score = $"mate {(curEval < 0 ? "-": "")}{(int)Ceiling(NumPlyToMateFromScore(curEval) / 2.0)}";
				var nps = (int)((nodeCount - lastNodeCount) / timer.Elapsed.TotalSeconds);
				var moveList = GetBestMoveChain(bestMoveThisIteration, currentIterationDepth);
				var pv = moveList.Count > 0
					? string.Join(' ', moveList.Select(MoveUtility.GetMoveNameUCI))
					: MoveUtility.GetMoveNameUCI(curMove);
				OnInfo?.Invoke($"depth {currentIterationDepth} {selDepth} time {(int)searchTotalTimer.ElapsedMilliseconds} nodes {nodeCount} nps {nps} score {score} hashfull {transpositionTable.Hashfull} pv {pv}");
				lastNodeCount = nodeCount;
				timer.Restart();
			}
			if (searchCancelled)
			{
				if (hasSearchedAtLeastOneMove)
				{
					bestMove = bestMoveThisIteration;
					bestEval = bestEvalThisIteration;
					searchDiagnostics.move = MoveUtility.GetMoveNameUCI(bestMove);
					searchDiagnostics.eval = bestEval;
					searchDiagnostics.moveIsFromPartialSearch = true;
					debugInfo += "\nUsing partial search result: " + MoveUtility.GetMoveNameUCI(bestMove) + " Eval: " + bestEval;
				}

				debugInfo += "\nSearch aborted";
				break;
			}
			else
			{
				CurrentDepth = searchDepth;
				bestMove = bestMoveThisIteration;
				bestEval = bestEvalThisIteration;

				debugInfo += "\nIteration result: " + MoveUtility.GetMoveNameUCI(bestMove) + " Eval: " + bestEval;
				if (IsMateScore(bestEval))
				{
					debugInfo += " Mate in ply: " + NumPlyToMateFromScore(bestEval);
				}

				bestEvalThisIteration = int.MinValue;
				bestMoveThisIteration = Move.NullMove;

				// Update diagnostics
				searchDiagnostics.numCompletedIterations = searchDepth;
				searchDiagnostics.move = MoveUtility.GetMoveNameUCI(bestMove);
				searchDiagnostics.eval = bestEval;
				// Exit search if found a mate within search depth.
				// A mate found outside of search depth (due to extensions) may not be the fastest mate.
				if (IsMateScore(bestEval) && NumPlyToMateFromScore(bestEval) <= searchDepth)
				{
					debugInfo += "\nExitting search due to mate found within search depth";
					break;
				}
			}
		}
	}

	private List<Move> GetBestMoveChain(Move move, int maxDepth)
	{
		var startDiagram = BoardHelper.CreateDiagram(board);
		var result = new List<Move>();
		var depth = 0;
		Span<Move> moves = stackalloc Move[256];
		do
		{
			var tmp = moves;
			moveGenerator.GenerateMoves(board, ref tmp, capturesOnly: false);
			if (!tmp.Contains(move))
				break;
			
			result.Add(move);
			depth++;
			board.MakeMove(move, true);
			move = transpositionTable.TryGetStoredMove();
			if (move == Move.NullMove)
				break;
		} while (depth <= maxDepth);
		foreach (var m in result.AsEnumerable().Reverse())
			board.UnmakeMove(m, true);
		var endDiagram = BoardHelper.CreateDiagram(board);
		if (endDiagram != startDiagram)
			throw new InvalidOperationException("Corrupted game state");
		return result;
	}

	public (Move move, int eval) GetSearchResult()
	{
		return (bestMove, bestEval);
	}

	public void EndSearch()
	{
		searchCancelled = true;
	}


	private int Search(int plyRemaining, int plyFromRoot, int alpha, int beta, int numExtensions = 0, Move prevMove = default, bool prevWasCapture = false)
	{
		if (searchCancelled)
		{
			return 0;
		}

		if (plyFromRoot > 0)
		{
			maxDepth = Max(maxDepth, plyFromRoot);
			
			// Detect draw by three-fold repetition.
			// (Note: returns a draw score even if this position has only appeared once for sake of simplicity)
			if (board.CurrentGameState.fiftyMoveCounter >= 100 || repetitionTable.Contains(board.CurrentGameState.zobristKey))
			{
				/*
				const int contempt = 50;
				// So long as not in king and pawn ending, prefer a slightly worse position over game ending in a draw
				if (board.totalPieceCountWithoutPawnsAndKings > 0)
				{
					bool isAITurn = board.IsWhiteToMove == aiPlaysWhite;
					return isAITurn ? -contempt : contempt;
				}
				*/
				return 0;
			}

			// Skip this position if a mating sequence has already been found earlier in the search, which would be shorter
			// than any mate we could find from here. This is done by observing that alpha can't possibly be worse
			// (and likewise beta can't  possibly be better) than being mated in the current position.
			alpha = Max(alpha, -immediateMateScore + plyFromRoot);
			beta = Min(beta, immediateMateScore - plyFromRoot);
			if (alpha >= beta)
			{
				return alpha;
			}
		}

		// Try looking up the current position in the transposition table.
		// If the same position has already been searched to at least an equal depth
		// to the search we're doing now,we can just use the recorded evaluation.
		var ttVal = transpositionTable.LookupEvaluation(plyRemaining, plyFromRoot, alpha, beta);
		if (ttVal != TranspositionTable.LookupFailed)
		{
			if (plyFromRoot == 0)
			{
				bestMoveThisIteration = transpositionTable.TryGetStoredMove();
				bestEvalThisIteration = transpositionTable.entries[transpositionTable.Index].value;
			}
			return ttVal;
		}

		if (plyRemaining == 0)
		{
			var evaluation = QuiescenceSearch(alpha, beta);
			return evaluation;
		}

		Span<Move> moves = stackalloc Move[256];
		moveGenerator.GenerateMoves(board, ref moves, capturesOnly: false);
		var prevBestMove = plyFromRoot == 0 ? bestMove : transpositionTable.TryGetStoredMove();
		moveOrderer.OrderMoves(prevBestMove, board, moves, moveGenerator.opponentAttackMap, moveGenerator.opponentPawnAttackMap, false, plyFromRoot);
		// Detect checkmate and stalemate when no legal moves are available
		if (moves.Length == 0)
		{
			if (moveGenerator.InCheck())
			{
				var mateScore = immediateMateScore - plyFromRoot;
				return -mateScore;
			}
			else
			{
				return 0;
			}
		}

		if (plyFromRoot > 0)
		{
			var wasPawnMove = Piece.PieceType(board.Square[prevMove.TargetSquare]) == Piece.Pawn;
			repetitionTable.Push(board.CurrentGameState.zobristKey, prevWasCapture || wasPawnMove);
		}

		var evaluationBound = TranspositionTable.UpperBound;
		var bestMoveInThisPosition = Move.NullMove;

		for (var i = 0; i < moves.Length; i++)
		{
			var move = moves[i];
			var capturedPieceType = Piece.PieceType(board.Square[move.TargetSquare]);
			var isCapture = capturedPieceType != Piece.None;
			try
			{
				board.MakeMove(moves[i], inSearch: true);
			}
			catch (InvalidOperationException e)
			{
				var moveStr = MoveUtility.GetMoveNameUCI(move);
				throw new InvalidOperationException($"{moveStr} {e.Message}", e);
			}

			// Extend the depth of the search in certain interesting cases
			var extension = 0;
			if (numExtensions < maxExtentions)
			{
				var movedPieceType = Piece.PieceType(board.Square[move.TargetSquare]);
				var targetRank = BoardHelper.RankIndex(move.TargetSquare);
				if (board.IsInCheck())
				{
					extension = 1;
				}
				else if (movedPieceType == Piece.Pawn && (targetRank == 1 || targetRank == 6))
				{
					extension = 1;
				}
			}

			var needsFullSearch = true;
			var eval = 0;
			// Reduce the depth of the search for moves later in the move list as these are less likely to be good
			// (assuming our move ordering isn't terrible)
			if (extension == 0 && plyRemaining >= 3 && i >= 3 && !isCapture)
			{
				const int reduceDepth = 1;
				eval = -Search(plyRemaining - 1 - reduceDepth, plyFromRoot + 1, -alpha - 1, -alpha, numExtensions, move, isCapture);
				// If the evaluation is better than expected, we'd better to a full-depth search to get a more accurate evaluation
				needsFullSearch = eval > alpha;
			}
			// Perform a full-depth search
			if (needsFullSearch)
			{
				eval = -Search(plyRemaining - 1 + extension, plyFromRoot + 1, -beta, -alpha, numExtensions + extension, move, isCapture);
			}
			board.UnmakeMove(moves[i], inSearch: true);
			Interlocked.Increment(ref nodeCount);

			if (searchCancelled)
			{
				return 0;
			}

			// Move was *too* good, opponent will choose a different move earlier on to avoid this position.
			// (Beta-cutoff / Fail high)
			if (eval >= beta)
			{
				// Store evaluation in transposition table. Note that since we're exiting the search early, there may be an
				// even better move we haven't looked at yet, and so the current eval is a lower bound on the actual eval.
				transpositionTable.StoreEvaluation(plyRemaining, plyFromRoot, beta, TranspositionTable.LowerBound, moves[i]);

				// Update killer moves and history heuristic (note: don't include captures as they are ranked highly anyway)
				if (!isCapture)
				{
					if (plyFromRoot < MoveOrdering.maxKillerMovePly)
					{
						moveOrderer.killerMoves[plyFromRoot].Add(move);
					}
					var historyScore = plyRemaining * plyRemaining;
					moveOrderer.History[board.MoveColourIndex, moves[i].StartSquare, moves[i].TargetSquare] += historyScore;
				}
				if (plyFromRoot > 0)
				{
					repetitionTable.TryPop();
				}

				searchDiagnostics.numCutOffs++;
				return beta;
			}

			// Found a new best move in this position
			if (eval > alpha)
			{
				evaluationBound = TranspositionTable.Exact;
				bestMoveInThisPosition = moves[i];

				alpha = eval;
				if (plyFromRoot == 0)
				{
					bestMoveThisIteration = moves[i];
					bestEvalThisIteration = eval;
					hasSearchedAtLeastOneMove = true;
				}
			}
		}

		if (plyFromRoot > 0)
		{
			repetitionTable.TryPop();
		}

		transpositionTable.StoreEvaluation(plyRemaining, plyFromRoot, alpha, evaluationBound, bestMoveInThisPosition);

		return alpha;

	}

	// Search capture moves until a 'quiet' position is reached.
	private int QuiescenceSearch(int alpha, int beta)
	{
		if (searchCancelled)
		{
			return 0;
		}
		// A player isn't forced to make a capture (typically), so see what the evaluation is without capturing anything.
		// This prevents situations where a player ony has bad captures available from being evaluated as bad,
		// when the player might have good non-capture moves available.
		var eval = evaluation.Evaluate(board);
		searchDiagnostics.numPositionsEvaluated++;
		if (eval >= beta)
		{
			searchDiagnostics.numCutOffs++;
			return beta;
		}
		if (eval > alpha)
		{
			alpha = eval;
		}

		Span<Move> moves = stackalloc Move[128];
		moveGenerator.GenerateMoves(board, ref moves, capturesOnly: true);
		moveOrderer.OrderMoves(Move.NullMove, board, moves, moveGenerator.opponentAttackMap, moveGenerator.opponentPawnAttackMap, true, 0);
		for (var i = 0; i < moves.Length; i++)
		{
			board.MakeMove(moves[i], true);
			eval = -QuiescenceSearch(-beta, -alpha);
			board.UnmakeMove(moves[i], true);

			if (eval >= beta)
			{
				searchDiagnostics.numCutOffs++;
				return beta;
			}
			if (eval > alpha)
			{
				alpha = eval;
			}
		}

		return alpha;
	}


	public static bool IsMateScore(int score)
	{
		if (score == int.MinValue)
		{
			return false;
		}
		const int maxMateDepth = 1000;
		return Abs(score) > immediateMateScore - maxMateDepth;
	}

	public static int NumPlyToMateFromScore(int score)
	{
		return immediateMateScore - Abs(score);

	}

	public string AnnounceMate()
	{
		if (IsMateScore(bestEvalThisIteration))
		{
			var numPlyToMate = NumPlyToMateFromScore(bestEvalThisIteration);
			var numMovesToMate = (int)Ceiling(numPlyToMate / 2f);

			var sideWithMate = (bestEvalThisIteration * ((board.IsWhiteToMove) ? 1 : -1) < 0) ? "Black" : "White";

			return $"{sideWithMate} can mate in {numMovesToMate} move{((numMovesToMate > 1) ? "s" : "")}";
		}
		return "No mate found";
	}

	public void ClearForNewPosition()
	{
		transpositionTable.Clear();
		moveOrderer.ClearKillers();
	}

	public TranspositionTable GetTranspositionTable() => transpositionTable;

	[Serializable]
	public struct SearchDiagnostics
	{
		public int numCompletedIterations;
		public int numPositionsEvaluated;
		public ulong numCutOffs;

		public string moveVal;
		public string move;
		public int eval;
		public bool moveIsFromPartialSearch;
		public int NumQChecks;
		public int numQMates;

		public bool isBook;

		public int maxExtentionReachedInSearch;
	}
}