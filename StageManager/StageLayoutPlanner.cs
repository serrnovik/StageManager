using System;
using System.Collections.Generic;
using System.Linq;

namespace StageManager
{
	public sealed record StageLayoutItem<T>(
		T Value,
		string GroupName,
		DateTime Updated,
		int Weight);

	public sealed record StageLayoutPlan<T>(
		IReadOnlyList<T> VisibleItems,
		IReadOnlyList<IReadOnlyList<T>> OverflowGroups);

	public static class StageLayoutPlanner
	{
		public static StageLayoutPlan<T> Plan<T>(
			IReadOnlyList<StageLayoutItem<T>> items,
			int capacity,
			int maxOverflowGroups)
		{
			if (!items.Any() || capacity <= 0)
				return new StageLayoutPlan<T>(Array.Empty<T>(), Array.Empty<IReadOnlyList<T>>());

			if (items.Count <= capacity)
				return new StageLayoutPlan<T>(
					items.OrderByDescending(i => i.Updated).Select(i => i.Value).ToArray(),
					Array.Empty<IReadOnlyList<T>>());

			var maxGroupCount = Math.Max(1, Math.Min(maxOverflowGroups, capacity));
			var best = Enumerable.Range(1, maxGroupCount)
				.Select(groupCount => BuildCandidate(items, capacity, groupCount))
				.OrderBy(candidate => candidate.LargestGroupWeight)
				.ThenBy(candidate => candidate.GroupWeightSpread)
				.ThenBy(candidate => candidate.OverflowGroups.Count)
				.First();

			return new StageLayoutPlan<T>(
				best.VisibleItems.Select(i => i.Value).ToArray(),
				best.OverflowGroups.Select(g => (IReadOnlyList<T>)g.Select(i => i.Value).ToArray()).ToArray());
		}

		private static LayoutCandidate<T> BuildCandidate<T>(
			IReadOnlyList<StageLayoutItem<T>> items,
			int capacity,
			int groupCount)
		{
			var visibleCount = Math.Max(0, capacity - groupCount);
			var visibleItems = items
				.OrderByDescending(i => i.Updated)
				.Take(visibleCount)
				.ToList();
			var hiddenItems = items
				.Except(visibleItems)
				.ToList();

			PromoteHeavyHiddenItems(visibleItems, hiddenItems);
			var overflowGroups = BuildBalancedGroups(hiddenItems, groupCount);
			var groupWeights = overflowGroups.Select(g => g.Sum(i => i.Weight)).ToArray();

			return new LayoutCandidate<T>(
				visibleItems,
				overflowGroups,
				groupWeights.Any() ? groupWeights.Max() : 0,
				groupWeights.Any() ? groupWeights.Max() - groupWeights.Min() : 0);
		}

		private static void PromoteHeavyHiddenItems<T>(
			List<StageLayoutItem<T>> visibleItems,
			List<StageLayoutItem<T>> hiddenItems)
		{
			while (visibleItems.Any() && hiddenItems.Any())
			{
				var heaviestHidden = hiddenItems
					.OrderByDescending(i => i.Weight)
					.ThenByDescending(i => i.Updated)
					.First();
				var lightestVisible = visibleItems
					.OrderBy(i => i.Weight)
					.ThenBy(i => i.Updated)
					.First();

				if (heaviestHidden.Weight <= lightestVisible.Weight + 1)
					return;

				hiddenItems.Remove(heaviestHidden);
				visibleItems.Remove(lightestVisible);
				visibleItems.Add(heaviestHidden);
				hiddenItems.Add(lightestVisible);
			}
		}

		private static IReadOnlyList<IReadOnlyList<StageLayoutItem<T>>> BuildBalancedGroups<T>(
			IReadOnlyList<StageLayoutItem<T>> hiddenItems,
			int groupCount)
		{
			if (!hiddenItems.Any() || groupCount <= 0)
				return Array.Empty<IReadOnlyList<StageLayoutItem<T>>>();

			groupCount = Math.Min(groupCount, hiddenItems.Count);
			var groups = Enumerable.Range(0, groupCount)
				.Select(_ => new List<StageLayoutItem<T>>())
				.ToArray();

			foreach (var item in hiddenItems
				.OrderByDescending(i => i.Weight)
				.ThenBy(i => i.GroupName, StringComparer.CurrentCultureIgnoreCase)
				.ThenByDescending(i => i.Updated))
			{
				var targetGroup = groups
					.OrderBy(g => g.Sum(i => i.Weight))
					.ThenBy(g => g.Count)
					.First();
				targetGroup.Add(item);
			}

			return groups
				.Where(g => g.Any())
				.Select(g => (IReadOnlyList<StageLayoutItem<T>>)g
					.OrderBy(i => i.GroupName, StringComparer.CurrentCultureIgnoreCase)
					.ThenByDescending(i => i.Updated)
					.ToArray())
				.ToArray();
		}

		private sealed record LayoutCandidate<T>(
			IReadOnlyList<StageLayoutItem<T>> VisibleItems,
			IReadOnlyList<IReadOnlyList<StageLayoutItem<T>>> OverflowGroups,
			int LargestGroupWeight,
			int GroupWeightSpread);
	}
}
