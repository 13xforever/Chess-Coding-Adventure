using System;
using System.Linq;

namespace Chess.Core;

public class RepetitionTable
{
	readonly ulong[] hashes;
	readonly int[] startIndices;
	int count;

	public RepetitionTable()
	{
		hashes = new ulong[256];
		startIndices = new int[hashes.Length + 1];
	}

	public void Init(Board board)
	{
		var initialHashes = board.RepetitionPositionHistory.Reverse().ToArray();
		count = initialHashes.Length;

		for (var i = 0; i < initialHashes.Length; i++)
		{
			hashes[i] = initialHashes[i];
			startIndices[i] = 0;
		}
		startIndices[count] = 0;
	}


	public void Push(ulong hash, bool reset)
	{
		// Check bounds just in case
		if (count < hashes.Length)
		{
			hashes[count] = hash;
			startIndices[count + 1] = reset ? count : startIndices[count];
		}
		count++;
	}

	public void TryPop()
	{
		count = Math.Max(0, count - 1);
	}

	public bool Contains(ulong h)
	{
		var s = startIndices[count];
		// up to count-1 so that curr position is not counted
		for (var i = s; i < count - 1; i++)
		{
			if (hashes[i] == h)
			{
				return true;
			}
		}
		return false;
	}
}