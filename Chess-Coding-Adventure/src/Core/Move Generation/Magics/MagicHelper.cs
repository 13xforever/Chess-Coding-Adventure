using System.Collections.Generic;

namespace Chess.Core;

public static class MagicHelper
{
	public static ulong[] CreateAllBlockerBitboards(ulong movementMask)
	{
		// Create a list of the indices of the bits that are set in the movement mask
		List<int> moveSquareIndices = new();
		for (var i = 0; i < 64; i++)
		{
			if (((movementMask >> i) & 1) == 1)
			{
				moveSquareIndices.Add(i);
			}
		}

		// Calculate total number of different bitboards (one for each possible arrangement of pieces)
		var numPatterns = 1 << moveSquareIndices.Count; // 2^n
		var blockerBitboards = new ulong[numPatterns];

		// Create all bitboards
		for (var patternIndex = 0; patternIndex < numPatterns; patternIndex++)
		{
			for (var bitIndex = 0; bitIndex < moveSquareIndices.Count; bitIndex++)
			{
				var bit = (patternIndex >> bitIndex) & 1;
				blockerBitboards[patternIndex] |= (ulong)bit << moveSquareIndices[bitIndex];
			}
		}

		return blockerBitboards;
	}


	public static ulong CreateMovementMask(int squareIndex, bool ortho)
	{
		ulong mask = 0;
		var directions = ortho ? BoardHelper.RookDirections : BoardHelper.BishopDirections;
		var startCoord = new Coord(squareIndex);

		foreach (var dir in directions)
		{
			for (var dst = 1; dst < 8; dst++)
			{
				var coord = startCoord + dir * dst;
				var nextCoord = startCoord + dir * (dst + 1);

				if (nextCoord.IsValidSquare())
				{
					BitBoardUtility.SetSquare(ref mask, coord.SquareIndex);
				}
				else { break; }
			}
		}
		return mask;
	}

	public static ulong LegalMoveBitboardFromBlockers(int startSquare, ulong blockerBitboard, bool ortho)
	{
		ulong bitboard = 0;

		var directions = ortho ? BoardHelper.RookDirections : BoardHelper.BishopDirections;
		var startCoord = new Coord(startSquare);

		foreach (var dir in directions)
		{
			for (var dst = 1; dst < 8; dst++)
			{
				var coord = startCoord + dir * dst;

				if (coord.IsValidSquare())
				{
					BitBoardUtility.SetSquare(ref bitboard, coord.SquareIndex);
					if (BitBoardUtility.ContainsSquare(blockerBitboard, coord.SquareIndex))
					{
						break;
					}
				}
				else { break; }
			}
		}

		return bitboard;
	}
}