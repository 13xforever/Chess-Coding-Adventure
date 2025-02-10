using Chess.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static System.Math;

namespace CodingAdventureBot;

public class Bot
{
	// # Settings
	private const bool useOpeningBook = true;

	private const int maxBookPly = 16;
	// Limit the amount of time the bot can spend per move (mainly for
	// games against human opponents, so not boring to play against).
	private const bool useMaxThinkTime = false;
	private const int maxThinkTimeMs = 2500;

	// Public stuff
	public event Action<string, string?>? OnMoveChosen;
	public event Action<string>? OnInfo;
	public bool IsThinking { get; private set; }
	public bool IsPondering { get; private set; }
	public bool IsPonderHit { get; private set; }
	public bool LatestMoveIsBookMove { get; private set; }

	// References
	private readonly Searcher searcher;
	private readonly Board board;
	private readonly OpeningBook book;
	private readonly AutoResetEvent searchWaitHandle;
	private CancellationTokenSource? cancelSearchTimer;

	// State
	private int currentSearchID;
	private bool isQuitting;
	private Move lastMove = Move.NullMove;
	private int thinkTimeAfterPonder;
	private string boardPositionForPondering;
	private readonly Lock searcherLock = new();

	public Bot(int hashSize, bool canPonder)
	{
		board = Board.CreateBoard();
		searcher = new(board, hashSize, canPonder);
		searcher.OnSearchComplete += OnSearchComplete;
		searcher.OnInfo += s => OnInfo?.Invoke(s);

		book = new(Chess_Coding_Adventure.Properties.Resources.Book);
		searchWaitHandle = new(false);

		Task.Factory.StartNew(SearchThread, TaskCreationOptions.LongRunning);
	}

	public void NotifyNewGame()
	{
		lock(searcherLock) searcher.ClearForNewPosition();
	}

	public void SetPosition(string fen)
	{
		board.LoadPosition(fen);
	}

	public void MakeMove(string moveString)
	{
		lastMove = MoveUtility.GetMoveFromUCIName(moveString, board);
		board.MakeMove(lastMove);
	}

	public int ChooseThinkTime(int timeRemainingWhiteMs, int timeRemainingBlackMs, int incrementWhiteMs, int incrementBlackMs)
	{
		var myTimeRemainingMs = board.IsWhiteToMove ? timeRemainingWhiteMs : timeRemainingBlackMs;
		var myIncrementMs = board.IsWhiteToMove ? incrementWhiteMs : incrementBlackMs;
		// Get a fraction of remaining time to use for current move
		var thinkTimeMs = myTimeRemainingMs / 40.0;
		// Clamp think time if a maximum limit is imposed
		if (useMaxThinkTime)
		{
			thinkTimeMs = Min(maxThinkTimeMs, thinkTimeMs);
		}
		// Add increment
		if (myTimeRemainingMs > myIncrementMs * 2)
		{
			thinkTimeMs += myIncrementMs * 0.8;
		}

		var minThinkTime = Min(50, myTimeRemainingMs * 0.25);
		return (int)Ceiling(Max(minThinkTime, thinkTimeMs));
	}

	public void ThinkTimed(int timeMs)
	{
		LatestMoveIsBookMove = false;
		IsThinking = true;
		lastMove = Move.NullMove;
		cancelSearchTimer?.Cancel();
		IsPondering = false;
		IsPonderHit = false;
		if (TryGetOpeningBookMove(out var bookMove))
		{
			LatestMoveIsBookMove = true;
			OnSearchComplete(bookMove, Move.NullMove);
		}
		else
		{
			StartSearch(timeMs);
		}
	}

	public void Ponder(int thinkTimeMs)
	{
		board.UnmakeMove(lastMove);
		boardPositionForPondering = board.CurrentFEN;
		thinkTimeAfterPonder = thinkTimeMs;
		searcher.PonderMove = lastMove;
		LatestMoveIsBookMove = false;
		IsThinking = true;
		IsPondering = true;
		cancelSearchTimer?.Cancel();
		StartSearch(-1);
	}
	
	public void PonderHit()
	{
		IsPonderHit = true;
		IsPondering = false;
		board.LoadPosition(boardPositionForPondering);
		board.MakeMove(lastMove);
		ThinkTimed(thinkTimeAfterPonder);
	}

	private void StartSearch(int timeMs)
	{
		lock (searcherLock)
		{
			searcher.IsPondering = IsPondering;
			currentSearchID++;
			searchWaitHandle.Set();
			cancelSearchTimer?.Dispose();
			cancelSearchTimer = new();
			Task.Delay(timeMs, cancelSearchTimer.Token).ContinueWith((t) => EndSearch(currentSearchID));
		}
	}

	private void SearchThread()
	{
		while (!isQuitting)
		{
			searchWaitHandle.WaitOne();
			searcher.StartSearch();
		}
	}

	public void StopThinking(bool isPonderhit)
	{
		IsPonderHit = isPonderhit;
		EndSearch();
	}

	public void Quit()
	{
		isQuitting = true;
		EndSearch();
	}

	public string GetBoardDiagram() => board.ToString();

	private void EndSearch()
	{
		cancelSearchTimer?.Cancel();
		if (IsThinking)
		{
			lock(searcherLock) searcher.EndSearch();
		}
		searcher.searchSemaphore.Wait();
		searcher.searchSemaphore.Release();
	}

	private void EndSearch(int searchID)
	{
		// If search timer has been cancelled, the search will have been stopped already
		if (cancelSearchTimer is { IsCancellationRequested: true })
		{
			return;
		}
		
		if (currentSearchID == searchID)
		{
			EndSearch();
		}
	}

	private void OnSearchComplete(Move move, Move ponderMove)
	{
		IsThinking = false;
		if (IsPonderHit)
			return;
		
		var moveName = MoveUtility.GetMoveNameUCI(move);
		string? ponderName = null;
		if (!ponderMove.IsNull)
			ponderName = MoveUtility.GetMoveNameUCI(ponderMove);
		OnMoveChosen?.Invoke(moveName, ponderName);
	}

	private bool TryGetOpeningBookMove(out Move bookMove)
	{
		if (useOpeningBook && board.PlyCount <= maxBookPly && book.TryGetBookMove(board, out var moveString))
		{
			bookMove = MoveUtility.GetMoveFromUCIName(moveString, board);
			return true;
		}
		bookMove = Move.NullMove;
		return false;
	}

	public static string GetResourcePath(params string[] localPath)
	{
		return Path.Combine(Directory.GetCurrentDirectory(), "resources", Path.Combine(localPath));
	}

	public static string ReadResourceFile(string localPath)
	{
		return File.ReadAllText(GetResourcePath(localPath));
	}
}
