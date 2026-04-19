namespace TSqlAnalyzer.Domain.Analysis;

/// <summary>
/// 解析結果全体の状態を表す。
/// 初期版では「正常にSELECT系を解析できたか」「未対応か」「入力不備か」を
/// 呼び出し元が明確に判定できるよう、文種別を独立して保持する。
/// </summary>
public enum QueryStatementCategory
{
    Empty,
    Select,
    SetOperation,
    Unsupported,
    ParseError
}

/// <summary>
/// ルート以降の再帰的なクエリ種別を表す。
/// SELECT と集合演算を分けておくことで、TreeView 側が表示を切り替えやすくなる。
/// </summary>
public enum QueryExpressionKind
{
    Select,
    SetOperation
}

/// <summary>
/// JOIN の種別。
/// 画面表示では文字列も保持するが、内部判定用に enum も持たせる。
/// </summary>
public enum JoinType
{
    Inner,
    Left,
    Right,
    Full,
    Cross,
    Unknown
}

/// <summary>
/// FROM / JOIN ソースの分類。
/// CTE 参照や派生テーブルを区別しておくと、参照関係や表示補足を付けやすい。
/// </summary>
public enum SourceKind
{
    Object,
    CommonTableExpressionReference,
    DerivedTable,
    Unknown
}

/// <summary>
/// 集合演算の種別。
/// UNION と UNION ALL を分けて保持し、将来の差異表示に備える。
/// </summary>
public enum SetOperationType
{
    Union,
    UnionAll,
    Except,
    Intersect
}

/// <summary>
/// WHERE / HAVING などで検出したサブクエリ系述語の種別。
/// 初期版では EXISTS 系と IN 系を優先して区別する。
/// </summary>
public enum ConditionMarkerType
{
    Exists,
    NotExists,
    In,
    NotIn
}

/// <summary>
/// 条件式論理木のノード種別。
/// AND / OR / NOT と通常述語を分けておくと、TreeView で構造表示しやすい。
/// </summary>
public enum ConditionNodeKind
{
    And,
    Or,
    Not,
    Predicate
}

/// <summary>
/// UI に伝える注意情報の重要度。
/// エラー・警告・補足を同一の仕組みで扱えるようにしている。
/// </summary>
public enum AnalysisNoticeLevel
{
    Information,
    Warning,
    Error
}

/// <summary>
/// 画面やテストで利用する解析結果のルート。
/// ScriptDom の生オブジェクトを外へ出さず、独自モデルとして完結させる。
/// </summary>
public sealed record QueryAnalysisResult(
    QueryStatementCategory StatementCategory,
    IReadOnlyList<CommonTableExpressionAnalysis> CommonTableExpressions,
    QueryExpressionAnalysis? Query,
    IReadOnlyList<ParseIssue> ParseIssues,
    IReadOnlyList<AnalysisNotice> Notices);

/// <summary>
/// SELECT 系解析結果の再帰的な基底型。
/// 集合演算の左右やサブクエリでも同じ型を再利用する。
/// </summary>
public abstract record QueryExpressionAnalysis(QueryExpressionKind Kind);

/// <summary>
/// 単一の SELECT 文構造を表す。
/// UI はこの構造を読めば、主テーブル・JOIN・WHERE などを段階的に表示できる。
/// </summary>
public sealed record SelectQueryAnalysis(
    bool IsDistinct,
    string? TopExpressionText,
    IReadOnlyList<SelectItemAnalysis> SelectItems,
    SourceAnalysis? MainSource,
    IReadOnlyList<JoinAnalysis> Joins,
    ConditionAnalysis? WhereCondition,
    GroupByAnalysis? GroupBy,
    ConditionAnalysis? HavingCondition,
    OrderByAnalysis? OrderBy,
    IReadOnlyList<SubqueryAnalysis> Subqueries)
    : QueryExpressionAnalysis(QueryExpressionKind.Select);

/// <summary>
/// CTE の 1 定義を表す。
/// 定義名と内部クエリを保持し、複雑なクエリの前段構造を追えるようにする。
/// </summary>
public sealed record CommonTableExpressionAnalysis(
    string Name,
    IReadOnlyList<string> ColumnNames,
    QueryExpressionAnalysis Query);

/// <summary>
/// UNION / EXCEPT / INTERSECT などの集合演算を表す。
/// 左右のクエリを同じ再帰型で保持することで、将来の深い入れ子にも対応しやすくする。
/// </summary>
public sealed record SetOperationQueryAnalysis(
    SetOperationType OperationType,
    QueryExpressionAnalysis LeftQuery,
    QueryExpressionAnalysis RightQuery)
    : QueryExpressionAnalysis(QueryExpressionKind.SetOperation);

/// <summary>
/// SELECT 項目を表す。
/// 初期版では表示文字列を主に持ち、必要に応じて後から別フィールドを増やせる形にする。
/// </summary>
public sealed record SelectItemAnalysis(int Sequence, string DisplayText);

/// <summary>
/// FROM 句や JOIN 先として使われるソースを表す。
/// 通常テーブルだけでなく、派生テーブルやサブクエリ結果も保持できるようにしている。
/// </summary>
public sealed record SourceAnalysis(
    string DisplayText,
    QueryExpressionAnalysis? NestedQuery,
    SourceKind SourceKind,
    string? SourceName);

/// <summary>
/// JOIN 表示に必要な最小情報。
/// 重要方針に合わせて、種別・結合先・ON 条件に責務を絞っている。
/// </summary>
public sealed record JoinAnalysis(
    int Sequence,
    JoinType JoinType,
    string JoinTypeText,
    SourceAnalysis TargetSource,
    string? OnConditionText);

/// <summary>
/// WHERE 句などの条件本体。
/// 条件式全文に加えて、EXISTS / IN などの構造上の目印を分けて保持する。
/// </summary>
public sealed record ConditionAnalysis(
    string DisplayText,
    ConditionNodeAnalysis RootNode,
    IReadOnlyList<ConditionMarker> Markers);

/// <summary>
/// 条件式の論理木 1 ノード分。
/// マーカー付き述語もこのノードにぶら下げることで、条件種別一覧と論理木を両立できる。
/// </summary>
public sealed record ConditionNodeAnalysis(
    ConditionNodeKind NodeKind,
    string DisplayText,
    IReadOnlyList<ConditionNodeAnalysis> Children,
    ConditionMarker? Marker);

/// <summary>
/// 条件式の中で検出した注目ポイント。
/// 将来は論理木へ発展させる余地を残しつつ、初期版では種類と表示文字列を優先する。
/// </summary>
public sealed record ConditionMarker(
    ConditionMarkerType MarkerType,
    string DisplayText,
    QueryExpressionAnalysis? NestedQuery);

/// <summary>
/// GROUP BY の情報。
/// 項目一覧と元の表示文字列を両方持たせ、TreeView と詳細表示の両方に使いやすくする。
/// </summary>
public sealed record GroupByAnalysis(
    IReadOnlyList<string> Items,
    string DisplayText);

/// <summary>
/// ORDER BY の情報。
/// 初期版では表示に必要な順序項目をそのまま保持する。
/// </summary>
public sealed record OrderByAnalysis(
    IReadOnlyList<string> Items,
    string DisplayText);

/// <summary>
/// サブクエリの出現箇所を表す。
/// 「どこにあったか」を明示しておくと、利用者が TreeView を追いやすくなる。
/// </summary>
public sealed record SubqueryAnalysis(
    string Location,
    string DisplayText,
    QueryExpressionAnalysis Query);

/// <summary>
/// パーサーから得た補足情報。
/// 今回は主に未対応構文や複数文入力などを利用者へ穏やかに伝える用途を想定する。
/// </summary>
public sealed record AnalysisNotice(AnalysisNoticeLevel Level, string Message);

/// <summary>
/// 構文エラーの位置情報。
/// GUI 側で将来エディタ連携する際にも使えるよう、行・列を保持する。
/// </summary>
public sealed record ParseIssue(int Line, int Column, string Message);
