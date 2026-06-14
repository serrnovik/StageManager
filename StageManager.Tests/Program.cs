using System;
using System.Collections.Generic;
using System.Linq;
using StageManager;

var tests = new (string Name, Action Test)[]
{
	("All items are visible when they fit", AllItemsVisibleWhenTheyFit),
	("Overflow groups are balanced by icon weight", OverflowGroupsAreBalancedByIconWeight),
	("Several small overflow groups beat one catch-all group", SeveralSmallGroupsBeatOneCatchAllGroup),
	("Heavy hidden scenes are promoted over light visible scenes", HeavyHiddenScenesArePromoted),
	("Smaller capacity creates bounded overflow groups", SmallerCapacityCreatesBoundedOverflowGroups)
};

foreach (var test in tests)
{
	test.Test();
	Console.WriteLine($"PASS {test.Name}");
}

static void AllItemsVisibleWhenTheyFit()
{
	var plan = StageLayoutPlanner.Plan(
		Items(("A", 1), ("B", 1), ("C", 2)),
		capacity: 3,
		maxOverflowGroups: 4);

	AssertSequence(plan.VisibleItems, "A", "B", "C");
	AssertEqual(0, plan.OverflowGroups.Count, "Expected no overflow groups.");
}

static void OverflowGroupsAreBalancedByIconWeight()
{
	var weights = Weights(("Alpha", 1), ("Bravo", 1), ("Charlie", 1), ("Delta", 1), ("Echo", 1), ("Foxtrot", 1), ("Golf", 1), ("Hotel", 1));
	var plan = StageLayoutPlanner.Plan(
		ItemsFromWeights(weights),
		capacity: 6,
		maxOverflowGroups: 4);

	AssertEqual(2, plan.OverflowGroups.Count, "Expected two overflow groups.");
	AssertMaxGroupWeightAtMost(plan, weights, 2);
}

static void SeveralSmallGroupsBeatOneCatchAllGroup()
{
	var weights = Weights(("Alpha", 1), ("Bravo", 1), ("Charlie", 1), ("Delta", 1), ("Echo", 1), ("Foxtrot", 1), ("Golf", 1), ("Hotel", 1), ("India", 1));
	var plan = StageLayoutPlanner.Plan(
		ItemsFromWeights(weights),
		capacity: 6,
		maxOverflowGroups: 4);

	AssertEqual(3, plan.OverflowGroups.Count, "Expected multiple small groups instead of one catch-all group.");
	AssertMaxGroupWeightAtMost(plan, weights, 2);
}

static void HeavyHiddenScenesArePromoted()
{
	var weights = Weights(("Recent one", 1), ("Recent two", 1), ("Recent three", 1), ("Recent four", 1), ("Old heavy", 4), ("Old light", 1));
	var plan = StageLayoutPlanner.Plan(
	 new[]
		{
			Item("Recent one", 1, minutesAgo: 0),
			Item("Recent two", 1, minutesAgo: 1),
			Item("Recent three", 1, minutesAgo: 2),
			Item("Recent four", 1, minutesAgo: 3),
			Item("Old heavy", 4, minutesAgo: 120),
			Item("Old light", 1, minutesAgo: 121)
		},
		capacity: 4,
		maxOverflowGroups: 4);

	AssertContains(plan.VisibleItems, "Old heavy", "Expected the 4-icon scene to be promoted instead of hidden in overflow.");
	AssertMaxGroupWeightAtMost(plan, weights, 2);
}

static void SmallerCapacityCreatesBoundedOverflowGroups()
{
	var weights = Weights(("Alpha", 2), ("Bravo", 2), ("Charlie", 1), ("Delta", 1), ("Echo", 1), ("Foxtrot", 1), ("Golf", 1));
	var plan = StageLayoutPlanner.Plan(
		ItemsFromWeights(weights),
		capacity: 4,
		maxOverflowGroups: 4);

	AssertTrue(plan.VisibleItems.Count + plan.OverflowGroups.Count <= 4, "Rendered slots exceeded capacity.");
	AssertMaxGroupWeightAtMost(plan, weights, 3);
}

static StageLayoutItem<string>[] Items(params (string Name, int Weight)[] items)
{
	return items
		.Select((item, index) => Item(item.Name, item.Weight, index))
		.ToArray();
}

static StageLayoutItem<string>[] ItemsFromWeights(IReadOnlyDictionary<string, int> weights)
{
	return weights
		.Select((item, index) => Item(item.Key, item.Value, index))
		.ToArray();
}

static IReadOnlyDictionary<string, int> Weights(params (string Name, int Weight)[] items)
{
	return items.ToDictionary(i => i.Name, i => i.Weight);
}

static StageLayoutItem<string> Item(string name, int weight, int minutesAgo)
{
	return new StageLayoutItem<string>(
		name,
		name,
		DateTime.UtcNow.AddMinutes(-minutesAgo),
		weight);
}

static void AssertSequence(IReadOnlyList<string> actual, params string[] expected)
{
	AssertEqual(expected.Length, actual.Count, "Sequence length mismatch.");
	for (var i = 0; i < expected.Length; i++)
		AssertEqual(expected[i], actual[i], $"Sequence mismatch at {i}.");
}

static void AssertContains(IReadOnlyList<string> actual, string expected, string message)
{
	if (!actual.Contains(expected))
		throw new InvalidOperationException($"{message} Actual: {string.Join(", ", actual)}");
}

static void AssertMaxGroupWeightAtMost(StageLayoutPlan<string> plan, IReadOnlyDictionary<string, int> weights, int maxWeight)
{
	var actual = plan.OverflowGroups
		.Select(group => group.Sum(item => weights[item]))
		.DefaultIfEmpty(0)
		.Max();

	AssertTrue(actual <= maxWeight, $"Expected max group weight <= {maxWeight}, got {actual}.");
}

static void AssertEqual<T>(T expected, T actual, string message)
{
	if (!EqualityComparer<T>.Default.Equals(expected, actual))
		throw new InvalidOperationException($"{message} Expected: {expected}. Actual: {actual}.");
}

static void AssertTrue(bool condition, string message)
{
	if (!condition)
		throw new InvalidOperationException(message);
}
