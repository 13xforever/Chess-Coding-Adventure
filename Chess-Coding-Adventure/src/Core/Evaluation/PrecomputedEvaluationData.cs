namespace Chess.Core;

using System.Collections.Generic;

public static class PrecomputedEvaluationData
{

	public static readonly int[][] PawnShieldSquaresWhite;
	public static readonly int[][] PawnShieldSquaresBlack;

	static PrecomputedEvaluationData()
	{
		PawnShieldSquaresWhite = new int[64][];
		PawnShieldSquaresBlack = new int[64][];
		for (var squareIndex = 0; squareIndex < 64; squareIndex++)
		{
			CreatePawnShieldSquare(squareIndex);
		}
	}

	private static void CreatePawnShieldSquare(int squareIndex)
	{
		List<int> shieldIndicesWhite = new();
		List<int> shieldIndicesBlack = new();
		var coord = new Coord(squareIndex);
		var rank = coord.rankIndex;
		var file = System.Math.Clamp(coord.fileIndex, 1, 6);

		for (var fileOffset = -1; fileOffset <= 1; fileOffset++)
		{
			AddIfValid(new(file + fileOffset, rank + 1), shieldIndicesWhite);
			AddIfValid(new(file + fileOffset, rank - 1), shieldIndicesBlack);
		}

		for (var fileOffset = -1; fileOffset <= 1; fileOffset++)
		{
			AddIfValid(new(file + fileOffset, rank + 2), shieldIndicesWhite);
			AddIfValid(new(file + fileOffset, rank - 2), shieldIndicesBlack);
		}

		PawnShieldSquaresWhite[squareIndex] = shieldIndicesWhite.ToArray();
		PawnShieldSquaresBlack[squareIndex] = shieldIndicesBlack.ToArray();

		void AddIfValid(Coord coord, List<int> list)
		{
			if (coord.IsValidSquare())
			{
				list.Add(coord.SquareIndex);
			}
		}
	}
}