using System;

namespace Chess.Core;

public static class MoveUtility
{
	/// <summary>
	/// Converts a moveName into internal move representation
	/// Name is expected in format: "e2e4"
	/// Promotions can be written with or without equals sign, for example: "e7e8=q" or "e7e8q"
	/// </summary>
	public static Move GetMoveFromUCIName(string moveName, Board board)
	{
		var startSquare = BoardHelper.SquareIndexFromName(moveName[..2]);
		var targetSquare = BoardHelper.SquareIndexFromName(moveName.Substring(2, 2));

		var movedPieceType = Piece.PieceType(board.Square[startSquare]);
		Coord startCoord = new(startSquare);
		Coord targetCoord = new(targetSquare);

		// Figure out move flag
		var flag = Move.NoFlag;

		if (movedPieceType == Piece.Pawn)
		{
			// Promotion
			if (moveName.Length > 4)
			{
				flag = moveName[^1] switch
				{
					'q' => Move.PromoteToQueenFlag,
					'r' => Move.PromoteToRookFlag,
					'n' => Move.PromoteToKnightFlag,
					'b' => Move.PromoteToBishopFlag,
					_ => Move.NoFlag
				};
			}
			// Double pawn push
			else if (Math.Abs(targetCoord.rankIndex - startCoord.rankIndex) == 2)
			{
				flag = Move.PawnTwoUpFlag;
			}
			// En-passant
			else if (startCoord.fileIndex != targetCoord.fileIndex && board.Square[targetSquare] == Piece.None)
			{
				flag = Move.EnPassantCaptureFlag;
			}
		}
		else if (movedPieceType == Piece.King)
		{
			if (Math.Abs(startCoord.fileIndex - targetCoord.fileIndex) > 1)
			{
				flag = Move.CastleFlag;
			}
		}

		return new(startSquare, targetSquare, flag);
	}

	/// <summary>
	/// Get algebraic name of move (with promotion specified)
	/// Examples: "e2e4", "e7e8q"
	/// </summary>
	public static string GetMoveNameUCI(Move move)
	{
		var startSquareName = BoardHelper.SquareNameFromIndex(move.StartSquare);
		var endSquareName = BoardHelper.SquareNameFromIndex(move.TargetSquare);
		var moveName = startSquareName + endSquareName;
		if (move.IsPromotion)
		{
			switch (move.MoveFlag)
			{
				case Move.PromoteToRookFlag:
					moveName += "r";
					break;
				case Move.PromoteToKnightFlag:
					moveName += "n";
					break;
				case Move.PromoteToBishopFlag:
					moveName += "b";
					break;
				case Move.PromoteToQueenFlag:
					moveName += "q";
					break;
			}
		}
		return moveName;
	}

	/// <summary>
	/// Get name of move in Standard Algebraic Notation (SAN)
	/// Examples: "e4", "Bxf7+", "O-O", "Rh8#", "Nfd2"
	/// Note, the move must not yet have been made on the board
	/// </summary>
	public static string GetMoveNameSAN(Move move, Board board)
	{
		if (move.IsNull)
		{
			return "Null";
		}
		var movePieceType = Piece.PieceType(board.Square[move.StartSquare]);
		var capturedPieceType = Piece.PieceType(board.Square[move.TargetSquare]);

		if (move.MoveFlag == Move.CastleFlag)
		{
			var delta = move.TargetSquare - move.StartSquare;
			if (delta == 2)
			{
				return "O-O";
			}
			else if (delta == -2)
			{
				return "O-O-O";
			}
		}

		MoveGenerator moveGen = new();
		var moveNotation = char.ToUpper(Piece.GetSymbol(movePieceType)) + "";

		// check if any ambiguity exists in notation (e.g if e2 can be reached via Nfe2 and Nbe2)
		if (movePieceType != Piece.Pawn && movePieceType != Piece.King)
		{
			var allMoves = moveGen.GenerateMoves(board);

			foreach (var altMove in allMoves)
			{

				if (altMove.StartSquare != move.StartSquare && altMove.TargetSquare == move.TargetSquare)
				{ // if moving to same square from different square
					if (Piece.PieceType(board.Square[altMove.StartSquare]) == movePieceType)
					{ // same piece type
						var fromFileIndex = BoardHelper.FileIndex(move.StartSquare);
						var alternateFromFileIndex = BoardHelper.FileIndex(altMove.TargetSquare);
						var fromRankIndex = BoardHelper.RankIndex(move.StartSquare);
						var alternateFromRankIndex = BoardHelper.RankIndex(altMove.StartSquare);

						if (fromFileIndex != alternateFromFileIndex)
						{ // pieces on different files, thus ambiguity can be resolved by specifying file
							moveNotation += BoardHelper.fileNames[fromFileIndex];
							break; // ambiguity resolved
						}
						else if (fromRankIndex != alternateFromRankIndex)
						{
							moveNotation += BoardHelper.rankNames[fromRankIndex];
							break; // ambiguity resolved
						}
					}
				}
			}
		}

		if (capturedPieceType != 0)
		{
			// add 'x' to indicate capture
			if (movePieceType == Piece.Pawn)
			{
				moveNotation += BoardHelper.fileNames[BoardHelper.FileIndex(move.StartSquare)];
			}
			moveNotation += "x";
		}
		else
		{
			// check if capturing ep
			if (move.MoveFlag == Move.EnPassantCaptureFlag)
			{
				moveNotation += BoardHelper.fileNames[BoardHelper.FileIndex(move.StartSquare)] + "x";
			}
		}

		moveNotation += BoardHelper.fileNames[BoardHelper.FileIndex(move.TargetSquare)];
		moveNotation += BoardHelper.rankNames[BoardHelper.RankIndex(move.TargetSquare)];

		// add promotion piece
		if (move.IsPromotion)
		{
			var promotionPieceType = move.PromotionPieceType;
			moveNotation += "=" + char.ToUpper(Piece.GetSymbol(promotionPieceType));
		}

		board.MakeMove(move, inSearch: true);
		var legalResponses = moveGen.GenerateMoves(board);
		// add check/mate symbol if applicable
		if (moveGen.InCheck())
		{
			if (legalResponses.Length == 0)
			{
				moveNotation += "#";
			}
			else
			{
				moveNotation += "+";
			}
		}
		board.UnmakeMove(move, inSearch: true);

		return moveNotation;
	}

	/// <summary>
	/// Get move from the given name in SAN notation (e.g. "Nxf3", "Rad1", "O-O", etc.)
	/// The given board must contain the position from before the move was made
	/// </summary>
	public static Move GetMoveFromSAN(Board board, string algebraicMove)
	{
		MoveGenerator moveGenerator = new();

		// Remove unrequired info from move string
		algebraicMove = algebraicMove.Replace("+", "").Replace("#", "").Replace("x", "").Replace("-", "");
		var allMoves = moveGenerator.GenerateMoves(board);

		Move move = new();

		foreach (var moveToTest in allMoves)
		{
			move = moveToTest;

			var moveFromIndex = move.StartSquare;
			var moveToIndex = move.TargetSquare;
			var movePieceType = Piece.PieceType(board.Square[moveFromIndex]);
			var fromCoord = BoardHelper.CoordFromIndex(moveFromIndex);
			var toCoord = BoardHelper.CoordFromIndex(moveToIndex);
			if (algebraicMove == "OO")
			{ 
				// castle kingside
				if (movePieceType == Piece.King && moveToIndex - moveFromIndex == 2)
				{
					return move;
				}
			}
			else if (algebraicMove == "OOO")
			{ 
				// castle queenside
				if (movePieceType == Piece.King && moveToIndex - moveFromIndex == -2)
				{
					return move;
				}
			}
			// Is pawn move if starts with any file indicator (e.g. 'e'4. Note that uppercase B is used for bishops) 
			else if (BoardHelper.fileNames.Contains(algebraicMove[0].ToString()))
			{
				if (movePieceType != Piece.Pawn)
				{
					continue;
				}
				if (BoardHelper.fileNames.IndexOf(algebraicMove[0]) == fromCoord.fileIndex)
				{ 
					// correct starting file
					if (algebraicMove.Contains('='))
					{
						// is promotion
						if (toCoord.rankIndex == 0 || toCoord.rankIndex == 7)
						{

							if (algebraicMove.Length == 5) // pawn is capturing to promote
							{
								var targetFile = algebraicMove[1];
								if (BoardHelper.fileNames.IndexOf(targetFile) != toCoord.fileIndex)
								{
									// Skip if not moving to correct file
									continue;
								}
							}
							var promotionChar = algebraicMove[algebraicMove.Length - 1];

							if (move.PromotionPieceType != Piece.GetPieceTypeFromSymbol(promotionChar))
							{
								continue; // skip this move, incorrect promotion type
							}

							return move;
						}
					}
					else
					{
						var targetFile = algebraicMove[algebraicMove.Length - 2];
						var targetRank = algebraicMove[algebraicMove.Length - 1];

						if (BoardHelper.fileNames.IndexOf(targetFile) == toCoord.fileIndex)
						{ 
							// correct ending file
							if (targetRank.ToString() == (toCoord.rankIndex + 1).ToString())
							{ 
								// correct ending rank
								break;
							}
						}
					}
				}
			}
			else
			{
				// regular piece move
				var movePieceChar = algebraicMove[0];
				if (Piece.GetPieceTypeFromSymbol(movePieceChar) != movePieceType)
				{
					continue; // skip this move, incorrect move piece type
				}

				var targetFile = algebraicMove[algebraicMove.Length - 2];
				var targetRank = algebraicMove[algebraicMove.Length - 1];
				if (BoardHelper.fileNames.IndexOf(targetFile) == toCoord.fileIndex)
				{ 
					// correct ending file
					if (targetRank.ToString() == (toCoord.rankIndex + 1).ToString())
					{ 
						// correct ending rank
						if (algebraicMove.Length == 4)
						{ 
							// addition char present for disambiguation (e.g. Nbd7 or R7e2)
							var disambiguationChar = algebraicMove[1];

							if (BoardHelper.fileNames.Contains(disambiguationChar.ToString()))
							{ 
								// is file disambiguation
								if (BoardHelper.fileNames.IndexOf(disambiguationChar) != fromCoord.fileIndex)
								{ 
									// incorrect starting file
									continue;
								}
							}
							else
							{
								// is rank disambiguation
								if (disambiguationChar.ToString() != (fromCoord.rankIndex + 1).ToString())
								{
									// incorrect starting rank
									continue;
								}

							}
						}
						break;
					}
				}
			}
		}
		return move;
	}
}