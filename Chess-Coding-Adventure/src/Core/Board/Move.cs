/*
Compact (16bit) move representation to preserve memory during search.

The format is as follows (ffffttttttssssss)
Bits 0-5: start square index
Bits 6-11: target square index
Bits 12-15: flag (promotion type, etc)
*/
namespace Chess.Core;

public readonly struct Move
{
	// 16bit move value
	readonly ushort moveValue;

	// Flags
	public const int NoFlag               = 0b_0000;
	public const int EnPassantCaptureFlag = 0b_0001;
	public const int CastleFlag           = 0b_0010;
	public const int PawnTwoUpFlag        = 0b_0011;
	public const int PromoteToQueenFlag   = 0b_0100;
	public const int PromoteToKnightFlag  = 0b_0101;
	public const int PromoteToRookFlag    = 0b_0110;
	public const int PromoteToBishopFlag  = 0b_0111;

	// Masks
	const ushort startSquareMask  = 0b_0000_000000_111111;
	const ushort targetSquareMask = 0b_0000_111111_000000;
	const ushort flagMask         = 0b_1111_000000_000000;

	public Move(ushort moveValue)
	{
		this.moveValue = moveValue;
	}

	public Move(int startSquare, int targetSquare)
	{
		moveValue = (ushort)(startSquare | targetSquare << 6);
	}

	public Move(int startSquare, int targetSquare, int flag)
	{
		moveValue = (ushort)(startSquare | targetSquare << 6 | flag << 12);
	}

	public ushort Value => moveValue;
	public bool IsNull => moveValue == 0;

	public int StartSquare => moveValue & startSquareMask;
	public int TargetSquare => (moveValue & targetSquareMask) >> 6;
	public bool IsPromotion => MoveFlag >= PromoteToQueenFlag;
	public int MoveFlag => moveValue >> 12;

	public int PromotionPieceType
	{
		get
		{
			switch (MoveFlag)
			{
				case PromoteToRookFlag:
					return Piece.Rook;
				case PromoteToKnightFlag:
					return Piece.Knight;
				case PromoteToBishopFlag:
					return Piece.Bishop;
				case PromoteToQueenFlag:
					return Piece.Queen;
				default:
					return Piece.None;
			}
		}
	}

	public static Move NullMove => new(0);
	public static bool SameMove(Move a, Move b) => a.moveValue == b.moveValue;
}