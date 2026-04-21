using TSqlAnalyzer.Application.Presentation;

namespace TSqlAnalyzer.Tests.Presentation;

/// <summary>
/// TreeView 表示分類のメタデータを固定するテスト。
/// UI 側の色やアイコンは WinForms の責務だが、分類名は表示モデルの一部として検証する。
/// </summary>
public sealed class DisplayTreeNodeKindCatalogTests
{
    /// <summary>
    /// 主要分類が利用者に伝わる日本語名へ変換されることを確認する。
    /// </summary>
    [Theory]
    [InlineData(DisplayTreeNodeKind.Root, "解析結果")]
    [InlineData(DisplayTreeNodeKind.Join, "JOIN")]
    [InlineData(DisplayTreeNodeKind.Condition, "条件")]
    [InlineData(DisplayTreeNodeKind.SetOperation, "集合演算")]
    [InlineData(DisplayTreeNodeKind.CommonTableExpression, "CTE")]
    [InlineData(DisplayTreeNodeKind.DataModification, "DML")]
    [InlineData(DisplayTreeNodeKind.ColumnReference, "参照列")]
    public void Get_ForRepresentativeKind_ReturnsDisplayName(DisplayTreeNodeKind kind, string expectedDisplayName)
    {
        var metadata = DisplayTreeNodeKindCatalog.Get(kind);

        Assert.Equal(kind, metadata.Kind);
        Assert.Equal(expectedDisplayName, metadata.DisplayName);
    }

    /// <summary>
    /// ツールチップでは分類名とノード本文の両方を確認できることを固定する。
    /// TreeView 上で長い文字列が省略されても、ホバー時に文脈を補える。
    /// </summary>
    [Fact]
    public void BuildToolTip_IncludesKindDisplayNameAndNodeText()
    {
        var node = new DisplayTreeNode(
            "JOIN #1",
            [],
            Kind: DisplayTreeNodeKind.Join);

        var toolTip = DisplayTreeNodeKindCatalog.BuildToolTip(node);

        Assert.Equal("JOIN: JOIN #1", toolTip);
    }
}
