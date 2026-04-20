using TSqlAnalyzer.Application.Presentation;
using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Tests.Presentation;

/// <summary>
/// 表示木の位置対応探索を固定するテスト。
/// SQL 側の選択位置から最も近い解析ノードを選べることを確認する。
/// </summary>
public sealed class DisplayTreeNodeNavigatorTests
{
    /// <summary>
    /// 子ノードの方が狭い範囲を持つ場合、より深いノードが選ばれることを確認する。
    /// </summary>
    [Fact]
    public void FindBestMatch_ReturnsDeepestContainingNode()
    {
        var root = new DisplayTreeNode(
            "root",
            [
                new DisplayTreeNode(
                    "query",
                    [
                        new DisplayTreeNode("select item", [], new TextSpan(10, 8))
                    ],
                    new TextSpan(0, 80))
            ]);

        var match = DisplayTreeNodeNavigator.FindBestMatch(root, 12, 0);

        Assert.NotNull(match);
        Assert.Equal("select item", match!.Text);
    }

    /// <summary>
    /// 選択範囲が子ノードをまたぐ場合は、範囲全体を含む親ノードが返ることを確認する。
    /// </summary>
    [Fact]
    public void FindBestMatch_ForSelectionRange_ReturnsContainingParentNode()
    {
        var root = new DisplayTreeNode(
            "root",
            [
                new DisplayTreeNode(
                    "condition",
                    [
                        new DisplayTreeNode("left", [], new TextSpan(20, 5)),
                        new DisplayTreeNode("right", [], new TextSpan(30, 5))
                    ],
                    new TextSpan(18, 20))
            ]);

        var match = DisplayTreeNodeNavigator.FindBestMatch(root, 20, 15);

        Assert.NotNull(match);
        Assert.Equal("condition", match!.Text);
    }

    /// <summary>
    /// 位置情報を持つノードがなければ null を返すことを確認する。
    /// </summary>
    [Fact]
    public void FindBestMatch_WhenNoSpanExists_ReturnsNull()
    {
        var root = new DisplayTreeNode("root", [new DisplayTreeNode("child", [])]);

        var match = DisplayTreeNodeNavigator.FindBestMatch(root, 5, 0);

        Assert.Null(match);
    }
}
