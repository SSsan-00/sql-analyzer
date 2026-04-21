namespace TSqlAnalyzer.Application.Presentation;

/// <summary>
/// 表示ノード分類の説明情報。
/// UI はこの情報を使うことで、分類名やツールチップ文言を解析ロジックから独立して扱える。
/// </summary>
public sealed record DisplayTreeNodeKindMetadata(
    DisplayTreeNodeKind Kind,
    string DisplayName);

/// <summary>
/// DisplayTreeNodeKind に関する表示用メタデータを提供する。
/// WinForms 固有の色やフォントは持たせず、UI 非依存な分類名だけを管理する。
/// </summary>
public static class DisplayTreeNodeKindCatalog
{
    /// <summary>
    /// 分類に対応する日本語表示名を返す。
    /// </summary>
    public static DisplayTreeNodeKindMetadata Get(DisplayTreeNodeKind kind)
    {
        return kind switch
        {
            DisplayTreeNodeKind.Root => new DisplayTreeNodeKindMetadata(kind, "解析結果"),
            DisplayTreeNodeKind.Section => new DisplayTreeNodeKindMetadata(kind, "見出し"),
            DisplayTreeNodeKind.Select => new DisplayTreeNodeKindMetadata(kind, "SELECT"),
            DisplayTreeNodeKind.Source => new DisplayTreeNodeKindMetadata(kind, "ソース"),
            DisplayTreeNodeKind.Join => new DisplayTreeNodeKindMetadata(kind, "JOIN"),
            DisplayTreeNodeKind.Condition => new DisplayTreeNodeKindMetadata(kind, "条件"),
            DisplayTreeNodeKind.SetOperation => new DisplayTreeNodeKindMetadata(kind, "集合演算"),
            DisplayTreeNodeKind.CommonTableExpression => new DisplayTreeNodeKindMetadata(kind, "CTE"),
            DisplayTreeNodeKind.Subquery => new DisplayTreeNodeKindMetadata(kind, "サブクエリ"),
            DisplayTreeNodeKind.DataModification => new DisplayTreeNodeKindMetadata(kind, "DML"),
            DisplayTreeNodeKind.Create => new DisplayTreeNodeKindMetadata(kind, "CREATE"),
            DisplayTreeNodeKind.ColumnReference => new DisplayTreeNodeKindMetadata(kind, "参照列"),
            _ => new DisplayTreeNodeKindMetadata(kind, "詳細")
        };
    }

    /// <summary>
    /// TreeView のホバー表示に使う説明文を作る。
    /// 長い SQL 断片を省略表示している場合でも、どの分類のノードかを確認できるようにする。
    /// </summary>
    public static string BuildToolTip(DisplayTreeNode node)
    {
        var metadata = Get(node.Kind);
        return $"{metadata.DisplayName}: {node.Text}";
    }
}
