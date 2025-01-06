using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Chess.Core;

// Thanks to https://web.archive.org/web/20071031100051/http://www.brucemo.com/compchess/programming/hashing.htm
public class TranspositionTable
{

	public const int LookupFailed = -1;

	// The value for this position is the exact evaluation
	public const int Exact = 0;
	// A move was found during the search that was too good, meaning the opponent will play a different move earlier on,
	// not allowing the position where this move was available to be reached. Because the search cuts off at
	// this point (beta cut-off), an even better move may exist. This means that the evaluation for the
	// position could be even higher, making the stored value the lower bound of the actual value.
	public const int LowerBound = 1;
	// No move during the search resulted in a position that was better than the current player could get from playing a
	// different move in an earlier position (i.e. eval was <= alpha for all moves in the position).
	// Due to the way alpha-beta search works, the value we get here won't be the exact evaluation of the position,
	// but rather the upper bound of the evaluation. This means that the evaluation is, at most, equal to this value.
	public const int UpperBound = 2;

	public Entry[] entries;

	public readonly int count;
	public bool enabled = true;
	Board board;

	private static readonly int ttEntrySizeBytes = Marshal.SizeOf<Entry>();
	private readonly Lock setLocker = new();
	private readonly HashSet<ulong> setEntries;
	public int Hashfull
	{
		get
		{
			lock (setLocker)
				return (int)(setEntries.Count * 100_0L / count);
		}
	}

	public TranspositionTable(Board board, int sizeMB)
	{
		this.board = board;

		var desiredTableSizeInBytes = sizeMB * 1024 * 1024;
		var numEntries = desiredTableSizeInBytes / ttEntrySizeBytes;

		count = numEntries;
		entries = new Entry[numEntries];
		setEntries = new(numEntries);
	}

	public void Clear()
	{
		for (var i = 0; i < entries.Length; i++)
		{
			entries[i] = new();
		}
		lock (setLocker) setEntries.Clear();
	}

	public ulong Index => board.CurrentGameState.zobristKey % (ulong)count;

	public Move TryGetStoredMove() => entries[Index].move;


	public bool TryLookupEvaluation(int depth, int plyFromRoot, int alpha, int beta, out int eval)
	{
		eval = 0;
		return false;
	}

	public int LookupEvaluation(int depth, int plyFromRoot, int alpha, int beta)
	{
		if (!enabled)
		{
			return LookupFailed;
		}
		var entry = entries[Index];

		if (entry.key == board.CurrentGameState.zobristKey)
		{
			// Only use stored evaluation if it has been searched to at least the same depth as would be searched now
			if (entry.depth >= depth)
			{
				var correctedScore = CorrectRetrievedMateScore(entry.value, plyFromRoot);
				// We have stored the exact evaluation for this position, so return it
				if (entry.nodeType == Exact)
				{
					return correctedScore;
				}
				// We have stored the upper bound of the eval for this position. If it's less than alpha then we don't need to
				// search the moves in this position as they won't interest us; otherwise we will have to search to find the exact value
				if (entry.nodeType == UpperBound && correctedScore <= alpha)
				{
					return correctedScore;
				}
				// We have stored the lower bound of the eval for this position. Only return if it causes a beta cut-off.
				if (entry.nodeType == LowerBound && correctedScore >= beta)
				{
					return correctedScore;
				}
			}
		}
		return LookupFailed;
	}

	public void StoreEvaluation(int depth, int numPlySearched, int eval, int evalType, Move move)
	{
		if (!enabled)
		{
			return;
		}
		var index = Index;

		//if (depth >= entries[Index].depth) {
		var entry = new Entry(board.CurrentGameState.zobristKey, CorrectMateScoreForStorage(eval, numPlySearched), (byte)depth, (byte)evalType, move);
		entries[index] = entry;
		lock (setLocker) setEntries.Add(index);
		//}
	}

	int CorrectMateScoreForStorage(int score, int numPlySearched)
	{
		if (Searcher.IsMateScore(score))
		{
			var sign = System.Math.Sign(score);
			return (score * sign + numPlySearched) * sign;
		}
		return score;
	}

	int CorrectRetrievedMateScore(int score, int numPlySearched)
	{
		if (Searcher.IsMateScore(score))
		{
			var sign = System.Math.Sign(score);
			return (score * sign - numPlySearched) * sign;
		}
		return score;
	}

	public Entry GetEntry(ulong zobristKey)
	{
		return entries[zobristKey % (ulong)entries.Length];
	}

	public struct Entry
	{
		public readonly ulong key;
		public readonly int value;
		public readonly Move move;
		public readonly byte depth;
		public readonly byte nodeType;

		//	public readonly byte gamePly;

		public Entry(ulong key, int value, byte depth, byte nodeType, Move move)
		{
			this.key = key;
			this.value = value;
			this.depth = depth; // depth is how many ply were searched ahead from this position
			this.nodeType = nodeType;
			this.move = move;
		}
	}
}