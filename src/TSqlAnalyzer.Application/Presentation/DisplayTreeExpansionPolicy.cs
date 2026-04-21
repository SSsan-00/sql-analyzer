namespace TSqlAnalyzer.Application.Presentation;

/// <summary>
/// TreeView の初期展開状態を決める UI 非依存ポリシー。
/// 巨大 SQL では全展開すると全体像を追いにくいため、主要構造だけを開き、詳細は利用者が必要に応じて開けるようにする。
/// </summary>
public static class DisplayTreeExpansionPolicy
{
    /// <summary>
    /// 指定ノードを初期表示で展開するかを返す。
    /// depth はルートを 0 とした階層深さで、深い詳細ノードほど既定では閉じる。
    /// </summary>
    public static bool ShouldExpand(DisplayTreeNode node, int depth)
    {
        if (node.Children.Count == 0)
        {
            return false;
        }

        return node.Kind switch
        {
            DisplayTreeNodeKind.Root => true,
            DisplayTreeNodeKind.ColumnReference => false,
            DisplayTreeNodeKind.Select => false,
            DisplayTreeNodeKind.Detail => false,
            DisplayTreeNodeKind.Join => depth <= 4,
            DisplayTreeNodeKind.Condition => depth <= 4,
            DisplayTreeNodeKind.SetOperation => depth <= 3,
            DisplayTreeNodeKind.DataModification => depth <= 3,
            DisplayTreeNodeKind.Create => depth <= 3,
            DisplayTreeNodeKind.CommonTableExpression => depth <= 2,
            DisplayTreeNodeKind.Source => depth <= 2,
            DisplayTreeNodeKind.Subquery => depth <= 2,
            DisplayTreeNodeKind.Section => depth <= 2,
            _ => depth <= 1
        };
    }
}
