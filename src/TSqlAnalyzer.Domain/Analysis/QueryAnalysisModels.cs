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
    Create,
    Update,
    Insert,
    Delete,
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
/// SELECT 項目の種別。
/// 式、ワイルドカード、変数代入を分けておくと表示の粒度を後から上げやすい。
/// </summary>
public enum SelectItemKind
{
    Expression,
    Wildcard,
    VariableAssignment,
    Unknown
}

/// <summary>
/// SELECT ワイルドカード項目の種別。
/// `*` と `table.*` を分けると、取得範囲の違いを追いやすくなる。
/// </summary>
public enum SelectWildcardKind
{
    None,
    AllColumns,
    QualifiedAllColumns
}

/// <summary>
/// CASE 式の形。
/// 値比較 CASE と条件式 CASE を分けることで、TreeView で WHEN の読み方を切り替えられる。
/// </summary>
public enum CaseExpressionKind
{
    Simple,
    Searched
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
/// 列参照のソース解決状態。
/// どのソースを指すか確定したかどうかを区別し、曖昧さも保持する。
/// </summary>
public enum ColumnReferenceResolutionStatus
{
    Unresolved,
    Resolved,
    Ambiguous
}

/// <summary>
/// 列参照が何に解決したかの分類。
/// 通常ソースだけでなく、ORDER BY の SELECT 別名解決も同じ器で扱えるようにする。
/// </summary>
public enum ColumnReferenceResolvedTargetKind
{
    None,
    Source,
    SelectAlias
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
/// DML 文の種別。
/// UPDATE / INSERT / DELETE を独立して持たせることで、画面側の主構造切り替えを単純化する。
/// </summary>
public enum DataModificationKind
{
    Update,
    Insert,
    Delete
}

/// <summary>
/// INSERT の入力元種別。
/// VALUES / SELECT / EXECUTE を区別すると、挿入元の追い方が分かりやすくなる。
/// </summary>
public enum InsertSourceKind
{
    Values,
    Query,
    Execute,
    Unknown
}

/// <summary>
/// CREATE 文の種別。
/// 初期版では VIEW と TABLE を対象にし、他の CREATE 種別は将来拡張へ回す。
/// </summary>
public enum CreateStatementKind
{
    View,
    Table
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
/// 条件式の述語種別。
/// 比較・NULL 判定・LIKE などを分けておくと、巨大な WHERE を俯瞰しやすくなる。
/// </summary>
public enum ConditionPredicateKind
{
    Unknown,
    Comparison,
    NullCheck,
    Like,
    Between,
    Exists,
    In
}

/// <summary>
/// 比較述語の演算子種別。
/// 比較系だけを別軸で持たせることで、「比較」と分かった後に何比較なのかまで辿れる。
/// </summary>
public enum ConditionComparisonKind
{
    Unknown,
    Equal,
    NotEqual,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
    NotLessThan,
    NotGreaterThan,
    IsDistinctFrom,
    IsNotDistinctFrom
}

/// <summary>
/// NULL 判定述語の種別。
/// IS NULL と IS NOT NULL を分けると、削除フラグや終了日時の見分けが付きやすくなる。
/// </summary>
public enum ConditionNullCheckKind
{
    Unknown,
    IsNull,
    IsNotNull
}

/// <summary>
/// BETWEEN 述語の種別。
/// NOT BETWEEN を別扱いにしておくと、条件の意味を読み違えにくくなる。
/// </summary>
public enum ConditionBetweenKind
{
    Unknown,
    Between,
    NotBetween
}

/// <summary>
/// LIKE 述語の種別。
/// NOT LIKE を別扱いにしておくと、否定条件を見落としにくくなる。
/// </summary>
public enum ConditionLikeKind
{
    Unknown,
    Like,
    NotLike
}

/// <summary>
/// ORDER BY の並び方向。
/// ASC / DESC を構造化して保持し、表示やエクスポートで使いやすくする。
/// </summary>
public enum OrderByDirection
{
    Unspecified,
    Ascending,
    Descending
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
/// 元 SQL 文字列上の位置情報。
/// TreeView と入力欄の相互ハイライトに使うため、開始位置と長さを保持する。
/// </summary>
public sealed record TextSpan(int Start, int Length)
{
    public int End => Start + Length;

    public bool Contains(int index)
    {
        return index >= Start && index < End;
    }

    public bool Contains(TextSpan other)
    {
        return other.Start >= Start && other.End <= End;
    }
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
    IReadOnlyList<AnalysisNotice> Notices,
    DataModificationAnalysis? DataModification = null,
    CreateStatementAnalysis? CreateStatement = null);

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
    SourceAnalysis? IntoTarget,
    SourceAnalysis? MainSource,
    IReadOnlyList<JoinAnalysis> Joins,
    ConditionAnalysis? WhereCondition,
    GroupByAnalysis? GroupBy,
    ConditionAnalysis? HavingCondition,
    OrderByAnalysis? OrderBy,
    IReadOnlyList<SubqueryAnalysis> Subqueries,
    TextSpan? SourceSpan = null)
    : QueryExpressionAnalysis(QueryExpressionKind.Select);

/// <summary>
/// CTE の 1 定義を表す。
/// 定義名と内部クエリを保持し、複雑なクエリの前段構造を追えるようにする。
/// </summary>
public sealed record CommonTableExpressionAnalysis(
    string Name,
    IReadOnlyList<string> ColumnNames,
    QueryExpressionAnalysis Query,
    TextSpan? SourceSpan = null);

/// <summary>
/// UNION / EXCEPT / INTERSECT などの集合演算を表す。
/// 左右のクエリを同じ再帰型で保持することで、将来の深い入れ子にも対応しやすくする。
/// </summary>
public sealed record SetOperationQueryAnalysis(
    SetOperationType OperationType,
    QueryExpressionAnalysis LeftQuery,
    QueryExpressionAnalysis RightQuery,
    TextSpan? SourceSpan = null)
    : QueryExpressionAnalysis(QueryExpressionKind.SetOperation);

/// <summary>
/// CREATE 文の共通情報を表す基底型。
/// 作成種別と名前を共通化して、TreeView 側の見出し切り替えを簡潔にする。
/// </summary>
public abstract record CreateStatementAnalysis(
    CreateStatementKind Kind,
    string Name,
    TextSpan? SourceSpan = null);

/// <summary>
/// CREATE VIEW 文の構造を表す。
/// ビュー名と内部 SELECT を保持し、ビュー定義の構造をそのまま辿れるようにする。
/// </summary>
public sealed record CreateViewAnalysis(
    string Name,
    IReadOnlyList<string> ColumnNames,
    QueryExpressionAnalysis Query,
    bool WithCheckOption,
    TextSpan? SourceSpan = null)
    : CreateStatementAnalysis(CreateStatementKind.View, Name, SourceSpan);

/// <summary>
/// CREATE TABLE 文の構造を表す。
/// 列定義に加えて CTAS の内部 SELECT も保持できるようにする。
/// </summary>
public sealed record CreateTableAnalysis(
    string Name,
    IReadOnlyList<CreateTableColumnAnalysis> Columns,
    QueryExpressionAnalysis? Query,
    TextSpan? SourceSpan = null)
    : CreateStatementAnalysis(CreateStatementKind.Table, Name, SourceSpan);

/// <summary>
/// CREATE TABLE の列定義 1 件分を表す。
/// 初期版では列名、データ型、NULL 許可の有無に絞って保持する。
/// </summary>
public sealed record CreateTableColumnAnalysis(
    int Sequence,
    string Name,
    string DataType,
    bool IsNullable,
    string DisplayText,
    TextSpan? SourceSpan = null);

/// <summary>
/// UPDATE / INSERT / DELETE の共通情報を表す基底型。
/// 対象・TOP・OUTPUT・サブクエリは DML 間で共通になりやすいため、ここへ寄せる。
/// </summary>
public abstract record DataModificationAnalysis(
    DataModificationKind Kind,
    SourceAnalysis Target,
    string? TopExpressionText,
    string? OutputClauseText,
    string? OutputIntoClauseText,
    IReadOnlyList<SubqueryAnalysis> Subqueries,
    TextSpan? SourceSpan = null);

/// <summary>
/// UPDATE 文の構造を表す。
/// 対象、SET、FROM / JOIN、WHERE を分けて保持し、後から表示粒度を上げやすくする。
/// </summary>
public sealed record UpdateStatementAnalysis(
    SourceAnalysis Target,
    string? TopExpressionText,
    IReadOnlyList<UpdateSetClauseAnalysis> SetClauses,
    SourceAnalysis? MainSource,
    IReadOnlyList<JoinAnalysis> Joins,
    ConditionAnalysis? WhereCondition,
    string? OutputClauseText,
    string? OutputIntoClauseText,
    IReadOnlyList<SubqueryAnalysis> Subqueries,
    TextSpan? SourceSpan = null)
    : DataModificationAnalysis(
        DataModificationKind.Update,
        Target,
        TopExpressionText,
        OutputClauseText,
        OutputIntoClauseText,
        Subqueries,
        SourceSpan);

/// <summary>
/// INSERT 文の構造を表す。
/// 挿入先列と入力元を分けて持たせることで、VALUES と SELECT の違いを表示しやすくする。
/// </summary>
public sealed record InsertStatementAnalysis(
    SourceAnalysis Target,
    string? TopExpressionText,
    string? InsertOptionText,
    IReadOnlyList<string> TargetColumns,
    InsertSourceAnalysis? InsertSource,
    string? OutputClauseText,
    string? OutputIntoClauseText,
    IReadOnlyList<SubqueryAnalysis> Subqueries,
    TextSpan? SourceSpan = null)
    : DataModificationAnalysis(
        DataModificationKind.Insert,
        Target,
        TopExpressionText,
        OutputClauseText,
        OutputIntoClauseText,
        Subqueries,
        SourceSpan);

/// <summary>
/// DELETE 文の構造を表す。
/// 対象、FROM / JOIN、WHERE を独立して持たせることで、UPDATE と似た見せ方を再利用できる。
/// </summary>
public sealed record DeleteStatementAnalysis(
    SourceAnalysis Target,
    string? TopExpressionText,
    SourceAnalysis? MainSource,
    IReadOnlyList<JoinAnalysis> Joins,
    ConditionAnalysis? WhereCondition,
    string? OutputClauseText,
    string? OutputIntoClauseText,
    IReadOnlyList<SubqueryAnalysis> Subqueries,
    TextSpan? SourceSpan = null)
    : DataModificationAnalysis(
        DataModificationKind.Delete,
        Target,
        TopExpressionText,
        OutputClauseText,
        OutputIntoClauseText,
        Subqueries,
        SourceSpan);

/// <summary>
/// UPDATE の SET 句 1 件分を表す。
/// 列名と値式を分けて持たせることで、TreeView で更新内容を整理して並べやすくする。
/// </summary>
public sealed record UpdateSetClauseAnalysis(
    int Sequence,
    string DisplayText,
    string TargetText,
    string ValueText,
    TextSpan? SourceSpan = null);

/// <summary>
/// INSERT 時の列と値の対応 1 件分。
/// どの列へどの値が入るかを、行単位で見やすく表示するために使う。
/// </summary>
public sealed record InsertValueMappingAnalysis(
    int Sequence,
    string TargetColumn,
    string ValueText,
    TextSpan? SourceSpan = null);

/// <summary>
/// INSERT 値対応の 1 グループ分。
/// VALUES の複数行や SELECT 由来の 1 セットを区別して表示するために使う。
/// </summary>
public sealed record InsertValueMappingGroupAnalysis(
    string Title,
    IReadOnlyList<InsertValueMappingAnalysis> Mappings,
    TextSpan? SourceSpan = null);

/// <summary>
/// INSERT の入力元情報。
/// SELECT 由来か VALUES 由来かを区別しつつ、必要なら内部クエリも保持する。
/// </summary>
public sealed record InsertSourceAnalysis(
    InsertSourceKind SourceKind,
    string DisplayText,
    IReadOnlyList<string> Items,
    QueryExpressionAnalysis? Query,
    string? ExecuteText,
    IReadOnlyList<InsertValueMappingGroupAnalysis> MappingGroups,
    TextSpan? SourceSpan = null);

/// <summary>
/// SELECT 項目を表す。
/// 表示文字列に加えて、式本体・別名・集計関数を分解して保持する。
/// </summary>
public sealed record SelectItemAnalysis(
    int Sequence,
    string DisplayText,
    SelectItemKind Kind,
    string ExpressionText,
    string? Alias,
    string? AggregateFunctionName,
    SelectWildcardKind WildcardKind,
    string? WildcardQualifier,
    TextSpan? SourceSpan = null)
{
    public IReadOnlyList<ColumnReferenceAnalysis> ColumnReferences { get; init; } = [];
    public IReadOnlyList<CaseExpressionAnalysis> CaseExpressions { get; init; } = [];
}

/// <summary>
/// CASE 式 1 件分の解析結果。
/// 値比較 CASE では比較対象を持ち、条件式 CASE では WHEN 条件を個別に持つ。
/// </summary>
public sealed record CaseExpressionAnalysis(
    int Sequence,
    CaseExpressionKind Kind,
    string DisplayText,
    string? InputExpressionText,
    IReadOnlyList<CaseWhenClauseAnalysis> WhenClauses,
    string? ElseExpressionText,
    TextSpan? SourceSpan = null);

/// <summary>
/// CASE 式の WHEN / THEN 1 件分。
/// Simple CASE では WhenText が比較値、Searched CASE では条件式になる。
/// </summary>
public sealed record CaseWhenClauseAnalysis(
    int Sequence,
    string WhenText,
    string ThenText,
    TextSpan? SourceSpan = null);

/// <summary>
/// FROM 句や JOIN 先として使われるソースを表す。
/// 通常テーブルだけでなく、派生テーブルやサブクエリ結果も保持できるようにしている。
/// </summary>
public sealed record SourceAnalysis(
    string DisplayText,
    QueryExpressionAnalysis? NestedQuery,
    SourceKind SourceKind,
    string? SourceName,
    string? Alias = null,
    TextSpan? SourceSpan = null)
{
    public IReadOnlyList<string> ExposedColumnNames { get; init; } = [];
}

/// <summary>
/// JOIN の ON 条件を分割表示するための 1 条件分。
/// AND で連結された条件を見やすく 1 行ずつ保持する。
/// </summary>
public sealed record JoinConditionPartAnalysis(
    int Sequence,
    string DisplayText,
    TextSpan? SourceSpan = null)
{
    public IReadOnlyList<ColumnReferenceAnalysis> ColumnReferences { get; init; } = [];
    public IReadOnlyList<CaseExpressionAnalysis> CaseExpressions { get; init; } = [];
}

/// <summary>
/// JOIN 表示に必要な最小情報。
/// 重要方針に合わせて、種別・結合先・ON 条件に責務を絞っている。
/// </summary>
public sealed record JoinAnalysis(
    int Sequence,
    JoinType JoinType,
    string JoinTypeText,
    SourceAnalysis TargetSource,
    string? OnConditionText,
    IReadOnlyList<JoinConditionPartAnalysis> OnConditionParts,
    TextSpan? SourceSpan = null);

/// <summary>
/// WHERE 句などの条件本体。
/// 条件式全文に加えて、EXISTS / IN などの構造上の目印を分けて保持する。
/// </summary>
public sealed record ConditionAnalysis(
    string DisplayText,
    ConditionNodeAnalysis RootNode,
    IReadOnlyList<ConditionMarker> Markers,
    TextSpan? SourceSpan = null)
{
    public IReadOnlyList<ColumnReferenceAnalysis> ColumnReferences { get; init; } = [];
    public IReadOnlyList<CaseExpressionAnalysis> CaseExpressions { get; init; } = [];
}

/// <summary>
/// 条件式の論理木 1 ノード分。
/// マーカー付き述語もこのノードにぶら下げることで、条件種別一覧と論理木を両立できる。
/// </summary>
public sealed record ConditionNodeAnalysis(
    ConditionNodeKind NodeKind,
    string DisplayText,
    IReadOnlyList<ConditionNodeAnalysis> Children,
    ConditionPredicateKind PredicateKind,
    ConditionComparisonKind ComparisonKind,
    ConditionNullCheckKind NullCheckKind,
    ConditionBetweenKind BetweenKind,
    ConditionLikeKind LikeKind,
    bool IsParenthesized,
    ConditionMarker? Marker,
    TextSpan? SourceSpan = null)
{
    public IReadOnlyList<ColumnReferenceAnalysis> ColumnReferences { get; init; } = [];
    public IReadOnlyList<CaseExpressionAnalysis> CaseExpressions { get; init; } = [];
}

/// <summary>
/// 式の中で参照されている列 1 件分。
/// 別名や列名を分けて保持し、どのソースのどの列かを追いやすくする。
/// </summary>
public sealed record ColumnReferenceAnalysis(
    int Sequence,
    string DisplayText,
    string? Qualifier,
    string ColumnName,
    TextSpan? SourceSpan = null)
{
    public ColumnReferenceResolutionStatus ResolutionStatus { get; init; } = ColumnReferenceResolutionStatus.Unresolved;
    public ColumnReferenceResolvedTargetKind ResolvedTargetKind { get; init; } = ColumnReferenceResolvedTargetKind.None;
    public string? ResolvedTargetDisplayText { get; init; }
    public string? ResolvedSelectItemAlias { get; init; }
    public string? ResolvedSourceDisplayText { get; init; }
    public string? ResolvedSourceName { get; init; }
    public string? ResolvedSourceAlias { get; init; }
    public SourceKind? ResolvedSourceKind { get; init; }
}

/// <summary>
/// GROUP BY 項目 1 件分。
/// 項目式と参照列を分けて持たせることで、後から表示粒度を上げやすくする。
/// </summary>
public sealed record GroupByItemAnalysis(
    int Sequence,
    string DisplayText,
    string ExpressionText,
    TextSpan? SourceSpan = null)
{
    public IReadOnlyList<ColumnReferenceAnalysis> ColumnReferences { get; init; } = [];
}

/// <summary>
/// 条件式の中で検出した注目ポイント。
/// 将来は論理木へ発展させる余地を残しつつ、初期版では種類と表示文字列を優先する。
/// </summary>
public sealed record ConditionMarker(
    ConditionMarkerType MarkerType,
    string DisplayText,
    QueryExpressionAnalysis? NestedQuery,
    TextSpan? SourceSpan = null);

/// <summary>
/// GROUP BY の情報。
/// 項目一覧と元の表示文字列を両方持たせ、TreeView と詳細表示の両方に使いやすくする。
/// </summary>
public sealed record GroupByAnalysis(
    IReadOnlyList<string> Items,
    string DisplayText,
    TextSpan? SourceSpan = null)
{
    public IReadOnlyList<GroupByItemAnalysis> GroupingItems { get; init; } = [];
    public IReadOnlyList<ColumnReferenceAnalysis> ColumnReferences { get; init; } = [];
}

/// <summary>
/// ORDER BY 項目 1 件分。
/// 式と並び方向を構造化して保持し、画面で分けて表示できるようにする。
/// </summary>
public sealed record OrderByItemAnalysis(
    int Sequence,
    string DisplayText,
    string ExpressionText,
    OrderByDirection Direction,
    TextSpan? SourceSpan = null)
{
    public IReadOnlyList<ColumnReferenceAnalysis> ColumnReferences { get; init; } = [];
}

/// <summary>
/// ORDER BY の情報。
/// 初期版では表示に必要な順序項目をそのまま保持する。
/// </summary>
public sealed record OrderByAnalysis(
    IReadOnlyList<string> Items,
    string DisplayText,
    TextSpan? SourceSpan = null)
{
    public IReadOnlyList<OrderByItemAnalysis> OrderItems { get; init; } = [];
    public IReadOnlyList<ColumnReferenceAnalysis> ColumnReferences { get; init; } = [];
}

/// <summary>
/// サブクエリの出現箇所を表す。
/// 「どこにあったか」を明示しておくと、利用者が TreeView を追いやすくなる。
/// </summary>
public sealed record SubqueryAnalysis(
    string Location,
    string DisplayText,
    QueryExpressionAnalysis Query,
    TextSpan? SourceSpan = null);

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
