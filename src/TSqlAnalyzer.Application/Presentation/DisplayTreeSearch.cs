namespace TSqlAnalyzer.Application.Presentation;

/// <summary>
/// TreeView 表示モデルを対象にした検索・絞り込み処理。
/// WinForms の TreeNode へ依存させず、表示木そのものを検索対象にすることでテストしやすくしている。
/// </summary>
public static class DisplayTreeSearch
{
    /// <summary>
    /// 検索語に一致するノードを深さ優先で返す。
    /// ノード本文だけでなく分類名も検索対象に含め、利用者が「JOIN」「条件」などで探せるようにする。
    /// </summary>
    public static IReadOnlyList<DisplayTreeNode> FindMatches(DisplayTreeNode root, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return [];
        }

        var matches = new List<DisplayTreeNode>();
        CollectMatches(root, searchText.Trim(), matches);
        return matches;
    }

    /// <summary>
    /// 検索語に一致するノード、または一致ノードを子孫に持つノードだけを残した表示木を返す。
    /// 一致したノード自身は子孫をそのまま残し、見つけた構造の詳細をすぐ辿れるようにする。
    /// </summary>
    public static DisplayTreeNode? Filter(DisplayTreeNode root, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return root;
        }

        return FilterCore(root, searchText.Trim());
    }

    /// <summary>
    /// 指定ノードが検索語に一致するかを返す。
    /// TreeView 側のハイライト表示でも同じ判定を使う。
    /// </summary>
    public static bool IsMatch(DisplayTreeNode node, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return false;
        }

        var keyword = searchText.Trim();
        var kindName = DisplayTreeNodeKindCatalog.Get(node.Kind).DisplayName;
        return node.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || kindName.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static void CollectMatches(DisplayTreeNode node, string searchText, ICollection<DisplayTreeNode> matches)
    {
        if (IsMatch(node, searchText))
        {
            matches.Add(node);
        }

        foreach (var child in node.Children)
        {
            CollectMatches(child, searchText, matches);
        }
    }

    private static DisplayTreeNode? FilterCore(DisplayTreeNode node, string searchText)
    {
        if (IsMatch(node, searchText))
        {
            return node;
        }

        var filteredChildren = node.Children
            .Select(child => FilterCore(child, searchText))
            .Where(child => child is not null)
            .Cast<DisplayTreeNode>()
            .ToArray();

        if (filteredChildren.Length == 0)
        {
            return null;
        }

        return node with { Children = filteredChildren };
    }
}
