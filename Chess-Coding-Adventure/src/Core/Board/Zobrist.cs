﻿namespace Chess.Core;
// Helper class for the calculation of zobrist hash.
// This is a single 64bit value that (non-uniquely) represents the current state of the game.

// It is mainly used for quickly detecting positions that have already been evaluated, to avoid
// potentially performing lots of duplicate work during game search.

public static class Zobrist
{
	// Random numbers are generated for each aspect of the game state, and are used for calculating the hash:

	// piece type, colour, square index
	public static readonly ulong[,] piecesArray = new ulong[Piece.MaxPieceIndex + 1, 64];
	// Each player has 4 possible castling right states: none, queenside, kingside, both.
	// So, taking both sides into account, there are 16 possible states.
	public static readonly ulong[] castlingRights = new ulong[16];
	// En passant file (0 = no ep).
	//  Rank does not need to be specified since side to move is included in key
	public static readonly ulong[] enPassantFile = new ulong[9];
	public static readonly ulong sideToMove;

	static Zobrist()
	{

		const int seed = 29426028;
		var rng = new System.Random(seed);

		for (var squareIndex = 0; squareIndex < 64; squareIndex++)
		{
			foreach (var piece in Piece.PieceIndices)
			{
				piecesArray[piece, squareIndex] = RandomUnsigned64BitNumber(rng);
			}
		}


		for (var i = 0; i < castlingRights.Length; i++)
		{
			castlingRights[i] = RandomUnsigned64BitNumber(rng);
		}

		for (var i = 0; i < enPassantFile.Length; i++)
		{
			enPassantFile[i] = i == 0 ? 0 : RandomUnsigned64BitNumber(rng);
		}

		sideToMove = RandomUnsigned64BitNumber(rng);
	}

	// Calculate zobrist key from current board position.
	// NOTE: this function is slow and should only be used when the board is initially set up from fen.
	// During search, the key should be updated incrementally instead.
	public static ulong CalculateZobristKey(Board board)
	{
		ulong zobristKey = 0;

		for (var squareIndex = 0; squareIndex < 64; squareIndex++)
		{
			var piece = board.Square[squareIndex];

			if (Piece.PieceType(piece) != Piece.None)
			{
				zobristKey ^= piecesArray[piece, squareIndex];
			}
		}

		zobristKey ^= enPassantFile[board.CurrentGameState.enPassantFile];

		if (board.MoveColour == Piece.Black)
		{
			zobristKey ^= sideToMove;
		}

		zobristKey ^= castlingRights[board.CurrentGameState.castlingRights];

		return zobristKey;
	}

	private static ulong RandomUnsigned64BitNumber(System.Random rng)
	{
		var buffer = new byte[8];
		rng.NextBytes(buffer);
		return System.BitConverter.ToUInt64(buffer, 0);
	}
}