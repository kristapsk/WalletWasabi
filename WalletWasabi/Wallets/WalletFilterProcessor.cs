using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using WalletWasabi.Backend.Models;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.Mempool;
using WalletWasabi.Blockchain.TransactionProcessing;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Models;

namespace WalletWasabi.Wallets;

public class WalletFilterProcessor : BackgroundService
{
	private readonly Comparer<SyncRequestWithTaskCompletionSource> _comparer = Comparer<SyncRequestWithTaskCompletionSource>.Create(
		(x, y) =>
		{
			// Turbo and Complete have priority over NonTurbo.
			if (x.SyncRequest.SyncType != SyncType.NonTurbo && y.SyncRequest.SyncType == SyncType.NonTurbo)
			{
				return 1;
			}
		
			// Higher height have priority.
			if (y.SyncRequest.Filter.Header.Height > x.SyncRequest.Filter.Header.Height)
			{
				return 1;
			}
			if (x.SyncRequest.Filter.Header.Height > y.SyncRequest.Filter.Header.Height)
			{
				return -1;
			}
		
			return 0;
		});
	
	public WalletFilterProcessor(KeyManager keyManager, MempoolService mempoolService, TransactionProcessor transactionProcessor, IBlockProvider blockProvider)
	{
		SynchronizationRequests = new PriorityQueue<SyncRequestWithTaskCompletionSource, SyncRequestWithTaskCompletionSource>(_comparer);
		KeyManager = keyManager;
		MempoolService = mempoolService;
		TransactionProcessor = transactionProcessor;
		BlockProvider = blockProvider;
	}

	private PriorityQueue<SyncRequestWithTaskCompletionSource, SyncRequestWithTaskCompletionSource> SynchronizationRequests { get; }
	private SemaphoreSlim SynchronizationRequestsSemaphore { get; } = new(0);
	private object SynchronizationRequestsLock { get; } = new();
	private KeyManager KeyManager { get; }
	private MempoolService MempoolService { get; }
	private TransactionProcessor TransactionProcessor { get; }
	private IBlockProvider BlockProvider { get; }
	public FilterModel? LastProcessedFilter { get; private set; }

	private Task Add(SyncRequest request)
	{
		lock (SynchronizationRequestsLock)
		{
			var toInsertRequest = new SyncRequestWithTaskCompletionSource(request, new TaskCompletionSource());
			SynchronizationRequests.Enqueue(toInsertRequest, toInsertRequest);
			SynchronizationRequestsSemaphore.Release(1);
			return toInsertRequest.Task.Task;
		}
	}

	public async Task ProcessAsync(IEnumerable<SyncRequest> requests)
	{
		List<Task> tasks = new();
		foreach (var request in requests)
		{
			var task = Add(request);
			tasks.Add(task);
		}

		while (tasks.Count > 0)
		{
			var task = await Task.WhenAny(tasks).ConfigureAwait(false);
			tasks.Remove(task);
			await task; // This will re-throw an exception if the task failed.
		}
	}

	/// <inheritdoc />
	/// <summary>Used for filter synchronization.</summary>
	protected override async Task ExecuteAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			await SynchronizationRequestsSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

			SyncRequestWithTaskCompletionSource request;
			lock (SynchronizationRequestsLock)
			{
				if (SynchronizationRequests.Count == 0)
				{
					continue;
				}

				request = SynchronizationRequests.Dequeue();
			}
			try
			{
				await ProcessFilterModelAsync(request.SyncRequest, cancellationToken).ConfigureAwait(false);
				request.Task.SetResult();
			}
			catch (Exception ex)
			{
				request.Task.SetException(ex);
				throw;
			}
		}
	}

	/// <summary>
	/// Return the keys to test against the filter depending on the height of the filter and the type of synchronization.
	/// </summary>
	/// <param name="filterHeight">Height of the filter that needs to be tested.</param>
	/// <param name="syncType">First sync of TurboSync, second one, or complete synchronization.</param>
	/// <returns>Keys to test against this filter</returns>
	/// <seealso href="https://github.com/zkSNACKs/WalletWasabi/issues/10219">TurboSync specification.</seealso>
	private List<byte[]> GetScriptPubKeysToTest(Height filterHeight, SyncType syncType)
	{
		if (syncType == SyncType.Complete)
		{
			return KeyManager.UnsafeGetSynchronizationInfos().Select(x => x.ScriptBytesHdPubKeyPair.ScriptBytes).ToList();
		}

		Func<HdPubKey, bool> stepPredicate = syncType == SyncType.Turbo
			? hdPubKey => hdPubKey.LatestSpendingHeight is null || (Height)hdPubKey.LatestSpendingHeight >= filterHeight
			: hdPubKey => hdPubKey.LatestSpendingHeight is not null && (Height)hdPubKey.LatestSpendingHeight < filterHeight;

		IEnumerable<byte[]> keysToTest = KeyManager.UnsafeGetSynchronizationInfos()
			.Where(x => stepPredicate(x.ScriptBytesHdPubKeyPair.HdPubKey))
			.Select(x => x.ScriptBytesHdPubKeyPair.ScriptBytes);

		return keysToTest.ToList();
	}

	private async Task ProcessFilterModelAsync(SyncRequest request, CancellationToken cancel)
	{
		var height = new Height(request.Filter.Header.Height);
		var toTestKeys = GetScriptPubKeysToTest(height, request.SyncType);

		if (toTestKeys.Count == 0)
		{
			// No keys to test.
			LastProcessedFilter = request.Filter;
			return;
		}

		var matchFound = request.Filter.Filter.MatchAny(toTestKeys, request.Filter.FilterKey);
		if (matchFound)
		{
			Block currentBlock = await BlockProvider.GetBlockAsync(request.Filter.Header.BlockHash, cancel).ConfigureAwait(false); // Wait until not downloaded.

			var txsToProcess = new List<SmartTransaction>();
			for (int i = 0; i < currentBlock.Transactions.Count; i++)
			{
				Transaction tx = currentBlock.Transactions[i];
				txsToProcess.Add(new SmartTransaction(tx, height, currentBlock.GetHash(), i, firstSeen: currentBlock.Header.BlockTime, labels: MempoolService.TryGetLabel(tx.GetHash())));
			}

			TransactionProcessor.Process(txsToProcess);

			if (request.SyncType == SyncType.Turbo)
			{
				// Only keys in TurboSync subset (external + internal that didn't receive or fully spent coins) were tested, update TurboSyncHeight
				KeyManager.SetBestTurboSyncHeight(height);
			}
			else
			{
				// All keys were tested at this height, update the Height.
				KeyManager.SetBestHeight(height);
			}
		}

		LastProcessedFilter = request.Filter;
	}

	public record SyncRequest(SyncType SyncType, FilterModel Filter);
	private record SyncRequestWithTaskCompletionSource(SyncRequest SyncRequest, TaskCompletionSource Task);
}
