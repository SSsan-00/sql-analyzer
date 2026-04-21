using TSqlAnalyzer.Application.Presentation;

namespace TSqlAnalyzer.Tests.Presentation;

/// <summary>
/// TreeView 初期展開ポリシーのテスト。
/// GUI を直接検証せず、表示モデルの分類と深さから開閉判断を固定する。
/// </summary>
public sealed class DisplayTreeExpansionPolicyTests
{
    /// <summary>
    /// 主要な見出しは開き、SELECT 項目の詳細や参照列は閉じることを確認する。
    /// </summary>
    [Fact]
    public void ShouldExpand_ForRepresentativeNodes_OpensStructureAndClosesDetails()
    {
        var root = Node(DisplayTreeNodeKind.Root, "クエリ解析結果", Node(DisplayTreeNodeKind.Section, "主構造"));
        var mainStructure = Node(DisplayTreeNodeKind.Section, "主構造", Node(DisplayTreeNodeKind.Section, "取得項目"));
        var selectItem = Node(DisplayTreeNodeKind.Select, "項目 #1: u.Id", Node(DisplayTreeNodeKind.Detail, "式: u.Id"));
        var columnReference = Node(DisplayTreeNodeKind.ColumnReference, "参照列", Node(DisplayTreeNodeKind.Detail, "列 #1: u.Id"));

        Assert.True(DisplayTreeExpansionPolicy.ShouldExpand(root, depth: 0));
        Assert.True(DisplayTreeExpansionPolicy.ShouldExpand(mainStructure, depth: 1));
        Assert.False(DisplayTreeExpansionPolicy.ShouldExpand(selectItem, depth: 3));
        Assert.False(DisplayTreeExpansionPolicy.ShouldExpand(columnReference, depth: 3));
    }

    /// <summary>
    /// JOIN と条件論理は巨大 SQL の理解に重要なため、一定階層までは初期展開されることを確認する。
    /// </summary>
    [Fact]
    public void ShouldExpand_ForJoinAndCondition_OpensImportantNestedStructure()
    {
        var join = Node(DisplayTreeNodeKind.Join, "JOIN #1", Node(DisplayTreeNodeKind.Join, "ON条件"));
        var onCondition = Node(DisplayTreeNodeKind.Join, "ON条件", Node(DisplayTreeNodeKind.Condition, "条件 #1: u.Id = o.UserId"));
        var conditionLogic = Node(DisplayTreeNodeKind.Condition, "条件論理", Node(DisplayTreeNodeKind.Condition, "AND"));
        var deepCondition = Node(DisplayTreeNodeKind.Condition, "条件", Node(DisplayTreeNodeKind.Detail, "式: x = 1"));

        Assert.True(DisplayTreeExpansionPolicy.ShouldExpand(join, depth: 3));
        Assert.True(DisplayTreeExpansionPolicy.ShouldExpand(onCondition, depth: 4));
        Assert.True(DisplayTreeExpansionPolicy.ShouldExpand(conditionLogic, depth: 3));
        Assert.False(DisplayTreeExpansionPolicy.ShouldExpand(deepCondition, depth: 5));
    }

    private static DisplayTreeNode Node(DisplayTreeNodeKind kind, string text, params DisplayTreeNode[] children)
    {
        return new DisplayTreeNode(text, children, Kind: kind);
    }
}
