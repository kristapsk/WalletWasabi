using System.Collections.Generic;
using System.Reactive.Linq;
using Avalonia.Input.Platform;
using DynamicData;
using Moq;
using NBitcoin;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Fluent;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.ViewModels.Navigation;
using WalletWasabi.Fluent.ViewModels.Wallets.Labels;
using WalletWasabi.Fluent.ViewModels.Wallets.Receive;
using WalletWasabi.Tests.UnitTests.ViewModels.TestDoubles;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.ViewModels;

public class ReceiveAddressViewModelTests
{
	[Fact]
	public void Copy_command_should_set_address_in_clipboard()
	{
		var clipboard = Mock.Of<IClipboard>(MockBehavior.Loose);
		var context = ContextWith(clipboard);
		var sut = new ReceiveAddressViewModel(new TestWallet(), new TestAddress("SomeAddress"), false, context);

		sut.CopyAddressCommand.Execute(null);

		var mock = Mock.Get(clipboard);
		mock.Verify(x => x.SetTextAsync("SomeAddress"));
	}

	[Fact]
	public void Auto_copy_enabled_should_copy_to_clipboard()
	{
		var clipboard = Mock.Of<IClipboard>(MockBehavior.Loose);
		var context = ContextWith(clipboard);
		new ReceiveAddressViewModel(new TestWallet(), new TestAddress("SomeAddress"), true, context);
		var mock = Mock.Get(clipboard);
		mock.Verify(x => x.SetTextAsync("SomeAddress"));
	}

	[Fact]
	public void When_address_becomes_used_navigation_goes_back()
	{
		var ns = Mock.Of<INavigationStack<RoutableViewModel>>(MockBehavior.Loose);
		var uiContext = ContextWith(new TestNavigation(ns));
		var address = new TestAddress("SomeAddress");
		var wallet = WalletWithAddresses(address);
		new ReceiveAddressViewModel(wallet, address, true, uiContext);

		address.IsUsed = true;

		Mock.Get(ns).Verify(x => x.Back(), Times.Once);
	}

	private static IWalletModel WalletWithAddresses(TestAddress address)
	{
		return Mock.Of<IWalletModel>(x => x.Addresses == AddressList(address).Connect(null).AutoRefresh(null, null, null));
	}

	private static UIContext ContextWith(INavigate navigation)
	{
		var uiContext = new UIContext(Mock.Of<IQrCodeGenerator>(x => x.Generate(It.IsAny<string>()) == Observable.Return(new bool[0, 0])), Mock.Of<IClipboard>());
		uiContext.RegisterNavigation(navigation);
		return uiContext;
	}

	private static UIContext ContextWith(IClipboard clipboard)
	{
		var contextWith = new UIContext(Mock.Of<IQrCodeGenerator>(x => x.Generate(It.IsAny<string>()) == Observable.Return(new bool[0, 0])), clipboard);
		contextWith.RegisterNavigation(Mock.Of<INavigate>());
		return contextWith;
	}

	private static ISourceCache<IAddress, string> AddressList(params IAddress[] addresses)
	{
		var cache = new SourceCache<IAddress, string>(s => s.Text);
		cache.PopulateFrom(addresses.ToObservable());
		return cache;
	}

	private class TestWallet : IWalletModel
	{
		public TestWallet()
		{
			Addresses = new SourceCache<IAddress, string>(address => address.Text).Connect();
		}

		public string Name { get; }
		public IObservable<IChangeSet<TransactionSummary, uint256>> Transactions { get; }

		public IObservable<Money> Balance { get; }
		public IObservable<IChangeSet<IAddress, string>> Addresses { get; }

		public IAddress GetNextReceiveAddress(IEnumerable<string> destinationLabels)
		{
			throw new NotImplementedException();
		}

		public IEnumerable<(string Label, int Score)> GetMostUsedLabels(Intent intent)
		{
			throw new NotImplementedException();
		}

		public bool IsHardwareWallet()
		{
			return false;
		}
	}

	private class TestNavigation : INavigate
	{
		private readonly INavigationStack<RoutableViewModel> _navigationStack;

		public TestNavigation(INavigationStack<RoutableViewModel> navigationStack)
		{
			_navigationStack = navigationStack;
		}

		public INavigationStack<RoutableViewModel> Navigate(NavigationTarget target)
		{
			return _navigationStack;
		}

		public FluentNavigate To()
		{
			throw new NotImplementedException();
		}
	}
}

