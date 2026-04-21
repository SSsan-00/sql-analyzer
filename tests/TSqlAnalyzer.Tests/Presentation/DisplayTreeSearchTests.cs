using TSqlAnalyzer.Application.Presentation;

namespace TSqlAnalyzer.Tests.Presentation;

/// <summary>
/// TreeView 表示モデルの検索・絞り込み処理を固定するテスト。
/// GUI を直接触らず、検索対象となる表示木の振る舞いを検証する。
/// </summary>
public sealed class DisplayTreeSearchTests
{
    /// <summary>
    /// ノード本文だけでなく分類名でも検索できることを確認する。
    /// </summary>
    [Fact]
    public void FindMatches_MatchesTextAndKindDisplayName()
    {
        var root = BuildSampleTree();

        var joinMatches = DisplayTreeSearch.FindMatches(root, "join");
        var conditionMatches = DisplayTreeSearch.FindMatches(root, "条件");

        Assert.Contains(joinMatches, node => node.Text == "JOIN #1");
        Assert.Contains(conditionMatches, node => node.Text == "ON条件");
    }

    /// <summary>
    /// 絞り込みでは一致ノードと祖先だけを残し、関係のない兄弟ノードを消すことを確認する。
    /// </summary>
    [Fact]
    public void Filter_KeepsMatchingPathAndRemovesUnrelatedBranches()
    {
        var root = BuildSampleTree();

        var filtered = DisplayTreeSearch.Filter(root, "dbo.Orders");

        Assert.NotNull(filtered);
        var texts = Flatten(filtered).ToArray();
        Assert.Contains("クエリ解析結果", texts);
        Assert.Contains("主構造", texts);
        Assert.Contains("結合", texts);
        Assert.Contains("JOIN #1", texts);
        Assert.Contains("結合先: dbo.Orders o", texts);
        Assert.DoesNotContain("取得項目", texts);
    }

    /// <summary>
    /// 一致がない場合は null を返し、UI 側が「一致なし」表示へ切り替えられることを確認する。
    /// </summary>
    [Fact]
    public void Filter_WhenNoMatch_ReturnsNull()
    {
        var root = BuildSampleTree();

        var filtered = DisplayTreeSearch.Filter(root, "存在しないテーブル");

        Assert.Null(filtered);
    }

    private static DisplayTreeNode BuildSampleTree()
    {
        return Node(
            DisplayTreeNodeKind.Root,
            "クエリ解析結果",
            Node(
                DisplayTreeNodeKind.Section,
                "主構造",
                Node(
                    DisplayTreeNodeKind.Section,
                    "取得項目",
                    Node(DisplayTreeNodeKind.Select, "項目 #1: u.Id")),
                Node(
                    DisplayTreeNodeKind.Join,
                    "結合",
                    Node(
                        DisplayTreeNodeKind.Join,
                        "JOIN #1",
                        Node(DisplayTreeNodeKind.Detail, "結合先: dbo.Orders o"),
                        Node(
                            DisplayTreeNodeKind.Join,
                            "ON条件",
                            Node(DisplayTreeNodeKind.Condition, "条件 #1: u.Id = o.UserId"))))));
    }

    private static DisplayTreeNode Node(DisplayTreeNodeKind kind, string text, params DisplayTreeNode[] children)
    {
        return new DisplayTreeNode(text, children, Kind: kind);
    }

    private static IEnumerable<string> Flatten(DisplayTreeNode node)
    {
        yield return node.Text;

        foreach (var child in node.Children)
        {
            foreach (var text in Flatten(child))
            {
                yield return text;
            }
        }
    }
}
