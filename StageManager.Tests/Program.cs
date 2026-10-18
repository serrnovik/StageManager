using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StageManager;
using StageManager.Native;
using StageManager.Native.PInvoke;
using StageManager.Native.Window;
using Microsoft.Win32;

var tests = new (string Name, Action Test)[]
{
	("All items are visible when they fit", AllItemsVisibleWhenTheyFit),
	("Overflow groups are balanced by icon weight", OverflowGroupsAreBalancedByIconWeight),
	("Several small overflow groups beat one catch-all group", SeveralSmallGroupsBeatOneCatchAllGroup),
	("Heavy hidden scenes are promoted over light visible scenes", HeavyHiddenScenesArePromoted),
	("Smaller capacity creates bounded overflow groups", SmallerCapacityCreatesBoundedOverflowGroups),
	("Same app overflow items stay together", SameAppOverflowItemsStayTogether),
	("Legacy startup shortcut counts as startup", LegacyStartupShortcutCountsAsStartup),
	("Disabling startup removes registry value and legacy shortcut", DisablingStartupRemovesAllRegistrations),
	("Text input shell processes are ignored", TextInputShellProcessesAreIgnored),
	("Hide events map to a hide update", HideEventsMapToHideUpdate),
	("Cloak events do not remove windows", CloakEventsDoNotRemoveWindows),
	("Stale foreground events are detected", StaleForegroundEventsAreDetected),
	("Reentrancy only blocks the recently left scene", ReentrancyOnlyBlocksRecentlyLeftScene),
	("Current desktop is decided by majority", CurrentDesktopIsDecidedByMajority)
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

static void SameAppOverflowItemsStayTogether()
{
	var plan = StageLayoutPlanner.Plan(
		new[]
		{
			ItemWithGroup("Recent A", "Recent A", 1, 0),
			ItemWithGroup("Recent B", "Recent B", 1, 1),
			ItemWithGroup("Logitech window 1", "Logitech", 1, 20),
			ItemWithGroup("Logitech window 2", "Logitech", 1, 21),
			ItemWithGroup("Chrome", "Chrome", 1, 22),
			ItemWithGroup("Terminal", "Terminal", 1, 23)
		},
		capacity: 4,
		maxOverflowGroups: 4);

	var logitechGroups = plan.OverflowGroups
		.Count(group => group.Any(item => item.StartsWith("Logitech", StringComparison.Ordinal)));

	AssertEqual(1, logitechGroups, "Expected same-app overflow items to stay in one group.");
}

static void LegacyStartupShortcutCountsAsStartup()
{
	AssertTrue(
		AutoStart.HasStartupRegistration(null, "StageManager", "\"C:\\StageManager.exe\"", hasLegacyStartupShortcut: true),
		"Expected a legacy Startup-folder shortcut to count as enabled startup.");
}

static void DisablingStartupRemovesAllRegistrations()
{
	var testKeyName = $"Software\\StageManager.Tests\\{Guid.NewGuid():N}";
	var shortcutPath = Path.GetTempFileName();
	const string appName = "StageManager";
	const string appPath = "\"C:\\StageManager.exe\"";

	try
	{
		using var key = Registry.CurrentUser.CreateSubKey(testKeyName, writable: true);
		key.SetValue(appName, appPath);
		AutoStart.SetStartupRegistration(key, appName, startup: false, appPath, shortcutPath);

		AssertEqual(null, key.GetValue(appName), "Expected the Registry startup value to be removed.");
		AssertTrue(!File.Exists(shortcutPath), "Expected the legacy Startup-folder shortcut to be removed.");
	}
	finally
	{
		Registry.CurrentUser.DeleteSubKeyTree(testKeyName, throwOnMissingSubKey: false);
		if (File.Exists(shortcutPath))
			File.Delete(shortcutPath);
	}
}

static void TextInputShellProcessesAreIgnored()
{
	AssertTrue(WindowsWindow.IsIgnoredProcess("TextInputHost"), "Expected Windows 11 text-input panels to be ignored.");
	AssertTrue(WindowsWindow.IsIgnoredProcess("InputApp"), "Expected Windows 10 input panels to be ignored.");
	AssertTrue(WindowsWindow.IsIgnoredProcess("PowerToys.AdvancedPaste"), "Expected PowerToys pop-ups to be ignored.");
	AssertTrue(!WindowsWindow.IsIgnoredProcess("notepad"), "Expected regular application processes to remain eligible.");
}

static void HideEventsMapToHideUpdate()
{
	AssertEqual(WindowUpdateType.Hide, WindowsManager.GetWindowUpdateType(Win32.EVENT_CONSTANTS.EVENT_OBJECT_HIDE), "Expected a hidden window to be removed from Stage Manager.");
	AssertEqual(null, WindowsManager.GetWindowUpdateType(Win32.EVENT_CONSTANTS.EVENT_OBJECT_SHOW), "Expected window-show registration to remain a separate lifecycle path.");
}

static void CloakEventsDoNotRemoveWindows()
{
	AssertEqual(WindowUpdateType.Cloak, WindowsManager.GetWindowUpdateType(Win32.EVENT_CONSTANTS.EVENT_OBJECT_CLOAKED), "Expected cloaking (virtual desktop switch, suspended app) to be distinct from hiding.");
	AssertEqual(WindowUpdateType.Show, WindowsManager.GetWindowUpdateType(Win32.EVENT_CONSTANTS.EVENT_OBJECT_UNCLOAKED), "Expected uncloaking to show the window.");
}

static void StaleForegroundEventsAreDetected()
{
	AssertTrue(WindowsManager.IsStaleForegroundEvent(new IntPtr(1), new IntPtr(2)), "Expected a foreground event for a window that lost the focus again to be stale.");
	AssertTrue(!WindowsManager.IsStaleForegroundEvent(new IntPtr(1), new IntPtr(1)), "Expected a foreground event for the current foreground window to be current.");
}

static void ReentrancyOnlyBlocksRecentlyLeftScene()
{
	var left = Guid.NewGuid();
	var current = Guid.NewGuid();
	var other = Guid.NewGuid();
	var now = DateTime.UtcNow;

	AssertTrue(SceneManager.IsReentrancy(left, current, left, now.AddMilliseconds(-200), now), "Expected an app re-activating the scene just left to be blocked.");
	AssertTrue(!SceneManager.IsReentrancy(left, current, left, now.AddSeconds(-2), now), "Expected the block to expire.");
	AssertTrue(!SceneManager.IsReentrancy(other, current, left, now, now), "Expected other scenes not to be blocked.");
	AssertTrue(!SceneManager.IsReentrancy(current, current, current, now, now), "Expected the current scene never to be blocked.");
	AssertTrue(!SceneManager.IsReentrancy(left, current, null, now, now), "Expected no block without a recently left scene.");
}

static void CurrentDesktopIsDecidedByMajority()
{
	var a = Guid.NewGuid();
	var b = Guid.NewGuid();

	AssertEqual<Guid?>(a, VirtualDesktopManager.PickMostCommonDesktopId(new[] { b, a, a, Guid.Empty, Guid.Empty, Guid.Empty }), "Expected the most common non-empty desktop id to win.");
	AssertEqual<Guid?>(null, VirtualDesktopManager.PickMostCommonDesktopId(new[] { Guid.Empty }), "Expected empty desktop ids to be ignored.");
	AssertEqual<Guid?>(null, VirtualDesktopManager.PickMostCommonDesktopId(Array.Empty<Guid>()), "Expected no desktop id without votes.");
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
	return ItemWithGroup(name, name, weight, minutesAgo);
}

static StageLayoutItem<string> ItemWithGroup(string name, string groupName, int weight, int minutesAgo)
{
	return new StageLayoutItem<string>(
		name,
		groupName,
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
