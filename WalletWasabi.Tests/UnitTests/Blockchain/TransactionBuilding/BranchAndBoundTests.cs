using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Blockchain.TransactionBuilding.BnB;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.Blockchain.TransactionBuilding;

/// <summary>
/// Tests for <see cref="BranchAndBound"/> class.
/// </summary>
public class BranchAndBoundTests
{
	/// <summary>Tests that a best found selection is found when an exact solution does not exist.</summary>
	[Fact]
	public void MoreSelection_NoInputCosts()
	{
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

		long[] inputValues = new long[] { 11, 7, 5, 3, 2 }; // Sum is 28.
		long[] inputCosts = new long[] { 0, 0, 0, 0, 0 }; // No input costs. Idealized.
		long target = 27; // Target that we cannot get as a sum of input values.

		BranchAndBound algorithm = new();
		MoreSelectionStrategy strategy = new(target, inputValues, inputCosts);
		_ = algorithm.TryGetMatch(strategy, out _, cts.Token);

		Assert.Equal(new long[] { 11, 7, 5, 3, 2 }, strategy.GetBestSelectionFound());
	}

	[Fact]
	public void MoreSelection_ExactMatchIsAlsoCheapest()
	{
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

		long[] inputValues = new long[] { 35, 17, 10, 5, 3, 2 };
		long[] inputCosts = new long[] { 1, 5, 1, 1, 1, 1 };

		long target = 27;

		BranchAndBound algorithm = new();
		MoreSelectionStrategy strategy = new(target, inputValues, inputCosts);
		_ = algorithm.TryGetMatch(strategy, out _, cts.Token);

		long[] actualSelection = strategy.GetBestSelectionFound()!;
		Assert.NotNull(actualSelection);

		Assert.Equal(new long[] { 17, 10 }, actualSelection);
	}

	/// <summary>
	/// Effective bitcoin value received by the payee is more important than
	/// payer's total costs.
	/// </summary>
	[Fact]
	public void MoreSelection_ExactMatchIsMoreExpensiveButStillSelected()
	{
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

		long[] inputValues = new long[] { 35, 17, 10, 5, 3, 2 };

		// Make the second input very expensive to spend so that it is not selected (not likely in reality).
		long[] inputCosts = new long[] { 1, 10, 1, 1, 1, 1 };

		long target = 27; // Target that we cannot get as a sum of input values.

		BranchAndBound algorithm = new();
		MoreSelectionStrategy strategy = new(target, inputValues, inputCosts);
		_ = algorithm.TryGetMatch(strategy, out _, cts.Token);

		// Selection (35) costs us 35 + 1 = 36.
		// Selection (17, 10) is actually more expensive: (17 + 10) + (10 + 1) = 38, but
		// we use that selection sa 27 is exactly the amount a payee expects.
		long[] actualSelection = strategy.GetBestSelectionFound()!;
		Assert.NotNull(actualSelection);

		Assert.Equal(new long[] { 17, 10 }, actualSelection);
	}

	/// <summary>
	/// The goal of the test is to verify that we prune branches at points when it is clear that
	/// the sum of remaining coins is less than what we need to hit our target.
	/// </summary>
	/// <remarks>
	/// This is especially important for cases where user has many coins.
	/// <para>If the optimization is not in place, you should observe that this test does not really finish fast.</para>
	/// </remarks>
	[Fact]
	public void MoreSelection_RemainingAmountOptimization()
	{
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

		List<long> inputValues = new();
		inputValues.Add(1_000_000);

		for (int i = 0; i < 1000; i++)
		{
			inputValues.Add(1);
		}

		// All inputs cost the same.
		long[] inputCosts = inputValues.Select(x => 1L).ToArray();

		long target = 999_999; // Target that we cannot get as a sum of input values.

		BranchAndBound algorithm = new();
		MoreSelectionStrategy strategy = new(target, inputValues.ToArray(), inputCosts);
		_ = algorithm.TryGetMatch(strategy, out _, cts.Token);

		// Assert that we get expected best solution.
		long[] actualSelection = strategy.GetBestSelectionFound()!;
		Assert.NotNull(actualSelection);

		Assert.Equal(new long[] { 1_000_000 }, actualSelection);
	}

	[Fact]
	public void LessSelection_RemainingAmountOptimization()
	{
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

		List<long> inputValues = new();
		inputValues.Add(999_999);

		for (int i = 0; i < 1000; i++)
		{
			inputValues.Add(1);
		}

		// All inputs cost the same.
		long[] inputCosts = inputValues.Select(x => 1L).ToArray();

		long target = 1_000_000; // Target that we cannot get as a sum of input values.

		BranchAndBound algorithm = new();
		LessSelectionStrategy strategy = new(target, inputValues.ToArray(), inputCosts);
		_ = algorithm.TryGetMatch(strategy, out _, cts.Token);

		// Assert that we get expected best solution.
		long[] actualSelection = strategy.GetBestSelectionFound()!;
		Assert.NotNull(actualSelection);
	}

	[Fact]
	public void LesserSelection_CoinSumIsLowerThanTarget()
	{
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

		long[] inputValues = new long[] { 5, 4, 3, 2, 1 };
		long[] inputCosts = new long[] { 1, 1, 1, 1, 1 };

		long target = 16;

		BranchAndBound algorithm = new();
		LessSelectionStrategy strategy = new(target, inputValues, inputCosts);
		_ = algorithm.TryGetMatch(strategy, out _, cts.Token);

		long[] actualSelection = strategy.GetBestSelectionFound()!;
		Assert.NotNull(actualSelection);
		Assert.Equal(new long[] { 5, 4, 3, 2, 1 }, actualSelection);
	}

	[Fact]
	public void LesserSelection_NoInputCosts()
	{
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

		long[] inputValues = new long[] { 35, 17, 10, 5, 3, 2 };
		long[] inputCosts = new long[] { 0, 0, 0, 0, 0, 0 };

		long target = 26;

		BranchAndBound algorithm = new();
		LessSelectionStrategy strategy = new(target, inputValues, inputCosts);
		_ = algorithm.TryGetMatch(strategy, out _, cts.Token);

		long[] actualSelection = strategy.GetBestSelectionFound()!;
		Assert.NotNull(actualSelection);

		// Target 26.
		// Closest match is 25, i.e. 17 + 5 + 3 = 25.
		// Total costs: 25 (input costs are zeros).
		Assert.Equal(new long[] { 17, 5, 3 }, actualSelection);
	}

	[Fact]
	public void LesserSelection_WithInputCosts()
	{
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

		long[] inputValues = new long[] { 35, 17, 10, 5, 3, 2 };
		long[] inputCosts = new long[] { 1, 2, 1, 3, 1, 1 };

		long target = 33;

		BranchAndBound algorithm = new();
		LessSelectionStrategy strategy = new(target, inputValues, inputCosts);
		_ = algorithm.TryGetMatch(strategy, out _, cts.Token);

		long[] actualSelection = strategy.GetBestSelectionFound()!;
		Assert.NotNull(actualSelection);

		// Target 33.
		// Closest match is 32.
		// Total costs: (17 + 2) + (10 + 3) + (5 + 3) = 40
		// Total costs: (17 + 2) + (10 + 3) + (3 + 1) + (2 + 1) = 39
		Assert.Equal(new long[] { 17, 10, 3, 2 }, actualSelection);
	}
}
