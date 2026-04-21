using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Application.Presentation;

/// <summary>
/// TreeView 上での見た目を決めるための表示分類。
/// 解析モデルそのものではなく、利用者が画面上で構造を追うための分類として扱う。
/// </summary>
public enum DisplayTreeNodeKind
{
    /// <summary>
    /// 解析結果全体のルート。
    /// </summary>
    Root,

    /// <summary>
    /// 「主構造」「取得項目」などの見出し。
    /// </summary>
    Section,

    /// <summary>
    /// SELECT 句や取得項目に関するノード。
    /// </summary>
    Select,

    /// <summary>
    /// FROM、INTO、更新対象などデータソースに関するノード。
    /// </summary>
    Source,

    /// <summary>
    /// JOIN と JOIN 条件に関するノード。
    /// </summary>
    Join,

    /// <summary>
    /// WHERE、HAVING、ON 内の条件構造に関するノード。
    /// </summary>
    Condition,

    /// <summary>
    /// UNION / EXCEPT / INTERSECT など集合演算に関するノード。
    /// </summary>
    SetOperation,

    /// <summary>
    /// WITH 句の共通テーブル式に関するノード。
    /// </summary>
    CommonTableExpression,

    /// <summary>
    /// サブクエリや派生テーブルの内部構造に関するノード。
    /// </summary>
    Subquery,

    /// <summary>
    /// INSERT / UPDATE / DELETE に関するノード。
    /// </summary>
    DataModification,

    /// <summary>
    /// CREATE VIEW / CREATE TABLE に関するノード。
    /// </summary>
    Create,

    /// <summary>
    /// 式の中で参照される列に関するノード。
    /// </summary>
    ColumnReference,

    /// <summary>
    /// 分類に依存しない通常の詳細行。
    /// </summary>
    Detail
}

/// <summary>
/// TreeView 表示用の UI 非依存ノード。
/// WinForms の TreeNode を直接返さず、中立的な木構造へ一度落とし込むために使う。
/// </summary>
public sealed record DisplayTreeNode(
    string Text,
    IReadOnlyList<DisplayTreeNode> Children,
    TextSpan? SourceSpan = null,
    DisplayTreeNodeKind Kind = DisplayTreeNodeKind.Detail);
