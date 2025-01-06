using System;
using static Chess.Core.PrecomputedMoveData;

namespace Chess.Core;

public class MoveGenerator
{
	public const int MaxMoves = 218;
	//public enum PromotionMode { All, QueenOnly, QueenAndKnight }
	public enum PromotionMode { All, QueenOnly, QueenAndKnight }

	public PromotionMode promotionsToGenerate = PromotionMode.All;

	// ---- Instance variables ----
	bool isWhiteToMove;
	int friendlyColour;
	int opponentColour;
	int friendlyKingSquare;
	int friendlyIndex;
	int enemyIndex;

	bool inCheck;
	bool inDoubleCheck;

	// If in check, this bitboard contains squares in line from checking piece up to king
	// If not in check, all bits are set to 1
	ulong checkRayBitmask;

	ulong pinRays;
	ulong notPinRays;
	ulong opponentAttackMapNoPawns;
	public ulong opponentAttackMap;
	public ulong opponentPawnAttackMap;
	ulong opponentSlidingAttackMap;

	bool generateQuietMoves;
	Board board;
	int currMoveIndex;

	ulong enemyPieces;
	ulong friendlyPieces;
	ulong allPieces;
	ulong emptySquares;
	ulong emptyOrEnemySquares;
	// If only captures should be generated, this will have 1s only in positions of enemy pieces.
	// Otherwise it will have 1s everywhere.
	ulong moveTypeMask;

	public Span<Move> GenerateMoves(Board board, bool capturesOnly = false)
	{
		Span<Move> moves = new Move[MaxMoves];
		GenerateMoves(board, ref moves, capturesOnly);
		return moves;
	}

	// Generates list of legal moves in current position.
	// Quiet moves (non captures) can optionally be excluded. This is used in quiescence search.
	public int GenerateMoves(Board board, ref Span<Move> moves, bool capturesOnly = false)
	{
		this.board = board;
		generateQuietMoves = !capturesOnly;

		Init();

		GenerateKingMoves(moves);

		// Only king moves are valid in a double check position, so can return early.
		if (!inDoubleCheck)
		{
			GenerateSlidingMoves(moves);
			GenerateKnightMoves(moves);
			GeneratePawnMoves(moves);
		}

		moves = moves[..currMoveIndex];
		return moves.Length;
	}

	// Note, this will only return correct value after GenerateMoves() has been called in the current position
	public bool InCheck()
	{
		return inCheck;
	}

	void Init()
	{
		// Reset state
		currMoveIndex = 0;
		inCheck = false;
		inDoubleCheck = false;
		checkRayBitmask = 0;
		pinRays = 0;

		// Store some info for convenience
		isWhiteToMove = board.MoveColour == Piece.White;
		friendlyColour = board.MoveColour;
		opponentColour = board.OpponentColour;
		friendlyKingSquare = board.KingSquare[board.MoveColourIndex];
		friendlyIndex = board.MoveColourIndex;
		enemyIndex = 1 - friendlyIndex;

		// Store some bitboards for convenience
		enemyPieces = board.ColourBitboards[enemyIndex];
		friendlyPieces = board.ColourBitboards[friendlyIndex];
		allPieces = board.AllPiecesBitboard;
		emptySquares = ~allPieces;
		emptyOrEnemySquares = emptySquares | enemyPieces;
		moveTypeMask = generateQuietMoves ? ulong.MaxValue : enemyPieces;

		CalculateAttackData();
	}

	void GenerateKingMoves(Span<Move> moves)
	{
		var legalMask = ~(opponentAttackMap | friendlyPieces);
		var kingMoves = BitBoardUtility.KingMoves[friendlyKingSquare] & legalMask & moveTypeMask;
		while (kingMoves != 0)
		{
			var targetSquare = BitBoardUtility.PopLSB(ref kingMoves);
			moves[currMoveIndex++] = new(friendlyKingSquare, targetSquare);
		}

		// Castling
		if (!inCheck && generateQuietMoves)
		{
			var castleBlockers = opponentAttackMap | board.AllPiecesBitboard;
			if (board.CurrentGameState.HasKingsideCastleRight(board.IsWhiteToMove))
			{
				var castleMask = board.IsWhiteToMove ? Bits.WhiteKingsideMask : Bits.BlackKingsideMask;
				if ((castleMask & castleBlockers) == 0)
				{
					var targetSquare = board.IsWhiteToMove ? BoardHelper.g1 : BoardHelper.g8;
					moves[currMoveIndex++] = new(friendlyKingSquare, targetSquare, Move.CastleFlag);
				}
			}
			if (board.CurrentGameState.HasQueensideCastleRight(board.IsWhiteToMove))
			{
				var castleMask = board.IsWhiteToMove ? Bits.WhiteQueensideMask2 : Bits.BlackQueensideMask2;
				var castleBlockMask = board.IsWhiteToMove ? Bits.WhiteQueensideMask : Bits.BlackQueensideMask;
				if ((castleMask & castleBlockers) == 0 && (castleBlockMask & board.AllPiecesBitboard) == 0)
				{
					var targetSquare = board.IsWhiteToMove ? BoardHelper.c1 : BoardHelper.c8;
					moves[currMoveIndex++] = new(friendlyKingSquare, targetSquare, Move.CastleFlag);
				}
			}
		}
	}

	void GenerateSlidingMoves(Span<Move> moves)
	{
		// Limit movement to empty or enemy squares, and must block check if king is in check.
		var moveMask = emptyOrEnemySquares & checkRayBitmask & moveTypeMask;

		var othogonalSliders = board.FriendlyOrthogonalSliders;
		var diagonalSliders = board.FriendlyDiagonalSliders;

		// Pinned pieces cannot move if king is in check
		if (inCheck)
		{
			othogonalSliders &= ~pinRays;
			diagonalSliders &= ~pinRays;
		}

		// Ortho
		while (othogonalSliders != 0)
		{
			var startSquare = BitBoardUtility.PopLSB(ref othogonalSliders);
			var moveSquares = Magic.GetRookAttacks(startSquare, allPieces) & moveMask;

			// If piece is pinned, it can only move along the pin ray
			if (IsPinned(startSquare))
			{
				moveSquares &= alignMask[startSquare, friendlyKingSquare];
			}

			while (moveSquares != 0)
			{
				var targetSquare = BitBoardUtility.PopLSB(ref moveSquares);
				moves[currMoveIndex++] = new(startSquare, targetSquare);
			}
		}

		// Diag
		while (diagonalSliders != 0)
		{
			var startSquare = BitBoardUtility.PopLSB(ref diagonalSliders);
			var moveSquares = Magic.GetBishopAttacks(startSquare, allPieces) & moveMask;

			// If piece is pinned, it can only move along the pin ray
			if (IsPinned(startSquare))
			{
				moveSquares &= alignMask[startSquare, friendlyKingSquare];
			}

			while (moveSquares != 0)
			{
				var targetSquare = BitBoardUtility.PopLSB(ref moveSquares);
				moves[currMoveIndex++] = new(startSquare, targetSquare);
			}
		}
	}


	void GenerateKnightMoves(Span<Move> moves)
	{
		var friendlyKnightPiece = Piece.MakePiece(Piece.Knight, board.MoveColour);
		// bitboard of all non-pinned knights
		var knights = board.PieceBitboards[friendlyKnightPiece] & notPinRays;
		var moveMask = emptyOrEnemySquares & checkRayBitmask & moveTypeMask;

		while (knights != 0)
		{
			var knightSquare = BitBoardUtility.PopLSB(ref knights);
			var moveSquares = BitBoardUtility.KnightAttacks[knightSquare] & moveMask;

			while (moveSquares != 0)
			{
				var targetSquare = BitBoardUtility.PopLSB(ref moveSquares);
				moves[currMoveIndex++] = new(knightSquare, targetSquare);
			}
		}
	}

	void GeneratePawnMoves(Span<Move> moves)
	{
		var pushDir = board.IsWhiteToMove ? 1 : -1;
		var pushOffset = pushDir * 8;

		var friendlyPawnPiece = Piece.MakePiece(Piece.Pawn, board.MoveColour);
		var pawns = board.PieceBitboards[friendlyPawnPiece];

		var promotionRankMask = board.IsWhiteToMove ? BitBoardUtility.Rank8 : BitBoardUtility.Rank1;

		var singlePush = (BitBoardUtility.Shift(pawns, pushOffset)) & emptySquares;

		var pushPromotions = singlePush & promotionRankMask & checkRayBitmask;


		var captureEdgeFileMask = board.IsWhiteToMove ? BitBoardUtility.notAFile : BitBoardUtility.notHFile;
		var captureEdgeFileMask2 = board.IsWhiteToMove ? BitBoardUtility.notHFile : BitBoardUtility.notAFile;
		var captureA = BitBoardUtility.Shift(pawns & captureEdgeFileMask, pushDir * 7) & enemyPieces;
		var captureB = BitBoardUtility.Shift(pawns & captureEdgeFileMask2, pushDir * 9) & enemyPieces;

		var singlePushNoPromotions = singlePush & ~promotionRankMask & checkRayBitmask;

		var capturePromotionsA = captureA & promotionRankMask & checkRayBitmask;
		var capturePromotionsB = captureB & promotionRankMask & checkRayBitmask;

		captureA &= checkRayBitmask & ~promotionRankMask;
		captureB &= checkRayBitmask & ~promotionRankMask;

		// Single / double push
		if (generateQuietMoves)
		{
			// Generate single pawn pushes
			while (singlePushNoPromotions != 0)
			{
				var targetSquare = BitBoardUtility.PopLSB(ref singlePushNoPromotions);
				var startSquare = targetSquare - pushOffset;
				if (!IsPinned(startSquare) || alignMask[startSquare, friendlyKingSquare] == alignMask[targetSquare, friendlyKingSquare])
				{
					moves[currMoveIndex++] = new(startSquare, targetSquare);
				}
			}

			// Generate double pawn pushes
			var doublePushTargetRankMask = board.IsWhiteToMove ? BitBoardUtility.Rank4 : BitBoardUtility.Rank5;
			var doublePush = BitBoardUtility.Shift(singlePush, pushOffset) & emptySquares & doublePushTargetRankMask & checkRayBitmask;

			while (doublePush != 0)
			{
				var targetSquare = BitBoardUtility.PopLSB(ref doublePush);
				var startSquare = targetSquare - pushOffset * 2;
				if (!IsPinned(startSquare) || alignMask[startSquare, friendlyKingSquare] == alignMask[targetSquare, friendlyKingSquare])
				{
					moves[currMoveIndex++] = new(startSquare, targetSquare, Move.PawnTwoUpFlag);
				}
			}
		}

		// Captures
		while (captureA != 0)
		{
			var targetSquare = BitBoardUtility.PopLSB(ref captureA);
			var startSquare = targetSquare - pushDir * 7;

			if (!IsPinned(startSquare) || alignMask[startSquare, friendlyKingSquare] == alignMask[targetSquare, friendlyKingSquare])
			{
				moves[currMoveIndex++] = new(startSquare, targetSquare);
			}
		}

		while (captureB != 0)
		{
			var targetSquare = BitBoardUtility.PopLSB(ref captureB);
			var startSquare = targetSquare - pushDir * 9;

			if (!IsPinned(startSquare) || alignMask[startSquare, friendlyKingSquare] == alignMask[targetSquare, friendlyKingSquare])
			{
				moves[currMoveIndex++] = new(startSquare, targetSquare);
			}
		}



		// Promotions
		while (pushPromotions != 0)
		{
			var targetSquare = BitBoardUtility.PopLSB(ref pushPromotions);
			var startSquare = targetSquare - pushOffset;
			if (!IsPinned(startSquare))
			{
				GeneratePromotions(startSquare, targetSquare, moves);
			}
		}


		while (capturePromotionsA != 0)
		{
			var targetSquare = BitBoardUtility.PopLSB(ref capturePromotionsA);
			var startSquare = targetSquare - pushDir * 7;

			if (!IsPinned(startSquare) || alignMask[startSquare, friendlyKingSquare] == alignMask[targetSquare, friendlyKingSquare])
			{
				GeneratePromotions(startSquare, targetSquare, moves);
			}
		}

		while (capturePromotionsB != 0)
		{
			var targetSquare = BitBoardUtility.PopLSB(ref capturePromotionsB);
			var startSquare = targetSquare - pushDir * 9;

			if (!IsPinned(startSquare) || alignMask[startSquare, friendlyKingSquare] == alignMask[targetSquare, friendlyKingSquare])
			{
				GeneratePromotions(startSquare, targetSquare, moves);
			}
		}

		// En passant
		if (board.CurrentGameState.enPassantFile > 0)
		{
			var epFileIndex = board.CurrentGameState.enPassantFile - 1;
			var epRankIndex = board.IsWhiteToMove ? 5 : 2;
			var targetSquare = epRankIndex * 8 + epFileIndex;
			var capturedPawnSquare = targetSquare - pushOffset;

			if (BitBoardUtility.ContainsSquare(checkRayBitmask, capturedPawnSquare))
			{
				var pawnsThatCanCaptureEp = pawns & BitBoardUtility.PawnAttacks(1ul << targetSquare, !board.IsWhiteToMove);

				while (pawnsThatCanCaptureEp != 0)
				{
					var startSquare = BitBoardUtility.PopLSB(ref pawnsThatCanCaptureEp);
					if (!IsPinned(startSquare) || alignMask[startSquare, friendlyKingSquare] == alignMask[targetSquare, friendlyKingSquare])
					{
						if (!InCheckAfterEnPassant(startSquare, targetSquare, capturedPawnSquare))
						{
							moves[currMoveIndex++] = new(startSquare, targetSquare, Move.EnPassantCaptureFlag);
						}
					}
				}
			}
		}
	}

	void GeneratePromotions(int startSquare, int targetSquare, Span<Move> moves)
	{
		moves[currMoveIndex++] = new(startSquare, targetSquare, Move.PromoteToQueenFlag);
		// Don't generate non-queen promotions in q-search
		if (generateQuietMoves)
		{
			if (promotionsToGenerate == PromotionMode.All)
			{
				moves[currMoveIndex++] = new(startSquare, targetSquare, Move.PromoteToKnightFlag);
				moves[currMoveIndex++] = new(startSquare, targetSquare, Move.PromoteToRookFlag);
				moves[currMoveIndex++] = new(startSquare, targetSquare, Move.PromoteToBishopFlag);
			}
			else if (promotionsToGenerate == PromotionMode.QueenAndKnight)
			{
				moves[currMoveIndex++] = new(startSquare, targetSquare, Move.PromoteToKnightFlag);
			}
		}
	}

	bool IsPinned(int square)
	{
		return ((pinRays >> square) & 1) != 0;
	}

	void GenSlidingAttackMap()
	{
		opponentSlidingAttackMap = 0;

		UpdateSlideAttack(board.EnemyOrthogonalSliders, true);
		UpdateSlideAttack(board.EnemyDiagonalSliders, false);

		void UpdateSlideAttack(ulong pieceBoard, bool ortho)
		{
			var blockers = board.AllPiecesBitboard & ~(1ul << friendlyKingSquare);

			while (pieceBoard != 0)
			{
				var startSquare = BitBoardUtility.PopLSB(ref pieceBoard);
				var moveBoard = Magic.GetSliderAttacks(startSquare, blockers, ortho);

				opponentSlidingAttackMap |= moveBoard;
			}
		}
	}

	void CalculateAttackData()
	{
		GenSlidingAttackMap();
		// Search squares in all directions around friendly king for checks/pins by enemy sliding pieces (queen, rook, bishop)
		var startDirIndex = 0;
		var endDirIndex = 8;

		if (board.Queens[enemyIndex].Count == 0)
		{
			startDirIndex = (board.Rooks[enemyIndex].Count > 0) ? 0 : 4;
			endDirIndex = (board.Bishops[enemyIndex].Count > 0) ? 8 : 4;
		}

		for (var dir = startDirIndex; dir < endDirIndex; dir++)
		{
			var isDiagonal = dir > 3;
			var slider = isDiagonal ? board.EnemyDiagonalSliders : board.EnemyOrthogonalSliders;
			if ((dirRayMask[dir, friendlyKingSquare] & slider) == 0)
			{
				continue;
			}

			var n = numSquaresToEdge[friendlyKingSquare][dir];
			var directionOffset = directionOffsets[dir];
			var isFriendlyPieceAlongRay = false;
			ulong rayMask = 0;

			for (var i = 0; i < n; i++)
			{
				var squareIndex = friendlyKingSquare + directionOffset * (i + 1);
				rayMask |= 1ul << squareIndex;
				var piece = board.Square[squareIndex];

				// This square contains a piece
				if (piece != Piece.None)
				{
					if (Piece.IsColour(piece, friendlyColour))
					{
						// First friendly piece we have come across in this direction, so it might be pinned
						if (!isFriendlyPieceAlongRay)
						{
							isFriendlyPieceAlongRay = true;
						}
						// This is the second friendly piece we've found in this direction, therefore pin is not possible
						else
						{
							break;
						}
					}
					// This square contains an enemy piece
					else
					{
						var pieceType = Piece.PieceType(piece);

						// Check if piece is in bitmask of pieces able to move in current direction
						if (isDiagonal && Piece.IsDiagonalSlider(pieceType) || !isDiagonal && Piece.IsOrthogonalSlider(pieceType))
						{
							// Friendly piece blocks the check, so this is a pin
							if (isFriendlyPieceAlongRay)
							{
								pinRays |= rayMask;
							}
							// No friendly piece blocking the attack, so this is a check
							else
							{
								checkRayBitmask |= rayMask;
								inDoubleCheck = inCheck; // if already in check, then this is double check
								inCheck = true;
							}
							break;
						}
						else
						{
							// This enemy piece is not able to move in the current direction, and so is blocking any checks/pins
							break;
						}
					}
				}
			}
			// Stop searching for pins if in double check, as the king is the only piece able to move in that case anyway
			if (inDoubleCheck)
			{
				break;
			}
		}

		notPinRays = ~pinRays;

		ulong opponentKnightAttacks = 0;
		var knights = board.PieceBitboards[Piece.MakePiece(Piece.Knight, board.OpponentColour)];
		var friendlyKingBoard = board.PieceBitboards[Piece.MakePiece(Piece.King, board.MoveColour)];

		while (knights != 0)
		{
			var knightSquare = BitBoardUtility.PopLSB(ref knights);
			var knightAttacks = BitBoardUtility.KnightAttacks[knightSquare];
			opponentKnightAttacks |= knightAttacks;

			if ((knightAttacks & friendlyKingBoard) != 0)
			{
				inDoubleCheck = inCheck;
				inCheck = true;
				checkRayBitmask |= 1ul << knightSquare;
			}
		}

		// Pawn attacks
		var opponentPawns = board.Pawns[enemyIndex];
		opponentPawnAttackMap = 0;

		var opponentPawnsBoard = board.PieceBitboards[Piece.MakePiece(Piece.Pawn, board.OpponentColour)];
		opponentPawnAttackMap = BitBoardUtility.PawnAttacks(opponentPawnsBoard, !isWhiteToMove);
		if (BitBoardUtility.ContainsSquare(opponentPawnAttackMap, friendlyKingSquare))
		{
			inDoubleCheck = inCheck; // if already in check, then this is double check
			inCheck = true;
			var possiblePawnAttackOrigins = board.IsWhiteToMove ? BitBoardUtility.WhitePawnAttacks[friendlyKingSquare] : BitBoardUtility.BlackPawnAttacks[friendlyKingSquare];
			var pawnCheckMap = opponentPawnsBoard & possiblePawnAttackOrigins;
			checkRayBitmask |= pawnCheckMap;
		}

		var enemyKingSquare = board.KingSquare[enemyIndex];

		opponentAttackMapNoPawns = opponentSlidingAttackMap | opponentKnightAttacks | BitBoardUtility.KingMoves[enemyKingSquare];
		opponentAttackMap = opponentAttackMapNoPawns | opponentPawnAttackMap;

		if (!inCheck)
		{
			checkRayBitmask = ulong.MaxValue;
		}
	}

	// Test if capturing a pawn with en-passant reveals a sliding piece attack against the king
	// Note: this is only used for cases where pawn appears to not be pinned due to opponent pawn being on same rank
	// (therefore only need to check orthogonal sliders)
	bool InCheckAfterEnPassant(int startSquare, int targetSquare, int epCaptureSquare)
	{
		var enemyOrtho = board.EnemyOrthogonalSliders;

		if (enemyOrtho != 0)
		{
			var maskedBlockers = (allPieces ^ (1ul << epCaptureSquare | 1ul << startSquare | 1ul << targetSquare));
			var rookAttacks = Magic.GetRookAttacks(friendlyKingSquare, maskedBlockers);
			return (rookAttacks & enemyOrtho) != 0;
		}

		return false;
	}
}