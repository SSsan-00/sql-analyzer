using TSqlAnalyzer.Application.Presentation;

namespace TSqlAnalyzer.WinForms;

/// <summary>
/// UI 非依存の表示木を WinForms の TreeView へ反映する補助クラス。
/// Form 本体から TreeNode 生成の詳細を分離するために用意している。
/// </summary>
internal static class AnalysisTreeViewBinder
{
    /// <summary>
    /// 表示木を TreeView に反映する。
    /// </summary>
    public static void Bind(TreeView treeView, DisplayTreeNode root)
    {
        treeView.BeginUpdate();

        try
        {
            treeView.Nodes.Clear();
            var rootNode = CreateNode(root);
            treeView.Nodes.Add(rootNode);
            ApplyInitialExpansion(rootNode, depth: 0);
            treeView.SelectedNode = rootNode;
        }
        finally
        {
            treeView.EndUpdate();
        }
    }

    /// <summary>
    /// TreeNode に紐付いた表示ノードを返す。
    /// </summary>
    public static DisplayTreeNode? GetDisplayNode(TreeNode? treeNode)
    {
        return treeNode?.Tag as DisplayTreeNode;
    }

    /// <summary>
    /// 指定した表示ノードに対応する TreeNode を探す。
    /// </summary>
    public static TreeNode? FindTreeNode(TreeView treeView, DisplayTreeNode targetNode)
    {
        foreach (TreeNode treeNode in treeView.Nodes)
        {
            var match = FindTreeNode(treeNode, targetNode);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// DisplayTreeNode を再帰的に TreeNode へ変換する。
    /// </summary>
    private static TreeNode CreateNode(DisplayTreeNode source)
    {
        var imageKey = TreeNodeVisualCatalog.GetImageKey(source.Kind);
        var node = new TreeNode(source.Text)
        {
            Tag = source,
            ToolTipText = DisplayTreeNodeKindCatalog.BuildToolTip(source),
            ImageKey = imageKey,
            SelectedImageKey = imageKey
        };

        foreach (var child in source.Children)
        {
            node.Nodes.Add(CreateNode(child));
        }

        return node;
    }

    /// <summary>
    /// 表示木の分類と深さに応じて初期展開状態を反映する。
    /// 子ノード側にも展開状態を設定しておき、利用者が後から親を開いたときも読みやすい状態を保つ。
    /// </summary>
    private static void ApplyInitialExpansion(TreeNode treeNode, int depth)
    {
        foreach (TreeNode childNode in treeNode.Nodes)
        {
            ApplyInitialExpansion(childNode, depth + 1);
        }

        if (GetDisplayNode(treeNode) is { } displayNode
            && DisplayTreeExpansionPolicy.ShouldExpand(displayNode, depth))
        {
            treeNode.Expand();
            return;
        }

        treeNode.Collapse(ignoreChildren: false);
    }

    private static TreeNode? FindTreeNode(TreeNode currentNode, DisplayTreeNode targetNode)
    {
        if (ReferenceEquals(currentNode.Tag, targetNode))
        {
            return currentNode;
        }

        foreach (TreeNode childNode in currentNode.Nodes)
        {
            var match = FindTreeNode(childNode, targetNode);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
