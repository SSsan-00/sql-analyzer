using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Application.Presentation;

/// <summary>
/// 表示木と SQL テキスト位置の対応を探索する補助。
/// UI 依存コードから選択判定ロジックを切り離し、テストしやすくする。
/// </summary>
public static class DisplayTreeNodeNavigator
{
    /// <summary>
    /// 指定位置または選択範囲を最も狭く含むノードを返す。
    /// </summary>
    public static DisplayTreeNode? FindBestMatch(DisplayTreeNode root, int start, int length)
    {
        var selectionSpan = new TextSpan(start, Math.Max(length, 0));
        return FindBestMatchCore(root, selectionSpan);
    }

    private static DisplayTreeNode? FindBestMatchCore(DisplayTreeNode node, TextSpan selectionSpan)
    {
        DisplayTreeNode? bestChild = null;
        var currentContainsSelection = Contains(node.SourceSpan, selectionSpan);

        foreach (var child in node.Children)
        {
            var match = FindBestMatchCore(child, selectionSpan);
            if (match is null)
            {
                continue;
            }

            if (bestChild is null
                || GetSpanLength(match.SourceSpan) < GetSpanLength(bestChild.SourceSpan))
            {
                bestChild = match;
            }
        }

        if (currentContainsSelection
            && bestChild is not null
            && GetSpanLength(node.SourceSpan) == GetSpanLength(bestChild.SourceSpan)
            && GetNavigationPriority(node.Kind) > GetNavigationPriority(bestChild.Kind))
        {
            return node;
        }

        if (bestChild is not null)
        {
            return bestChild;
        }

        if (currentContainsSelection
            && selectionSpan.Length == 0
            && ShouldUseNearestDescendantFallback(node.Kind))
        {
            var nearestChild = FindNearestDescendant(node, selectionSpan.Start);
            if (nearestChild is not null)
            {
                return nearestChild;
            }
        }

        return currentContainsSelection
            ? node
            : null;
    }

    private static bool ShouldUseNearestDescendantFallback(DisplayTreeNodeKind kind)
    {
        return kind is DisplayTreeNodeKind.Root
            or DisplayTreeNodeKind.Section
            or DisplayTreeNodeKind.DataModification;
    }

    private static DisplayTreeNode? FindNearestDescendant(DisplayTreeNode node, int position)
    {
        DisplayTreeNode? bestMatch = null;
        foreach (var child in node.Children)
        {
            CollectNearestDescendant(child, position, ref bestMatch);
        }

        return bestMatch;
    }

    private static void CollectNearestDescendant(DisplayTreeNode node, int position, ref DisplayTreeNode? bestMatch)
    {
        if (node.SourceSpan is not null && IsBetterNearestMatch(node, bestMatch, position))
        {
            bestMatch = node;
        }

        foreach (var child in node.Children)
        {
            CollectNearestDescendant(child, position, ref bestMatch);
        }
    }

    private static bool IsBetterNearestMatch(DisplayTreeNode candidate, DisplayTreeNode? currentBest, int position)
    {
        if (candidate.SourceSpan is null)
        {
            return false;
        }

        if (currentBest?.SourceSpan is not { } currentBestSpan)
        {
            return true;
        }

        var candidateDistance = GetDistanceToSpan(candidate.SourceSpan, position);
        var currentBestDistance = GetDistanceToSpan(currentBestSpan, position);
        if (candidateDistance != currentBestDistance)
        {
            return candidateDistance < currentBestDistance;
        }

        var candidateStartsBeforeOrAtPosition = candidate.SourceSpan.Start <= position;
        var currentBestStartsBeforeOrAtPosition = currentBestSpan.Start <= position;
        if (candidateStartsBeforeOrAtPosition != currentBestStartsBeforeOrAtPosition)
        {
            return candidateStartsBeforeOrAtPosition;
        }

        var candidateLength = GetSpanLength(candidate.SourceSpan);
        var currentBestLength = GetSpanLength(currentBestSpan);
        if (candidateLength != currentBestLength)
        {
            return candidateLength < currentBestLength;
        }

        return GetNavigationPriority(candidate.Kind) > GetNavigationPriority(currentBest.Kind);
    }

    private static int GetDistanceToSpan(TextSpan span, int position)
    {
        if (position < span.Start)
        {
            return span.Start - position;
        }

        if (position > span.End)
        {
            return position - span.End;
        }

        return 0;
    }

    private static bool Contains(TextSpan? candidate, TextSpan selectionSpan)
    {
        if (candidate is null)
        {
            return false;
        }

        return selectionSpan.Length == 0
            ? candidate.Contains(selectionSpan.Start)
            : candidate.Contains(selectionSpan);
    }

    private static int GetSpanLength(TextSpan? span)
    {
        return span?.Length ?? int.MaxValue;
    }

    private static int GetNavigationPriority(DisplayTreeNodeKind kind)
    {
        return kind switch
        {
            DisplayTreeNodeKind.Condition => 5,
            DisplayTreeNodeKind.Select => 4,
            DisplayTreeNodeKind.Source => 3,
            DisplayTreeNodeKind.Join => 2,
            DisplayTreeNodeKind.ColumnReference => 1,
            DisplayTreeNodeKind.Detail => 0,
            DisplayTreeNodeKind.Section => 0,
            DisplayTreeNodeKind.Root => 0,
            _ => 1
        };
    }
}
