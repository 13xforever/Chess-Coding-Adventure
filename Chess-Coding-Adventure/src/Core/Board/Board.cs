using System;
using System.Collections.Generic;
using System.Threading;
using CodingAdventureBot;

namespace Chess.Core;
// Represents the current state of the board during a game.
// The state includes things such as: positions of all pieces, side to move,
// castling rights, en-passant square, etc. Some extra information is included
// as well to help with evaluation and move generation.

// The initial state of the board can be set from a FEN string, and moves are
// subsequently made (or undone) using the MakeMove and UnmakeMove functions.

public sealed class Board
{
	public const int WhiteIndex = 0;
	public const int BlackIndex = 1;

	// Stores piece code for each square on the board
	public readonly int[] Square;
	// Square index of white and black king
	public int[] KingSquare;
	// # Bitboards
	// Bitboard for each piece type and colour (white pawns, white knights, ... black pawns, etc.)
	public ulong[] PieceBitboards;
	// Bitboards for all pieces of either colour (all white pieces, all black pieces)
	public ulong[] ColourBitboards;
	public ulong AllPiecesBitboard;
	public ulong FriendlyOrthogonalSliders;
	public ulong FriendlyDiagonalSliders;
	public ulong EnemyOrthogonalSliders;
	public ulong EnemyDiagonalSliders;
	// Piece count excluding pawns and kings
	public int TotalPieceCountWithoutPawnsAndKings;
	// # Piece lists
	public PieceList[] Rooks;
	public PieceList[] Bishops;
	public PieceList[] Queens;
	public PieceList[] Knights;
	public PieceList[] Pawns;

	// # Side to move info
	public bool IsWhiteToMove;
	public int MoveColour => IsWhiteToMove ? Piece.White : Piece.Black;
	public int OpponentColour => IsWhiteToMove ? Piece.Black : Piece.White;
	public int MoveColourIndex => IsWhiteToMove ? WhiteIndex : BlackIndex;
	public int OpponentColourIndex => IsWhiteToMove ? BlackIndex : WhiteIndex;
	// List of (hashed) positions since last pawn move or capture (for detecting repetitions)
	public Stack<ulong> RepetitionPositionHistory;

	// Total plies (half-moves) played in game
	public int PlyCount;
	public int FiftyMoveCounter => CurrentGameState.fiftyMoveCounter;
	public GameState CurrentGameState;
	public ulong ZobristKey => CurrentGameState.zobristKey;
	public string CurrentFEN => FenUtility.CurrentFen(this);
	public string GameStartFEN => StartPositionInfo.fen;
	public List<Move> AllGameMoves;
	public readonly Lock Lock = new();

	// # Private stuff
	private PieceList[] allPieceLists;
	private Stack<GameState> gameStateHistory;
	private FenUtility.PositionInfo StartPositionInfo;
	private bool cachedInCheckValue;
	private bool hasCachedInCheckValue;

	public Board()
	{
		Square = new int[64];
	}

	// Make a move on the board
	// The inSearch parameter controls whether this move should be recorded in the game history.
	// (for detecting three-fold repetition)
	public void MakeMove(Move move, bool inSearch = false)
	{
		// Get info about move
		var startSquare = move.StartSquare;
		var targetSquare = move.TargetSquare;
		var moveFlag = move.MoveFlag;
		var isPromotion = move.IsPromotion;
		var isEnPassant = moveFlag is Move.EnPassantCaptureFlag;

		var movedPiece = Square[startSquare];
		var movedPieceType = Piece.PieceType(movedPiece);
		var capturedPiece = isEnPassant ? Piece.MakePiece(Piece.Pawn, OpponentColour) : Square[targetSquare];
		var capturedPieceType = Piece.PieceType(capturedPiece);

		var prevCastleState = CurrentGameState.castlingRights;
		var prevEnPassantFile = CurrentGameState.enPassantFile;
		var newZobristKey = CurrentGameState.zobristKey;
		var newCastlingRights = CurrentGameState.castlingRights;
		var newEnPassantFile = 0;

		// Update bitboard of moved piece (pawn promotion is a special case and is corrected later)
		MovePiece(movedPiece, startSquare, targetSquare);

		// Handle captures
		if (capturedPieceType != Piece.None)
		{
			var captureSquare = targetSquare;

			if (isEnPassant)
			{
				captureSquare = targetSquare + (IsWhiteToMove ? -8 : 8);
				Square[captureSquare] = Piece.None;
			}
			if (capturedPieceType != Piece.Pawn)
			{
				TotalPieceCountWithoutPawnsAndKings--;
			}

			// Remove captured piece from bitboards/piece list
			try
			{
				allPieceLists[capturedPiece].RemovePieceAtSquare(captureSquare);
			}
			catch (IndexOutOfRangeException e)
			{
				var boardLines = BoardHelper.CreateDiagram(this).Split(Environment.NewLine);
				var moveStr = MoveUtility.GetMoveNameUCI(move);
				EngineUCI.Respond($"info string Failed to capture {Piece.GetSymbol(capturedPiece)} with move {moveStr}");
				foreach (var line in boardLines)
					EngineUCI.Respond($"info string {line}");
				throw new InvalidOperationException(moveStr, e);
			}
			BitBoardUtility.ToggleSquare(ref PieceBitboards[capturedPiece], captureSquare);
			BitBoardUtility.ToggleSquare(ref ColourBitboards[OpponentColourIndex], captureSquare);
			newZobristKey ^= Zobrist.piecesArray[capturedPiece, captureSquare];
		}

		// Handle king
		if (movedPieceType == Piece.King)
		{
			KingSquare[MoveColourIndex] = targetSquare;
			newCastlingRights &= (IsWhiteToMove) ? 0b1100 : 0b0011;

			// Handle castling
			if (moveFlag == Move.CastleFlag)
			{
				var rookPiece = Piece.MakePiece(Piece.Rook, MoveColour);
				var kingside = targetSquare == BoardHelper.g1 || targetSquare == BoardHelper.g8;
				var castlingRookFromIndex = (kingside) ? targetSquare + 1 : targetSquare - 2;
				var castlingRookToIndex = (kingside) ? targetSquare - 1 : targetSquare + 1;

				// Update rook position
				BitBoardUtility.ToggleSquares(ref PieceBitboards[rookPiece], castlingRookFromIndex, castlingRookToIndex);
				BitBoardUtility.ToggleSquares(ref ColourBitboards[MoveColourIndex], castlingRookFromIndex, castlingRookToIndex);
				allPieceLists[rookPiece].MovePiece(castlingRookFromIndex, castlingRookToIndex);
				Square[castlingRookFromIndex] = Piece.None;
				Square[castlingRookToIndex] = Piece.Rook | MoveColour;

				newZobristKey ^= Zobrist.piecesArray[rookPiece, castlingRookFromIndex];
				newZobristKey ^= Zobrist.piecesArray[rookPiece, castlingRookToIndex];
			}
		}

		// Handle promotion
		if (isPromotion)
		{
			TotalPieceCountWithoutPawnsAndKings++;
			var promotionPieceType = moveFlag switch
			{
				Move.PromoteToQueenFlag => Piece.Queen,
				Move.PromoteToRookFlag => Piece.Rook,
				Move.PromoteToKnightFlag => Piece.Knight,
				Move.PromoteToBishopFlag => Piece.Bishop,
				_ => 0
			};

			var promotionPiece = Piece.MakePiece(promotionPieceType, MoveColour);

			// Remove pawn from promotion square and add promoted piece instead
			BitBoardUtility.ToggleSquare(ref PieceBitboards[movedPiece], targetSquare);
			BitBoardUtility.ToggleSquare(ref PieceBitboards[promotionPiece], targetSquare);
			allPieceLists[movedPiece].RemovePieceAtSquare(targetSquare);
			allPieceLists[promotionPiece].AddPieceAtSquare(targetSquare);
			Square[targetSquare] = promotionPiece;
		}

		// Pawn has moved two forwards, mark file with en-passant flag
		if (moveFlag == Move.PawnTwoUpFlag)
		{
			var file = BoardHelper.FileIndex(startSquare) + 1;
			newEnPassantFile = file;
			newZobristKey ^= Zobrist.enPassantFile[file];
		}

		// Update castling rights
		if (prevCastleState != 0)
		{
			// Any piece moving to/from rook square removes castling right for that side
			if (targetSquare == BoardHelper.h1 || startSquare == BoardHelper.h1)
			{
				newCastlingRights &= GameState.ClearWhiteKingsideMask;
			}
			else if (targetSquare == BoardHelper.a1 || startSquare == BoardHelper.a1)
			{
				newCastlingRights &= GameState.ClearWhiteQueensideMask;
			}
			if (targetSquare == BoardHelper.h8 || startSquare == BoardHelper.h8)
			{
				newCastlingRights &= GameState.ClearBlackKingsideMask;
			}
			else if (targetSquare == BoardHelper.a8 || startSquare == BoardHelper.a8)
			{
				newCastlingRights &= GameState.ClearBlackQueensideMask;
			}
		}

		// Update zobrist key with new piece position and side to move
		newZobristKey ^= Zobrist.sideToMove;
		newZobristKey ^= Zobrist.piecesArray[movedPiece, startSquare];
		newZobristKey ^= Zobrist.piecesArray[Square[targetSquare], targetSquare];
		newZobristKey ^= Zobrist.enPassantFile[prevEnPassantFile];

		if (newCastlingRights != prevCastleState)
		{
			newZobristKey ^= Zobrist.castlingRights[prevCastleState]; // remove old castling rights state
			newZobristKey ^= Zobrist.castlingRights[newCastlingRights]; // add new castling rights state
		}

		// Change side to move
		IsWhiteToMove = !IsWhiteToMove;

		PlyCount++;
		var newFiftyMoveCounter = CurrentGameState.fiftyMoveCounter + 1;

		// Update extra bitboards
		AllPiecesBitboard = ColourBitboards[WhiteIndex] | ColourBitboards[BlackIndex];
		UpdateSliderBitboards();

		// Pawn moves and captures reset the fifty move counter and clear 3-fold repetition history
		if (movedPieceType == Piece.Pawn || capturedPieceType != Piece.None)
		{
			if (!inSearch)
			{
				RepetitionPositionHistory.Clear();
			}
			newFiftyMoveCounter = 0;
		}

		GameState newState = new(capturedPieceType, newEnPassantFile, newCastlingRights, newFiftyMoveCounter, newZobristKey);
		gameStateHistory.Push(newState);
		CurrentGameState = newState;
		hasCachedInCheckValue = false;

		if (!inSearch)
		{
			RepetitionPositionHistory.Push(newState.zobristKey);
			AllGameMoves.Add(move);
		}
	}

	// Undo a move previously made on the board
	public void UnmakeMove(Move move, bool inSearch = false)
	{
		// Swap colour to move
		IsWhiteToMove = !IsWhiteToMove;

		var undoingWhiteMove = IsWhiteToMove;

		// Get move info
		var movedFrom = move.StartSquare;
		var movedTo = move.TargetSquare;
		var moveFlag = move.MoveFlag;

		var undoingEnPassant = moveFlag == Move.EnPassantCaptureFlag;
		var undoingPromotion = move.IsPromotion;
		var undoingCapture = CurrentGameState.capturedPieceType != Piece.None;

		var movedPiece = undoingPromotion ? Piece.MakePiece(Piece.Pawn, MoveColour) : Square[movedTo];
		var movedPieceType = Piece.PieceType(movedPiece);
		var capturedPieceType = CurrentGameState.capturedPieceType;

		// If undoing promotion, then remove piece from promotion square and replace with pawn
		if (undoingPromotion)
		{
			var promotedPiece = Square[movedTo];
			var pawnPiece = Piece.MakePiece(Piece.Pawn, MoveColour);
			TotalPieceCountWithoutPawnsAndKings--;

			allPieceLists[promotedPiece].RemovePieceAtSquare(movedTo);
			allPieceLists[movedPiece].AddPieceAtSquare(movedTo);
			BitBoardUtility.ToggleSquare(ref PieceBitboards[promotedPiece], movedTo);
			BitBoardUtility.ToggleSquare(ref PieceBitboards[pawnPiece], movedTo);
		}

		MovePiece(movedPiece, movedTo, movedFrom);

		// Undo capture
		if (undoingCapture)
		{
			var captureSquare = movedTo;
			var capturedPiece = Piece.MakePiece(capturedPieceType, OpponentColour);

			if (undoingEnPassant)
			{
				captureSquare = movedTo + ((undoingWhiteMove) ? -8 : 8);
			}
			if (capturedPieceType != Piece.Pawn)
			{
				TotalPieceCountWithoutPawnsAndKings++;
			}

			// Add back captured piece
			BitBoardUtility.ToggleSquare(ref PieceBitboards[capturedPiece], captureSquare);
			BitBoardUtility.ToggleSquare(ref ColourBitboards[OpponentColourIndex], captureSquare);
			allPieceLists[capturedPiece].AddPieceAtSquare(captureSquare);
			Square[captureSquare] = capturedPiece;
		}


		// Update king
		if (movedPieceType is Piece.King)
		{
			KingSquare[MoveColourIndex] = movedFrom;

			// Undo castling
			if (moveFlag is Move.CastleFlag)
			{
				var rookPiece = Piece.MakePiece(Piece.Rook, MoveColour);
				var kingside = movedTo == BoardHelper.g1 || movedTo == BoardHelper.g8;
				var rookSquareBeforeCastling = kingside ? movedTo + 1 : movedTo - 2;
				var rookSquareAfterCastling = kingside ? movedTo - 1 : movedTo + 1;

				// Undo castling by returning rook to original square
				BitBoardUtility.ToggleSquares(ref PieceBitboards[rookPiece], rookSquareAfterCastling, rookSquareBeforeCastling);
				BitBoardUtility.ToggleSquares(ref ColourBitboards[MoveColourIndex], rookSquareAfterCastling, rookSquareBeforeCastling);
				Square[rookSquareAfterCastling] = Piece.None;
				Square[rookSquareBeforeCastling] = rookPiece;
				allPieceLists[rookPiece].MovePiece(rookSquareAfterCastling, rookSquareBeforeCastling);
			}
		}

		AllPiecesBitboard = ColourBitboards[WhiteIndex] | ColourBitboards[BlackIndex];
		UpdateSliderBitboards();

		if (!inSearch && RepetitionPositionHistory.Count > 0)
		{
			RepetitionPositionHistory.Pop();
		}
		if (!inSearch)
		{
			AllGameMoves.RemoveAt(AllGameMoves.Count - 1);
		}

		// Go back to previous state
		gameStateHistory.Pop();
		CurrentGameState = gameStateHistory.Peek();
		PlyCount--;
		hasCachedInCheckValue = false;
	}

	// Switch side to play without making a move (NOTE: must not be in check when called)
	public void MakeNullMove()
	{
		IsWhiteToMove = !IsWhiteToMove;

		PlyCount++;

		var newZobristKey = CurrentGameState.zobristKey;
		newZobristKey ^= Zobrist.sideToMove;
		newZobristKey ^= Zobrist.enPassantFile[CurrentGameState.enPassantFile];

		GameState newState = new(Piece.None, 0, CurrentGameState.castlingRights, CurrentGameState.fiftyMoveCounter + 1, newZobristKey);
		CurrentGameState = newState;
		gameStateHistory.Push(CurrentGameState);
		UpdateSliderBitboards();
		hasCachedInCheckValue = true;
		cachedInCheckValue = false;
	}

	public void UnmakeNullMove()
	{
		IsWhiteToMove = !IsWhiteToMove;
		PlyCount--;
		gameStateHistory.Pop();
		CurrentGameState = gameStateHistory.Peek();
		UpdateSliderBitboards();
		hasCachedInCheckValue = true;
		cachedInCheckValue = false;
	}

	// Is current player in check?
	// Note: caches check value so calling multiple times does not require recalculating
	public bool IsInCheck()
	{
		if (hasCachedInCheckValue)
		{
			return cachedInCheckValue;
		}
		cachedInCheckValue = CalculateInCheckState();
		hasCachedInCheckValue = true;

		return cachedInCheckValue;
	}

	// Calculate in check value
	// Call IsInCheck instead for automatic caching of value
	public bool CalculateInCheckState()
	{
		var kingSquare = KingSquare[MoveColourIndex];
		var blockers = AllPiecesBitboard;

		if (EnemyOrthogonalSliders != 0)
		{
			var rookAttacks = Magic.GetRookAttacks(kingSquare, blockers);
			if ((rookAttacks & EnemyOrthogonalSliders) != 0)
			{
				return true;
			}
		}
		if (EnemyDiagonalSliders != 0)
		{
			var bishopAttacks = Magic.GetBishopAttacks(kingSquare, blockers);
			if ((bishopAttacks & EnemyDiagonalSliders) != 0)
			{
				return true;
			}
		}

		var enemyKnights = PieceBitboards[Piece.MakePiece(Piece.Knight, OpponentColour)];
		if ((BitBoardUtility.KnightAttacks[kingSquare] & enemyKnights) != 0)
		{
			return true;
		}

		var enemyPawns = PieceBitboards[Piece.MakePiece(Piece.Pawn, OpponentColour)];
		var pawnAttackMask = IsWhiteToMove ? BitBoardUtility.WhitePawnAttacks[kingSquare] : BitBoardUtility.BlackPawnAttacks[kingSquare];
		if ((pawnAttackMask & enemyPawns) != 0)
		{
			return true;
		}

		return false;
	}


	// Load the starting position
	public void LoadStartPosition()
	{
		LoadPosition(FenUtility.StartPositionFEN);
	}

	public void LoadPosition(string fen)
	{
		var posInfo = FenUtility.PositionFromFen(fen);
		LoadPosition(posInfo);
	}

	public void LoadPosition(FenUtility.PositionInfo posInfo)
	{
		lock (Lock)
		{
			StartPositionInfo = posInfo;
			Initialize();

			// Load pieces into board array and piece lists
			for (var squareIndex = 0; squareIndex < 64; squareIndex++)
			{
				var piece = posInfo.squares[squareIndex];
				var pieceType = Piece.PieceType(piece);
				var colourIndex = Piece.IsWhite(piece) ? WhiteIndex : BlackIndex;
				Square[squareIndex] = piece;

				if (piece != Piece.None)
				{
					BitBoardUtility.SetSquare(ref PieceBitboards[piece], squareIndex);
					BitBoardUtility.SetSquare(ref ColourBitboards[colourIndex], squareIndex);

					if (pieceType == Piece.King)
					{
						KingSquare[colourIndex] = squareIndex;
					}
					else
					{
						allPieceLists[piece].AddPieceAtSquare(squareIndex);
					}
					TotalPieceCountWithoutPawnsAndKings += pieceType is Piece.Pawn or Piece.King ? 0 : 1;
				}
			}

			// Side to move
			IsWhiteToMove = posInfo.whiteToMove;

			// Set extra bitboards
			AllPiecesBitboard = ColourBitboards[WhiteIndex] | ColourBitboards[BlackIndex];
			UpdateSliderBitboards();

			// Create gamestate
			var whiteCastle = (posInfo.whiteCastleKingside ? 1 << 0 : 0)
			                  | (posInfo.whiteCastleQueenside ? 1 << 1 : 0);
			var blackCastle = (posInfo.blackCastleKingside ? 1 << 2 : 0)
			                  | (posInfo.blackCastleQueenside ? 1 << 3 : 0);
			var castlingRights = whiteCastle | blackCastle;

			PlyCount = (posInfo.moveCount - 1) * 2 + (IsWhiteToMove ? 0 : 1);

			// Set game state (note: calculating zobrist key relies on current game state)
			CurrentGameState = new(Piece.None, posInfo.epFile, castlingRights, posInfo.fiftyMovePlyCount, 0);
			var zobristKey = Zobrist.CalculateZobristKey(this);
			CurrentGameState = new(Piece.None, posInfo.epFile, castlingRights, posInfo.fiftyMovePlyCount, zobristKey);

			RepetitionPositionHistory.Push(zobristKey);

			gameStateHistory.Push(CurrentGameState);
		}
	}

	public override string ToString()
	{
		return BoardHelper.CreateDiagram(this, IsWhiteToMove);
	}

	public static Board CreateBoard(string fen = FenUtility.StartPositionFEN)
	{
		Board board = new();
		board.LoadPosition(fen);
		return board;
	}

	public static Board CreateBoard(Board source)
	{
		Board board = new();
		board.LoadPosition(source.StartPositionInfo);

		for (var i = 0; i < source.AllGameMoves.Count; i++)
		{
			board.MakeMove(source.AllGameMoves[i]);
		}
		return board;
	}

	// Update piece lists / bitboards based on given move info.
	// Note that this does not account for the following things, which must be handled separately:
	// 1. Removal of a captured piece
	// 2. Movement of rook when castling
	// 3. Removal of pawn from 1st/8th rank during pawn promotion
	// 4. Addition of promoted piece during pawn promotion
	private void MovePiece(int piece, int startSquare, int targetSquare)
	{
		BitBoardUtility.ToggleSquares(ref PieceBitboards[piece], startSquare, targetSquare);
		BitBoardUtility.ToggleSquares(ref ColourBitboards[MoveColourIndex], startSquare, targetSquare);

		allPieceLists[piece].MovePiece(startSquare, targetSquare);
		Square[startSquare] = Piece.None;
		Square[targetSquare] = piece;
	}

	private void UpdateSliderBitboards()
	{
		var friendlyRook = Piece.MakePiece(Piece.Rook, MoveColour);
		var friendlyQueen = Piece.MakePiece(Piece.Queen, MoveColour);
		var friendlyBishop = Piece.MakePiece(Piece.Bishop, MoveColour);
		FriendlyOrthogonalSliders = PieceBitboards[friendlyRook] | PieceBitboards[friendlyQueen];
		FriendlyDiagonalSliders = PieceBitboards[friendlyBishop] | PieceBitboards[friendlyQueen];

		var enemyRook = Piece.MakePiece(Piece.Rook, OpponentColour);
		var enemyQueen = Piece.MakePiece(Piece.Queen, OpponentColour);
		var enemyBishop = Piece.MakePiece(Piece.Bishop, OpponentColour);
		EnemyOrthogonalSliders = PieceBitboards[enemyRook] | PieceBitboards[enemyQueen];
		EnemyDiagonalSliders = PieceBitboards[enemyBishop] | PieceBitboards[enemyQueen];
	}

	private void Initialize()
	{
		AllGameMoves = [];
		KingSquare = new int[2];
		Array.Clear(Square);

		RepetitionPositionHistory = new(capacity: 64);
		gameStateHistory = new(capacity: 64);

		CurrentGameState = new();
		PlyCount = 0;

		Knights = [new(10), new(10)];
		Pawns = [new(8), new(8)];
		Rooks = [new(10), new(10)];
		Bishops = [new(10), new(10)];
		Queens = [new(9), new(9)];

		allPieceLists = new PieceList[Piece.MaxPieceIndex + 1];
		allPieceLists[Piece.WhitePawn] = Pawns[WhiteIndex];
		allPieceLists[Piece.WhiteKnight] = Knights[WhiteIndex];
		allPieceLists[Piece.WhiteBishop] = Bishops[WhiteIndex];
		allPieceLists[Piece.WhiteRook] = Rooks[WhiteIndex];
		allPieceLists[Piece.WhiteQueen] = Queens[WhiteIndex];
		allPieceLists[Piece.WhiteKing] = new(1);

		allPieceLists[Piece.BlackPawn] = Pawns[BlackIndex];
		allPieceLists[Piece.BlackKnight] = Knights[BlackIndex];
		allPieceLists[Piece.BlackBishop] = Bishops[BlackIndex];
		allPieceLists[Piece.BlackRook] = Rooks[BlackIndex];
		allPieceLists[Piece.BlackQueen] = Queens[BlackIndex];
		allPieceLists[Piece.BlackKing] = new(1);

		TotalPieceCountWithoutPawnsAndKings = 0;

		// Initialize bitboards
		PieceBitboards = new ulong[Piece.MaxPieceIndex + 1];
		ColourBitboards = new ulong[2];
		AllPiecesBitboard = 0;
	}
}