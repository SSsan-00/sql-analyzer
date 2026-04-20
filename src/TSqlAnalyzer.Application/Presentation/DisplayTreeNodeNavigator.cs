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

        if (bestChild is not null)
        {
            return bestChild;
        }

        return Contains(node.SourceSpan, selectionSpan)
            ? node
            : null;
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
}
