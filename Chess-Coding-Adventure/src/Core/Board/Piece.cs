﻿namespace Chess.Core;

// Contains definitions for each piece type (represented as integers),
// as well as various helper functions for dealing with pieces.
public static class Piece
{
	// Piece Types
	public const int None = 0;
	public const int Pawn = 1;
	public const int Knight = 2;
	public const int Bishop = 3;
	public const int Rook = 4;
	public const int Queen = 5;
	public const int King = 6;

	// Piece Colours
	public const int White = 0;
	public const int Black = 8;

	// Pieces
	public const int WhitePawn = Pawn | White; // 1
	public const int WhiteKnight = Knight | White; // 2
	public const int WhiteBishop = Bishop | White; // 3
	public const int WhiteRook = Rook | White; // 4
	public const int WhiteQueen = Queen | White; // 5
	public const int WhiteKing = King | White; // 6

	public const int BlackPawn = Pawn | Black; // 9
	public const int BlackKnight = Knight | Black; // 10
	public const int BlackBishop = Bishop | Black; // 11
	public const int BlackRook = Rook | Black; // 12
	public const int BlackQueen = Queen | Black; // 13
	public const int BlackKing = King | Black; // 14

	public const int MaxPieceIndex = BlackKing;

	public static readonly int[] PieceIndices =
	[
		WhitePawn, WhiteKnight, WhiteBishop, WhiteRook, WhiteQueen, WhiteKing,
		BlackPawn, BlackKnight, BlackBishop, BlackRook, BlackQueen, BlackKing
	];

	// Bit Masks
	private const int typeMask   = 0b_0111;
	private const int colourMask = 0b_1000;

	public static int MakePiece(int pieceType, int pieceColour) => pieceType | pieceColour;

	public static int MakePiece(int pieceType, bool pieceIsWhite) => MakePiece(pieceType, pieceIsWhite ? White : Black);

	// Returns true if given piece matches the given colour. If piece is of type 'none', result will always be false.
	public static bool IsColour(int piece, int colour) => (piece & colourMask) == colour && piece != 0;

	public static bool IsWhite(int piece) => IsColour(piece, White);

	public static int PieceColour(int piece) => piece & colourMask;

	public static int PieceType(int piece) => piece & typeMask;

	// Rook or Queen
	public static bool IsOrthogonalSlider(int piece) => PieceType(piece) is Queen or Rook;

	// Bishop or Queen
	public static bool IsDiagonalSlider(int piece) => PieceType(piece) is Queen or Bishop;

	// Bishop, Rook, or Queen
	public static bool IsSlidingPiece(int piece) => PieceType(piece) is Queen or Bishop or Rook;

	public static char GetSymbol(int piece)
	{
		var symbol = PieceType(piece) switch
		{
			Rook => 'R',
			Knight => 'N',
			Bishop => 'B',
			Queen => 'Q',
			King => 'K',
			Pawn => 'P',
			_ => ' '
		};
		return IsWhite(piece) ? symbol : char.ToLower(symbol);
	}

	public static int GetPieceTypeFromSymbol(char symbol)
		=> char.ToUpper(symbol) switch
		{
			'R' => Rook,
			'N' => Knight,
			'B' => Bishop,
			'Q' => Queen,
			'K' => King,
			'P' => Pawn,
			_ => None
		};
}