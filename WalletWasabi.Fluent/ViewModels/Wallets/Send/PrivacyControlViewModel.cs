using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Send;

[NavigationMetaData(Title = "Privacy Control")]
public partial class PrivacyControlViewModel : DialogViewModelBase<IEnumerable<SmartCoin>>
{
	private readonly Wallet _wallet;
	private readonly bool _isSilent;
	private readonly IEnumerable<SmartCoin>? _usedCoins;

	public PrivacyControlViewModel(Wallet wallet, TransactionInfo transactionInfo, IEnumerable<SmartCoin>? usedCoins, bool isSilent, Money? targetAmount = null)
	{
		_wallet = wallet;
		_isSilent = isSilent;
		_usedCoins = usedCoins;

		LabelSelection = new LabelSelectionViewModel(targetAmount ?? transactionInfo.Amount);

		SetupCancel(enableCancel: false, enableCancelOnEscape: true, enableCancelOnPressed: false);
		EnableBack = true;

		NextCommand = ReactiveCommand.Create(() => Complete(LabelSelection.GetUsedPockets()), LabelSelection.WhenAnyValue(x => x.EnoughSelected));
	}

	public LabelSelectionViewModel LabelSelection { get; }

	private void Complete(IEnumerable<Pocket> pockets)
	{
		var coins = pockets.SelectMany(x => x.Coins);

		Close(DialogResultKind.Normal, coins);
	}

	private void InitializeLabels()
	{
		var privateThreshold = _wallet.KeyManager.MinAnonScoreTarget;

		LabelSelection.Reset(_wallet.Coins.GetPockets(privateThreshold).Select(x => new Pocket(x)).ToArray());
		LabelSelection.SetUsedLabel(_usedCoins, privateThreshold);
	}

	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
	{
		base.OnNavigatedTo(isInHistory, disposables);

		if (!isInHistory)
		{
			InitializeLabels();
		}

		Observable
			.FromEventPattern(_wallet.TransactionProcessor, nameof(Wallet.TransactionProcessor.WalletRelevantTransactionProcessed))
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(_ => InitializeLabels())
			.DisposeWith(disposables);

		if (_isSilent)
		{
			var autoSelectedPockets = LabelSelection.AutoSelectPockets();

			Complete(autoSelectedPockets);
		}
	}
}
