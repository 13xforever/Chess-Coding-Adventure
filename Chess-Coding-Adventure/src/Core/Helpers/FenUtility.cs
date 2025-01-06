using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Chess.Core;

// Helper class for dealing with FEN strings
public static class FenUtility
{
	public const string StartPositionFEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

	// Load position from fen string
	public static PositionInfo PositionFromFen(string fen)
	{

		PositionInfo loadedPositionInfo = new(fen);
		return loadedPositionInfo;
	}

	/// <summary>
	/// Get the fen string of the current position
	/// When alwaysIncludeEPSquare is true the en passant square will be included
	/// in the fen string even if no enemy pawn is in a position to capture it.
	/// </summary>
	public static string CurrentFen(Board board, bool alwaysIncludeEPSquare = true)
	{
		var fen = "";
		for (var rank = 7; rank >= 0; rank--)
		{
			var numEmptyFiles = 0;
			for (var file = 0; file < 8; file++)
			{
				var i = rank * 8 + file;
				var piece = board.Square[i];
				if (piece != 0)
				{
					if (numEmptyFiles != 0)
					{
						fen += numEmptyFiles;
						numEmptyFiles = 0;
					}
					var isBlack = Piece.IsColour(piece, Piece.Black);
					var pieceType = Piece.PieceType(piece);
					var pieceChar = ' ';
					switch (pieceType)
					{
						case Piece.Rook:
							pieceChar = 'R';
							break;
						case Piece.Knight:
							pieceChar = 'N';
							break;
						case Piece.Bishop:
							pieceChar = 'B';
							break;
						case Piece.Queen:
							pieceChar = 'Q';
							break;
						case Piece.King:
							pieceChar = 'K';
							break;
						case Piece.Pawn:
							pieceChar = 'P';
							break;
					}
					fen += (isBlack) ? pieceChar.ToString().ToLower() : pieceChar.ToString();
				}
				else
				{
					numEmptyFiles++;
				}

			}
			if (numEmptyFiles != 0)
			{
				fen += numEmptyFiles;
			}
			if (rank != 0)
			{
				fen += '/';
			}
		}

		// Side to move
		fen += ' ';
		fen += (board.IsWhiteToMove) ? 'w' : 'b';

		// Castling
		var whiteKingside = (board.CurrentGameState.castlingRights & 1) == 1;
		var whiteQueenside = (board.CurrentGameState.castlingRights >> 1 & 1) == 1;
		var blackKingside = (board.CurrentGameState.castlingRights >> 2 & 1) == 1;
		var blackQueenside = (board.CurrentGameState.castlingRights >> 3 & 1) == 1;
		fen += ' ';
		fen += (whiteKingside) ? "K" : "";
		fen += (whiteQueenside) ? "Q" : "";
		fen += (blackKingside) ? "k" : "";
		fen += (blackQueenside) ? "q" : "";
		fen += ((board.CurrentGameState.castlingRights) == 0) ? "-" : "";

		// En-passant
		fen += ' ';
		var epFileIndex = board.CurrentGameState.enPassantFile - 1;
		var epRankIndex = (board.IsWhiteToMove) ? 5 : 2;

		var isEnPassant = epFileIndex != -1;
		var includeEP = alwaysIncludeEPSquare || EnPassantCanBeCaptured(epFileIndex, epRankIndex, board);
		if (isEnPassant && includeEP)
		{
			fen += BoardHelper.SquareNameFromCoordinate(epFileIndex, epRankIndex);
		}
		else
		{
			fen += '-';
		}

		// 50 move counter
		fen += ' ';
		fen += board.CurrentGameState.fiftyMoveCounter;

		// Full-move count (should be one at start, and increase after each move by black)
		fen += ' ';
		fen += (board.PlyCount / 2) + 1;

		return fen;
	}

	static bool EnPassantCanBeCaptured(int epFileIndex, int epRankIndex, Board board)
	{
		var captureFromA = new Coord(epFileIndex - 1, epRankIndex + (board.IsWhiteToMove ? -1 : 1));
		var captureFromB = new Coord(epFileIndex + 1, epRankIndex + (board.IsWhiteToMove ? -1 : 1));
		var epCaptureSquare = new Coord(epFileIndex, epRankIndex).SquareIndex;
		var friendlyPawn = Piece.MakePiece(Piece.Pawn, board.MoveColour);



		return CanCapture(captureFromA) || CanCapture(captureFromB);


		bool CanCapture(Coord from)
		{
			var isPawnOnSquare = board.Square[from.SquareIndex] == friendlyPawn;
			if (from.IsValidSquare() && isPawnOnSquare)
			{
				var move = new Move(from.SquareIndex, epCaptureSquare, Move.EnPassantCaptureFlag);
				board.MakeMove(move);
				board.MakeNullMove();
				var wasLegalMove = !board.CalculateInCheckState();

				board.UnmakeNullMove();
				board.UnmakeMove(move);
				return wasLegalMove;
			}

			return false;
		}
	}

	public static string FlipFen(string fen)
	{
		var flippedFen = "";
		string[] sections = fen.Split(' ');

		List<char> invertedFenChars = new();
		string[] fenRanks = sections[0].Split('/');

		for (var i = fenRanks.Length - 1; i >= 0; i--)
		{
			var rank = fenRanks[i];
			foreach (var c in rank)
			{
				flippedFen += InvertCase(c);
			}
			if (i != 0)
			{
				flippedFen += '/';
			}
		}

		flippedFen += " " + (sections[1][0] == 'w' ? 'b' : 'w');
		var castlingRights = sections[2];
		var flippedRights = "";
		foreach (var c in "kqKQ")
		{
			if (castlingRights.Contains(c))
			{
				flippedRights += InvertCase(c);
			}
		}
		flippedFen += " " + (flippedRights.Length == 0 ? "-" : flippedRights);

		var ep = sections[3];
		var flippedEp = ep[0] + "";
		if (ep.Length > 1)
		{
			flippedEp += ep[1] == '6' ? '3' : '6';
		}
		flippedFen += " " + flippedEp;
		flippedFen += " " + sections[4] + " " + sections[5];


		return flippedFen;

		char InvertCase(char c)
		{
			if (char.IsLower(c))
			{
				return char.ToUpper(c);
			}
			return char.ToLower(c);
		}
	}

	public readonly struct PositionInfo
	{
		public readonly string fen;
		public readonly ReadOnlyCollection<int> squares;

		// Castling rights
		public readonly bool whiteCastleKingside;
		public readonly bool whiteCastleQueenside;
		public readonly bool blackCastleKingside;
		public readonly bool blackCastleQueenside;
		// En passant file (1 is a-file, 8 is h-file, 0 means none)
		public readonly int epFile;
		public readonly bool whiteToMove;
		// Number of half-moves since last capture or pawn advance
		// (starts at 0 and increments after each player's move)
		public readonly int fiftyMovePlyCount;
		// Total number of moves played in the game
		// (starts at 1 and increments after black's move)
		public readonly int moveCount;

		public PositionInfo(string fen)
		{
			this.fen = fen;
			var squarePieces = new int[64];

			string[] sections = fen.Split(' ');

			var file = 0;
			var rank = 7;

			foreach (var symbol in sections[0])
			{
				if (symbol == '/')
				{
					file = 0;
					rank--;
				}
				else
				{
					if (char.IsDigit(symbol))
					{
						file += (int)char.GetNumericValue(symbol);
					}
					else
					{
						var pieceColour = (char.IsUpper(symbol)) ? Piece.White : Piece.Black;
						var pieceType = char.ToLower(symbol) switch
						{
							'k' => Piece.King,
							'p' => Piece.Pawn,
							'n' => Piece.Knight,
							'b' => Piece.Bishop,
							'r' => Piece.Rook,
							'q' => Piece.Queen,
							_ => Piece.None
						};

						squarePieces[rank * 8 + file] = pieceType | pieceColour;
						file++;
					}
				}
			}

			squares = new(squarePieces);

			whiteToMove = (sections[1] == "w");

			var castlingRights = sections[2];
			whiteCastleKingside = castlingRights.Contains('K');
			whiteCastleQueenside = castlingRights.Contains('Q');
			blackCastleKingside = castlingRights.Contains('k');
			blackCastleQueenside = castlingRights.Contains('q');

			// Default values
			epFile = 0;
			fiftyMovePlyCount = 0;
			moveCount = 0;

			if (sections.Length > 3)
			{
				var enPassantFileName = sections[3][0].ToString();
				if (BoardHelper.fileNames.Contains(enPassantFileName))
				{
					epFile = BoardHelper.fileNames.IndexOf(enPassantFileName) + 1;
				}
			}

			// Half-move clock
			if (sections.Length > 4)
			{
				int.TryParse(sections[4], out fiftyMovePlyCount);
			}
			// Full move number
			if (sections.Length > 5)
			{
				int.TryParse(sections[5], out moveCount);
			}
		}
	}
}