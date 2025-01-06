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
	public event Action<string>? OnMoveChosen;
	public event Action<string>? OnInfo;
	public bool IsThinking { get; private set; }
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

	public Bot()
	{
		board = Board.CreateBoard();
		searcher = new(board);
		searcher.OnSearchComplete += OnSearchComplete;
		searcher.OnInfo += s => OnInfo?.Invoke(s);

		book = new(Chess_Coding_Adventure.Properties.Resources.Book);
		searchWaitHandle = new(false);

		Task.Factory.StartNew(SearchThread, TaskCreationOptions.LongRunning);
	}

	public void NotifyNewGame()
	{
		searcher.ClearForNewPosition();
	}

	public void SetPosition(string fen)
	{
		board.LoadPosition(fen);
	}

	public void MakeMove(string moveString)
	{
		var move = MoveUtility.GetMoveFromUCIName(moveString, board);
		board.MakeMove(move);
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
		cancelSearchTimer?.Cancel();

		if (TryGetOpeningBookMove(out var bookMove))
		{
			LatestMoveIsBookMove = true;
			OnSearchComplete(bookMove);
		}
		else
		{
			StartSearch(timeMs);
		}
	}

	private void StartSearch(int timeMs)
	{
		currentSearchID++;
		searchWaitHandle.Set();
		cancelSearchTimer = new();
		Task.Delay(timeMs, cancelSearchTimer.Token).ContinueWith((t) => EndSearch(currentSearchID));
	}

	private void SearchThread()
	{
		while (!isQuitting)
		{
			searchWaitHandle.WaitOne();
			searcher.StartSearch();
		}
	}

	public void StopThinking()
	{
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
			searcher.EndSearch();
		}
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

	private void OnSearchComplete(Move move)
	{
		IsThinking = false;

		var moveName = MoveUtility.GetMoveNameUCI(move).Replace("=", "");

		OnMoveChosen?.Invoke(moveName);
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
