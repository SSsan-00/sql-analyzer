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
            treeView.Nodes.Add(CreateNode(root));
            treeView.ExpandAll();
        }
        finally
        {
            treeView.EndUpdate();
        }
    }

    /// <summary>
    /// DisplayTreeNode を再帰的に TreeNode へ変換する。
    /// </summary>
    private static TreeNode CreateNode(DisplayTreeNode source)
    {
        var node = new TreeNode(source.Text);

        foreach (var child in source.Children)
        {
            node.Nodes.Add(CreateNode(child));
        }

        return node;
    }
}
