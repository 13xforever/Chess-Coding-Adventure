using static Chess.Core.PrecomputedMagics;

namespace Chess.Core;

// Helper class for magic bitboards.
// This is a technique where bishop and rook moves are precomputed
// for any configuration of origin square and blocking pieces.
public static class Magic
{
	// Rook and bishop mask bitboards for each origin square.
	// A mask is simply the legal moves available to the piece from the origin square
	// (on an empty board), except that the moves stop 1 square before the edge of the board.
	public static readonly ulong[] RookMask;
	public static readonly ulong[] BishopMask;

	public static readonly ulong[][] RookAttacks;
	public static readonly ulong[][] BishopAttacks;


	public static ulong GetSliderAttacks(int square, ulong blockers, bool ortho)
	{
		return ortho ? GetRookAttacks(square, blockers) : GetBishopAttacks(square, blockers);
	}

	public static ulong GetRookAttacks(int square, ulong blockers)
	{
		var key = ((blockers & RookMask[square]) * RookMagics[square]) >> RookShifts[square];
		return RookAttacks[square][key];
	}

	public static ulong GetBishopAttacks(int square, ulong blockers)
	{
		var key = ((blockers & BishopMask[square]) * BishopMagics[square]) >> BishopShifts[square];
		return BishopAttacks[square][key];
	}


	static Magic()
	{
		RookMask = new ulong[64];
		BishopMask = new ulong[64];

		for (var squareIndex = 0; squareIndex < 64; squareIndex++)
		{
			RookMask[squareIndex] = MagicHelper.CreateMovementMask(squareIndex, true);
			BishopMask[squareIndex] = MagicHelper.CreateMovementMask(squareIndex, false);
		}

		RookAttacks = new ulong[64][];
		BishopAttacks = new ulong[64][];

		for (var i = 0; i < 64; i++)
		{
			RookAttacks[i] = CreateTable(i, true, RookMagics[i], RookShifts[i]);
			BishopAttacks[i] = CreateTable(i, false, BishopMagics[i], BishopShifts[i]);
		}
	}

	private static ulong[] CreateTable(int square, bool rook, ulong magic, int leftShift)
	{
		var numBits = 64 - leftShift;
		var lookupSize = 1 << numBits;
		var table = new ulong[lookupSize];

		var movementMask = MagicHelper.CreateMovementMask(square, rook);
		var blockerPatterns = MagicHelper.CreateAllBlockerBitboards(movementMask);

		foreach (var pattern in blockerPatterns)
		{
			var index = (pattern * magic) >> leftShift;
			var moves = MagicHelper.LegalMoveBitboardFromBlockers(square, pattern, rook);
			table[index] = moves;
		}
		return table;
	}
}