using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Application.Formatting;

/// <summary>
/// T-SQL を読みやすい形へ整形する。
/// 複雑な SELECT 系は句構造を意識して自前整形し、それ以外は ScriptDom の生成結果へ委譲する。
/// </summary>
public sealed class SqlFormattingService
{
    private const int IndentationSize = 4;

    private readonly TSql160Parser _parser;
    private readonly Sql160ScriptGenerator _fallbackGenerator;
    private readonly Sql160ScriptGenerator _leafGenerator;

    /// <summary>
    /// 整形に使うパーサーとジェネレーターを初期化する。
    /// </summary>
    public SqlFormattingService()
    {
        _parser = new TSql160Parser(initialQuotedIdentifiers: false);
        _fallbackGenerator = new Sql160ScriptGenerator(CreateFallbackOptions());
        _leafGenerator = new Sql160ScriptGenerator(CreateLeafOptions());
    }

    /// <summary>
    /// SQL を整形する。
    /// 構文エラーがある場合は元の SQL を保ったまま失敗結果を返す。
    /// </summary>
    public SqlFormatResult Format(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new SqlFormatResult(true, string.Empty, []);
        }

        using var reader = new StringReader(sql);
        var fragment = _parser.Parse(reader, out IList<ParseError> parseErrors);
        if (parseErrors.Count > 0 || fragment is null)
        {
            return new SqlFormatResult(
                false,
                sql,
                parseErrors
                    .Select(error => new ParseIssue(error.Line, error.Column, error.Message))
                    .ToArray());
        }

        return new SqlFormatResult(
            true,
            FormatFragment(fragment),
            []);
    }

    /// <summary>
    /// 断片全体を整形する。
    /// 複数文入力では文ごとに空行で区切る。
    /// </summary>
    private string FormatFragment(TSqlFragment fragment)
    {
        if (fragment is TSqlScript script)
        {
            var statements = script.Batches
                .SelectMany(batch => batch.Statements)
                .ToArray();

            if (statements.Length == 0)
            {
                return string.Empty;
            }

            return string.Join(
                Environment.NewLine + Environment.NewLine,
                statements.Select(FormatStatement));
        }

        if (fragment is TSqlStatement statement)
        {
            return FormatStatement(statement);
        }

        return NormalizeGeneratedStatementText(GenerateFallbackScript(fragment));
    }

    /// <summary>
    /// 文単位の整形を行う。
    /// SELECT 系は階層構造が見えるよう自前整形し、他は既定ジェネレーターへ委譲する。
    /// </summary>
    private string FormatStatement(TSqlStatement statement)
    {
        return statement switch
        {
            SelectStatement selectStatement when selectStatement.QueryExpression is not null
                => EnsureSemicolon(string.Join(Environment.NewLine, FormatSelectStatement(selectStatement, 0))),
            _ => EnsureSemicolon(NormalizeGeneratedStatementText(GenerateFallbackScript(statement)))
        };
    }

    /// <summary>
    /// SELECT 文全体を整形する。
    /// WITH 句と本体クエリを連結し、必要なら SELECT INTO もここで差し込む。
    /// </summary>
    private List<string> FormatSelectStatement(SelectStatement selectStatement, int indentLevel)
    {
        var lines = new List<string>();
        AppendWithClause(lines, selectStatement.WithCtesAndXmlNamespaces, indentLevel);

        if (selectStatement.Into is not null
            && selectStatement.QueryExpression is QuerySpecification querySpecification)
        {
            lines.AddRange(FormatQuerySpecification(
                querySpecification,
                indentLevel,
                GenerateLeafScript(selectStatement.Into)));
            return lines;
        }

        lines.AddRange(FormatQueryExpression(selectStatement.QueryExpression!, indentLevel));
        return lines;
    }

    /// <summary>
    /// WITH 句の CTE 群を整形する。
    /// 各 CTE を独立ブロックとして並べ、内部クエリの入れ子を追いやすくする。
    /// </summary>
    private void AppendWithClause(List<string> lines, WithCtesAndXmlNamespaces? withClause, int indentLevel)
    {
        if (withClause?.CommonTableExpressions is not { Count: > 0 } commonTableExpressions)
        {
            return;
        }

        for (var index = 0; index < commonTableExpressions.Count; index++)
        {
            var commonTableExpression = commonTableExpressions[index];
            var header = index == 0 ? "WITH " : ", ";
            header += commonTableExpression.ExpressionName.Value;

            if (commonTableExpression.Columns is { Count: > 0 } columns)
            {
                header += " (" + string.Join(", ", columns.Select(column => column.Value)) + ")";
            }

            header += " AS (";
            lines.Add(Indent(indentLevel) + header);
            lines.AddRange(FormatQueryExpression(commonTableExpression.QueryExpression, indentLevel + 1));
            lines.Add(Indent(indentLevel) + ")");
        }
    }

    /// <summary>
    /// QueryExpression を種別ごとに整形する。
    /// SELECT、集合演算、括弧付きクエリを分けて扱う。
    /// </summary>
    private List<string> FormatQueryExpression(QueryExpression queryExpression, int indentLevel)
    {
        return queryExpression switch
        {
            QuerySpecification querySpecification
                => FormatQuerySpecification(querySpecification, indentLevel),
            BinaryQueryExpression binaryQueryExpression
                => FormatBinaryQueryExpression(binaryQueryExpression, indentLevel),
            QueryParenthesisExpression queryParenthesisExpression
                => FormatQueryParenthesisExpression(queryParenthesisExpression, indentLevel),
            _ => [Indent(indentLevel) + GenerateLeafScript(queryExpression)]
        };
    }

    /// <summary>
    /// 単一 SELECT を句単位で整形する。
    /// 取得項目、FROM、JOIN、WHERE、GROUP BY、HAVING、ORDER BY を順に積み上げる。
    /// </summary>
    private List<string> FormatQuerySpecification(
        QuerySpecification querySpecification,
        int indentLevel,
        string? intoTarget = null)
    {
        var lines = new List<string>
        {
            BuildSelectHeaderLine(querySpecification, indentLevel)
        };

        AppendSelectElements(lines, querySpecification.SelectElements, indentLevel + 1);

        if (!string.IsNullOrWhiteSpace(intoTarget))
        {
            lines.Add(Indent(indentLevel) + "INTO " + intoTarget);
        }

        AppendFromClause(lines, querySpecification.FromClause, indentLevel);
        AppendBooleanClause(lines, "WHERE", querySpecification.WhereClause?.SearchCondition, indentLevel);
        AppendGroupingClause(lines, querySpecification.GroupByClause, indentLevel);
        AppendBooleanClause(lines, "HAVING", querySpecification.HavingClause?.SearchCondition, indentLevel);
        AppendOrderByClause(lines, querySpecification.OrderByClause, indentLevel);

        return lines;
    }

    /// <summary>
    /// 集合演算を左右のクエリブロックへ分けて整形する。
    /// UNION / EXCEPT / INTERSECT を演算子行として中央へ置く。
    /// </summary>
    private List<string> FormatBinaryQueryExpression(BinaryQueryExpression binaryQueryExpression, int indentLevel)
    {
        var lines = new List<string>();
        lines.AddRange(FormatQueryExpression(binaryQueryExpression.FirstQueryExpression, indentLevel));
        lines.Add(Indent(indentLevel) + FormatSetOperation(binaryQueryExpression));
        lines.AddRange(FormatQueryExpression(binaryQueryExpression.SecondQueryExpression, indentLevel));
        AppendOrderByClause(lines, binaryQueryExpression.OrderByClause, indentLevel);
        return lines;
    }

    /// <summary>
    /// 括弧付きクエリを整形する。
    /// 内部クエリは 1 段深くする。
    /// </summary>
    private List<string> FormatQueryParenthesisExpression(QueryParenthesisExpression queryParenthesisExpression, int indentLevel)
    {
        var lines = new List<string>
        {
            Indent(indentLevel) + "("
        };

        lines.AddRange(FormatQueryExpression(queryParenthesisExpression.QueryExpression, indentLevel + 1));
        lines.Add(Indent(indentLevel) + ")");
        return lines;
    }

    /// <summary>
    /// SELECT 行のヘッダーを組み立てる。
    /// DISTINCT や TOP は SELECT と同じ行へ置く。
    /// </summary>
    private string BuildSelectHeaderLine(QuerySpecification querySpecification, int indentLevel)
    {
        var parts = new List<string> { "SELECT" };

        if (querySpecification.UniqueRowFilter == UniqueRowFilter.Distinct)
        {
            parts.Add("DISTINCT");
        }

        if (querySpecification.TopRowFilter is not null)
        {
            parts.Add(BuildTopExpression(querySpecification.TopRowFilter));
        }

        return Indent(indentLevel) + string.Join(" ", parts);
    }

    /// <summary>
    /// SELECT 項目一覧を整形する。
    /// CASE やサブクエリを含む項目は複数行ブロックで出力する。
    /// </summary>
    private void AppendSelectElements(List<string> lines, IList<SelectElement> selectElements, int indentLevel)
    {
        for (var index = 0; index < selectElements.Count; index++)
        {
            var elementLines = FormatSelectElement(selectElements[index], indentLevel);
            AppendCommaToLastLine(elementLines, index < selectElements.Count - 1);
            lines.AddRange(elementLines);
        }
    }

    /// <summary>
    /// 単一の SELECT 項目を整形する。
    /// 単純式は 1 行、CASE やサブクエリは複数行で返す。
    /// </summary>
    private List<string> FormatSelectElement(SelectElement selectElement, int indentLevel)
    {
        return selectElement switch
        {
            SelectScalarExpression scalarExpression => FormatSelectScalarExpression(scalarExpression, indentLevel),
            _ => [Indent(indentLevel) + GenerateLeafScript(selectElement)]
        };
    }

    /// <summary>
    /// 式付きの SELECT 項目を整形する。
    /// CASE とサブクエリだけ特別扱いし、他は単純式として扱う。
    /// </summary>
    private List<string> FormatSelectScalarExpression(SelectScalarExpression scalarExpression, int indentLevel)
    {
        var aliasText = scalarExpression.ColumnName is null
            ? null
            : GenerateLeafScript(scalarExpression.ColumnName);
        var expressionLines = FormatScalarExpressionBlock(scalarExpression.Expression, indentLevel);
        if (!string.IsNullOrWhiteSpace(aliasText))
        {
            expressionLines[^1] += " AS " + aliasText;
        }

        return expressionLines;
    }

    /// <summary>
    /// スカラー式を整形する。
    /// CASE、サブクエリ、関数引数内の複雑式は複数行ブロックへ開く。
    /// </summary>
    private List<string> FormatScalarExpressionBlock(ScalarExpression expression, int indentLevel)
    {
        switch (expression)
        {
            case CaseExpression caseExpression:
                return FormatCaseExpression(caseExpression, indentLevel);

            case ScalarSubquery scalarSubquery:
                return FormatScalarSubquery(scalarSubquery, indentLevel);

            case ParenthesisExpression parenthesisExpression
                when RequiresMultilineScalarExpression(parenthesisExpression.Expression):
                return FormatParenthesizedScalarExpression(parenthesisExpression, indentLevel);

            case FunctionCall functionCall
                when ShouldFormatFunctionCallAsMultiline(functionCall):
                return FormatFunctionCall(functionCall, indentLevel);

            case CoalesceExpression coalesceExpression
                when coalesceExpression.Expressions.Any(RequiresMultilineScalarExpression):
                return FormatNamedExpressionList(
                    "COALESCE",
                    coalesceExpression.Expressions,
                    indentLevel);

            case NullIfExpression nullIfExpression
                when RequiresMultilineScalarExpression(nullIfExpression.FirstExpression)
                    || RequiresMultilineScalarExpression(nullIfExpression.SecondExpression):
                return FormatNamedExpressionList(
                    "NULLIF",
                    [nullIfExpression.FirstExpression, nullIfExpression.SecondExpression],
                    indentLevel);

            default:
                return [Indent(indentLevel) + GenerateLeafScript(expression)];
        }
    }

    /// <summary>
    /// CASE 式を多段ブロックへ開く。
    /// WHEN / THEN / ELSE / END を揃え、条件の切れ目を追いやすくする。
    /// </summary>
    private List<string> FormatCaseExpression(CaseExpression caseExpression, int indentLevel)
    {
        var lines = new List<string>();

        switch (caseExpression)
        {
            case SimpleCaseExpression simpleCaseExpression:
                lines.Add(Indent(indentLevel) + "CASE " + GenerateLeafScript(simpleCaseExpression.InputExpression));
                foreach (var whenClause in simpleCaseExpression.WhenClauses)
                {
                    lines.Add(
                        Indent(indentLevel + 1)
                        + "WHEN "
                        + GenerateLeafScript(whenClause.WhenExpression)
                        + " THEN "
                        + GenerateLeafScript(whenClause.ThenExpression));
                }

                if (simpleCaseExpression.ElseExpression is not null)
                {
                    lines.Add(Indent(indentLevel + 1) + "ELSE " + GenerateLeafScript(simpleCaseExpression.ElseExpression));
                }

                break;

            case SearchedCaseExpression searchedCaseExpression:
                lines.Add(Indent(indentLevel) + "CASE");
                foreach (var whenClause in searchedCaseExpression.WhenClauses)
                {
                    lines.Add(
                        Indent(indentLevel + 1)
                        + "WHEN "
                        + GenerateLeafScript(whenClause.WhenExpression)
                        + " THEN "
                        + GenerateLeafScript(whenClause.ThenExpression));
                }

                if (searchedCaseExpression.ElseExpression is not null)
                {
                    lines.Add(Indent(indentLevel + 1) + "ELSE " + GenerateLeafScript(searchedCaseExpression.ElseExpression));
                }

                break;

            default:
                return [Indent(indentLevel) + GenerateLeafScript(caseExpression)];
        }

        lines.Add(Indent(indentLevel) + "END");
        return lines;
    }

    /// <summary>
    /// スカラーサブクエリを複数行で整形する。
    /// 括弧内クエリは 1 段深くする。
    /// </summary>
    private List<string> FormatScalarSubquery(ScalarSubquery scalarSubquery, int indentLevel)
    {
        var lines = new List<string>
        {
            Indent(indentLevel) + "("
        };

        lines.AddRange(FormatQueryExpression(scalarSubquery.QueryExpression, indentLevel + 1));
        lines.Add(Indent(indentLevel) + ")");
        return lines;
    }

    /// <summary>
    /// 複雑なスカラー式を囲む括弧を独立行で整形する。
    /// </summary>
    private List<string> FormatParenthesizedScalarExpression(ParenthesisExpression parenthesisExpression, int indentLevel)
    {
        var lines = new List<string>
        {
            Indent(indentLevel) + "("
        };

        lines.AddRange(FormatScalarExpressionBlock(parenthesisExpression.Expression, indentLevel + 1));
        lines.Add(Indent(indentLevel) + ")");
        return lines;
    }

    /// <summary>
    /// 複数引数を取る関数呼び出しを整形する。
    /// 複雑な引数がある場合だけ複数行へ展開する。
    /// </summary>
    private List<string> FormatFunctionCall(FunctionCall functionCall, int indentLevel)
    {
        var functionName = BuildFunctionCallName(functionCall);
        var lines = new List<string>
        {
            Indent(indentLevel) + functionName + "("
        };

        for (var index = 0; index < functionCall.Parameters.Count; index++)
        {
            var parameterLines = FormatScalarExpressionBlock(functionCall.Parameters[index], indentLevel + 1);

            if (index == 0 && functionCall.UniqueRowFilter == UniqueRowFilter.Distinct)
            {
                parameterLines[0] = Indent(indentLevel + 1) + "DISTINCT " + parameterLines[0].TrimStart();
            }

            AppendCommaToLastLine(parameterLines, index < functionCall.Parameters.Count - 1);
            lines.AddRange(parameterLines);
        }

        lines.Add(Indent(indentLevel) + ")");
        return lines;
    }

    /// <summary>
    /// COALESCE や NULLIF のような固定名の式リストを整形する。
    /// </summary>
    private List<string> FormatNamedExpressionList(string functionName, IEnumerable<ScalarExpression> expressions, int indentLevel)
    {
        var expressionList = expressions.ToArray();
        var lines = new List<string>
        {
            Indent(indentLevel) + functionName + "("
        };

        for (var index = 0; index < expressionList.Length; index++)
        {
            var expressionLines = FormatScalarExpressionBlock(expressionList[index], indentLevel + 1);
            AppendCommaToLastLine(expressionLines, index < expressionList.Length - 1);
            lines.AddRange(expressionLines);
        }

        lines.Add(Indent(indentLevel) + ")");
        return lines;
    }

    /// <summary>
    /// FROM 句を整形する。
    /// JOIN ツリーは主ソースと JOIN ブロックへ分ける。
    /// </summary>
    private void AppendFromClause(List<string> lines, FromClause? fromClause, int indentLevel)
    {
        if (fromClause?.TableReferences is not { Count: > 0 } tableReferences)
        {
            return;
        }

        lines.AddRange(FormatFromTableReference(tableReferences[0], indentLevel, "FROM"));

        for (var index = 1; index < tableReferences.Count; index++)
        {
            var additionalLines = FormatTableSourceWithLeader(tableReferences[index], indentLevel + 1, ",");
            lines.AddRange(additionalLines);
        }
    }

    /// <summary>
    /// FROM 句のテーブル参照を整形する。
    /// JOIN チェーンは再帰的に展開する。
    /// </summary>
    private List<string> FormatFromTableReference(TableReference tableReference, int indentLevel, string leader)
    {
        return tableReference switch
        {
            QualifiedJoin qualifiedJoin => FormatQualifiedJoin(qualifiedJoin, indentLevel, leader),
            UnqualifiedJoin unqualifiedJoin => FormatUnqualifiedJoin(unqualifiedJoin, indentLevel, leader),
            _ => FormatTableSourceWithLeader(tableReference, indentLevel, leader)
        };
    }

    /// <summary>
    /// INNER / LEFT / RIGHT / FULL JOIN を整形する。
    /// ON 条件は JOIN より 1 段深くし、AND / OR を分割する。
    /// </summary>
    private List<string> FormatQualifiedJoin(QualifiedJoin qualifiedJoin, int indentLevel, string leader)
    {
        var lines = FormatFromTableReference(qualifiedJoin.FirstTableReference, indentLevel, leader);
        lines.AddRange(FormatTableSourceWithLeader(
            qualifiedJoin.SecondTableReference,
            indentLevel + 1,
            FormatQualifiedJoinType(qualifiedJoin.QualifiedJoinType)));

        if (qualifiedJoin.SearchCondition is not null)
        {
            lines.AddRange(FormatBooleanClauseLines(
                "ON",
                qualifiedJoin.SearchCondition,
                indentLevel + 2));
        }

        return lines;
    }

    /// <summary>
    /// CROSS JOIN / APPLY 系を整形する。
    /// APPLY は既定生成結果に寄せた表現をそのまま使う。
    /// </summary>
    private List<string> FormatUnqualifiedJoin(UnqualifiedJoin unqualifiedJoin, int indentLevel, string leader)
    {
        var lines = FormatFromTableReference(unqualifiedJoin.FirstTableReference, indentLevel, leader);
        lines.AddRange(FormatTableSourceWithLeader(
            unqualifiedJoin.SecondTableReference,
            indentLevel + 1,
            FormatUnqualifiedJoinType(unqualifiedJoin.UnqualifiedJoinType)));
        return lines;
    }

    /// <summary>
    /// 1 つのソースをリーダー付きで整形する。
    /// 派生テーブルは内部クエリを展開する。
    /// </summary>
    private List<string> FormatTableSourceWithLeader(TableReference tableReference, int indentLevel, string leader)
    {
        if (tableReference is QueryDerivedTable queryDerivedTable)
        {
            var lines = new List<string>
            {
                Indent(indentLevel) + leader + " ("
            };

            lines.AddRange(FormatQueryExpression(queryDerivedTable.QueryExpression, indentLevel + 1));

            var closing = Indent(indentLevel) + ")";
            if (queryDerivedTable.Alias is not null)
            {
                closing += " " + queryDerivedTable.Alias.Value;
            }

            if (queryDerivedTable.Columns is { Count: > 0 } columns)
            {
                closing += " (" + string.Join(", ", columns.Select(column => column.Value)) + ")";
            }

            lines.Add(closing);
            return lines;
        }

        return [Indent(indentLevel) + leader + " " + GenerateLeafScript(tableReference)];
    }

    /// <summary>
    /// WHERE / HAVING / ON などの論理式ブロックを整形する。
    /// AND / OR は演算子ごとに改行し、EXISTS の中のサブクエリは展開する。
    /// </summary>
    private void AppendBooleanClause(List<string> lines, string clauseKeyword, BooleanExpression? expression, int indentLevel)
    {
        if (expression is null)
        {
            return;
        }

        lines.AddRange(FormatBooleanClauseLines(clauseKeyword, expression, indentLevel));
    }

    /// <summary>
    /// 論理式をリスト形式の行へ整形する。
    /// 先頭条件には句名を置き、続く条件は AND / OR を見出しとして揃える。
    /// </summary>
    private List<string> FormatBooleanClauseLines(string clauseKeyword, BooleanExpression expression, int indentLevel)
    {
        var lines = new List<string>
        {
            Indent(indentLevel) + clauseKeyword
        };

        if (TryFlattenLogicalChain(expression, out var operatorKeyword, out var operands))
        {
            lines.AddRange(FormatBooleanOperandBlock(operands[0], indentLevel + 1, prefixOperator: null));

            for (var index = 1; index < operands.Count; index++)
            {
                lines.AddRange(FormatBooleanOperandBlock(
                    operands[index],
                    indentLevel + 1,
                    prefixOperator: operatorKeyword));
            }

            return lines;
        }

        lines.AddRange(FormatBooleanOperandBlock(expression, indentLevel + 1, prefixOperator: null));
        return lines;
    }

    /// <summary>
    /// 単一条件をブロックとして整形する。
    /// EXISTS / NOT EXISTS / IN サブクエリや、複雑な LIKE / BETWEEN / IS NULL も内部構造まで展開する。
    /// </summary>
    private List<string> FormatBooleanOperandBlock(BooleanExpression expression, int indentLevel, string? prefixOperator)
    {
        switch (expression)
        {
            case BooleanComparisonExpression comparisonExpression
                when RequiresMultilineScalarExpression(comparisonExpression.FirstExpression)
                    || RequiresMultilineScalarExpression(comparisonExpression.SecondExpression):
                return FormatBooleanComparisonExpression(comparisonExpression, indentLevel, prefixOperator);

            case BooleanParenthesisExpression booleanParenthesisExpression:
                return FormatParenthesizedBooleanExpression(
                    booleanParenthesisExpression.Expression,
                    indentLevel,
                    prefixOperator,
                    negated: false);

            case LikePredicate likePredicate
                when RequiresMultilineScalarExpression(likePredicate.FirstExpression)
                    || RequiresMultilineScalarExpression(likePredicate.SecondExpression)
                    || (likePredicate.EscapeExpression is not null
                        && RequiresMultilineScalarExpression(likePredicate.EscapeExpression)):
                return FormatLikePredicate(likePredicate, indentLevel, prefixOperator);

            case BooleanTernaryExpression ternaryExpression
                when RequiresMultilineScalarExpression(ternaryExpression.FirstExpression)
                    || RequiresMultilineScalarExpression(ternaryExpression.SecondExpression)
                    || RequiresMultilineScalarExpression(ternaryExpression.ThirdExpression):
                return FormatBooleanTernaryExpression(ternaryExpression, indentLevel, prefixOperator);

            case BooleanIsNullExpression isNullExpression
                when RequiresMultilineScalarExpression(isNullExpression.Expression):
                return FormatBooleanIsNullExpression(isNullExpression, indentLevel, prefixOperator);

            case ExistsPredicate existsPredicate:
                return FormatExistsPredicate(existsPredicate.Subquery, indentLevel, negated: false, prefixOperator);

            case BooleanNotExpression
                {
                    Expression: ExistsPredicate existsPredicate
                }:
                return FormatExistsPredicate(existsPredicate.Subquery, indentLevel, negated: true, prefixOperator);

            case BooleanNotExpression
                {
                    Expression: BooleanParenthesisExpression booleanParenthesisExpression
                }:
                return FormatParenthesizedBooleanExpression(
                    booleanParenthesisExpression.Expression,
                    indentLevel,
                    prefixOperator,
                    negated: true);

            case BooleanNotExpression
                {
                    Expression: BooleanBinaryExpression booleanBinaryExpression
                }:
                return FormatParenthesizedBooleanExpression(
                    booleanBinaryExpression,
                    indentLevel,
                    prefixOperator,
                    negated: true);

            case InPredicate inPredicate when inPredicate.Subquery is not null:
                return FormatInPredicate(inPredicate, indentLevel, prefixOperator);

            default:
                var text = GenerateLeafScript(expression);
                if (!string.IsNullOrWhiteSpace(prefixOperator))
                {
                    text = prefixOperator + " " + text;
                }

                return [Indent(indentLevel) + text];
        }
    }

    /// <summary>
    /// 複雑な比較条件を整形する。
    /// CASE やサブクエリを含む左右を崩さず、比較演算子だけ末尾へ連結する。
    /// </summary>
    private List<string> FormatBooleanComparisonExpression(
        BooleanComparisonExpression comparisonExpression,
        int indentLevel,
        string? prefixOperator)
    {
        var leftIsComplex = RequiresMultilineScalarExpression(comparisonExpression.FirstExpression);
        var rightIsComplex = RequiresMultilineScalarExpression(comparisonExpression.SecondExpression);
        var operatorText = FormatComparisonOperator(comparisonExpression.ComparisonType);

        if (leftIsComplex)
        {
            var lines = FormatScalarExpressionBlock(comparisonExpression.FirstExpression, indentLevel);
            if (!string.IsNullOrWhiteSpace(prefixOperator))
            {
                ApplyPrefixToFirstLine(lines, indentLevel, prefixOperator);
            }

            if (!rightIsComplex)
            {
                lines[^1] += " " + operatorText + " " + GenerateLeafScript(comparisonExpression.SecondExpression);
                return lines;
            }

            lines[^1] += " " + operatorText;
            lines.AddRange(FormatScalarExpressionBlock(comparisonExpression.SecondExpression, indentLevel + 1));
            return lines;
        }

        var header = GenerateLeafScript(comparisonExpression.FirstExpression) + " " + operatorText;
        if (!string.IsNullOrWhiteSpace(prefixOperator))
        {
            header = prefixOperator + " " + header;
        }

        return
        [
            Indent(indentLevel) + header,
            ..FormatScalarExpressionBlock(comparisonExpression.SecondExpression, indentLevel + 1)
        ];
    }

    /// <summary>
    /// LIKE / NOT LIKE 条件を整形する。
    /// 左右どちらかが複雑なときは演算子を境目にして複数行へ開く。
    /// </summary>
    private List<string> FormatLikePredicate(
        LikePredicate likePredicate,
        int indentLevel,
        string? prefixOperator)
    {
        var leftIsComplex = RequiresMultilineScalarExpression(likePredicate.FirstExpression);
        var rightIsComplex = RequiresMultilineScalarExpression(likePredicate.SecondExpression);
        var escapeIsComplex = likePredicate.EscapeExpression is not null
            && RequiresMultilineScalarExpression(likePredicate.EscapeExpression);
        var operatorText = likePredicate.NotDefined ? "NOT LIKE" : "LIKE";
        var escapeSuffix = BuildLikeEscapeSuffix(likePredicate, escapeIsComplex);

        if (leftIsComplex)
        {
            var lines = FormatScalarExpressionBlock(likePredicate.FirstExpression, indentLevel);
            if (!string.IsNullOrWhiteSpace(prefixOperator))
            {
                ApplyPrefixToFirstLine(lines, indentLevel, prefixOperator);
            }

            if (!rightIsComplex && !escapeIsComplex)
            {
                lines[^1] += " " + operatorText + " " + GenerateLeafScript(likePredicate.SecondExpression) + escapeSuffix;
                return lines;
            }

            lines[^1] += " " + operatorText;
            lines.AddRange(FormatScalarExpressionBlock(likePredicate.SecondExpression, indentLevel + 1));
            AppendLikeEscapeExpression(lines, likePredicate.EscapeExpression, indentLevel + 1, escapeIsComplex);
            return lines;
        }

        var header = GenerateLeafScript(likePredicate.FirstExpression) + " " + operatorText;
        if (!string.IsNullOrWhiteSpace(prefixOperator))
        {
            header = prefixOperator + " " + header;
        }

        if (!rightIsComplex && !escapeIsComplex)
        {
            return [Indent(indentLevel) + header + " " + GenerateLeafScript(likePredicate.SecondExpression) + escapeSuffix];
        }

        var predicateLines = new List<string>
        {
            Indent(indentLevel) + header
        };

        predicateLines.AddRange(FormatScalarExpressionBlock(likePredicate.SecondExpression, indentLevel + 1));
        AppendLikeEscapeExpression(predicateLines, likePredicate.EscapeExpression, indentLevel + 1, escapeIsComplex);
        return predicateLines;
    }

    /// <summary>
    /// BETWEEN / NOT BETWEEN 条件を整形する。
    /// 境界値が複雑な場合は BETWEEN と AND を独立した切れ目として見せる。
    /// </summary>
    private List<string> FormatBooleanTernaryExpression(
        BooleanTernaryExpression ternaryExpression,
        int indentLevel,
        string? prefixOperator)
    {
        var leftIsComplex = RequiresMultilineScalarExpression(ternaryExpression.FirstExpression);
        var secondIsComplex = RequiresMultilineScalarExpression(ternaryExpression.SecondExpression);
        var thirdIsComplex = RequiresMultilineScalarExpression(ternaryExpression.ThirdExpression);
        var operatorText = ternaryExpression.TernaryExpressionType switch
        {
            BooleanTernaryExpressionType.Between => "BETWEEN",
            BooleanTernaryExpressionType.NotBetween => "NOT BETWEEN",
            _ => ternaryExpression.TernaryExpressionType.ToString().ToUpperInvariant()
        };

        if (leftIsComplex)
        {
            var lines = FormatScalarExpressionBlock(ternaryExpression.FirstExpression, indentLevel);
            if (!string.IsNullOrWhiteSpace(prefixOperator))
            {
                ApplyPrefixToFirstLine(lines, indentLevel, prefixOperator);
            }

            if (!secondIsComplex && !thirdIsComplex)
            {
                lines[^1] += " "
                    + operatorText
                    + " "
                    + GenerateLeafScript(ternaryExpression.SecondExpression)
                    + " AND "
                    + GenerateLeafScript(ternaryExpression.ThirdExpression);
                return lines;
            }

            if (!secondIsComplex)
            {
                lines[^1] += " " + operatorText + " " + GenerateLeafScript(ternaryExpression.SecondExpression);
            }
            else
            {
                lines[^1] += " " + operatorText;
                lines.AddRange(FormatScalarExpressionBlock(ternaryExpression.SecondExpression, indentLevel + 1));
            }

            AppendBetweenThirdExpression(lines, ternaryExpression.ThirdExpression, indentLevel + 1, thirdIsComplex);
            return lines;
        }

        var header = GenerateLeafScript(ternaryExpression.FirstExpression) + " " + operatorText;
        if (!string.IsNullOrWhiteSpace(prefixOperator))
        {
            header = prefixOperator + " " + header;
        }

        if (!secondIsComplex && !thirdIsComplex)
        {
            return
            [
                Indent(indentLevel)
                + header
                + " "
                + GenerateLeafScript(ternaryExpression.SecondExpression)
                + " AND "
                + GenerateLeafScript(ternaryExpression.ThirdExpression)
            ];
        }

        var predicateLines = new List<string>();
        if (!secondIsComplex)
        {
            predicateLines.Add(Indent(indentLevel) + header + " " + GenerateLeafScript(ternaryExpression.SecondExpression));
        }
        else
        {
            predicateLines.Add(Indent(indentLevel) + header);
            predicateLines.AddRange(FormatScalarExpressionBlock(ternaryExpression.SecondExpression, indentLevel + 1));
        }

        AppendBetweenThirdExpression(predicateLines, ternaryExpression.ThirdExpression, indentLevel + 1, thirdIsComplex);
        return predicateLines;
    }

    /// <summary>
    /// IS NULL / IS NOT NULL 条件を整形する。
    /// 判定対象が複雑なときも末尾の NULL 判定を見失わない形へ揃える。
    /// </summary>
    private List<string> FormatBooleanIsNullExpression(
        BooleanIsNullExpression isNullExpression,
        int indentLevel,
        string? prefixOperator)
    {
        var lines = FormatScalarExpressionBlock(isNullExpression.Expression, indentLevel);
        if (!string.IsNullOrWhiteSpace(prefixOperator))
        {
            ApplyPrefixToFirstLine(lines, indentLevel, prefixOperator);
        }

        lines[^1] += isNullExpression.IsNot ? " IS NOT NULL" : " IS NULL";
        return lines;
    }

    /// <summary>
    /// 括弧付き論理グループを整形する。
    /// AND / OR のまとまりが括弧内で見えるよう、開始行と終了行を独立させる。
    /// </summary>
    private List<string> FormatParenthesizedBooleanExpression(
        BooleanExpression expression,
        int indentLevel,
        string? prefixOperator,
        bool negated)
    {
        var header = negated ? "NOT (" : "(";
        if (!string.IsNullOrWhiteSpace(prefixOperator))
        {
            header = prefixOperator + " " + header;
        }

        var lines = new List<string>
        {
            Indent(indentLevel) + header
        };

        if (TryFlattenLogicalChain(expression, out var operatorKeyword, out var operands))
        {
            lines.AddRange(FormatBooleanOperandBlock(operands[0], indentLevel + 1, prefixOperator: null));

            for (var index = 1; index < operands.Count; index++)
            {
                lines.AddRange(FormatBooleanOperandBlock(
                    operands[index],
                    indentLevel + 1,
                    prefixOperator: operatorKeyword));
            }
        }
        else
        {
            lines.AddRange(FormatBooleanOperandBlock(expression, indentLevel + 1, prefixOperator: null));
        }

        lines.Add(Indent(indentLevel) + ")");
        return lines;
    }

    /// <summary>
    /// EXISTS / NOT EXISTS を展開整形する。
    /// サブクエリ本体は 1 段深くする。
    /// </summary>
    private List<string> FormatExistsPredicate(
        ScalarSubquery scalarSubquery,
        int indentLevel,
        bool negated,
        string? prefixOperator)
    {
        var header = negated ? "NOT EXISTS (" : "EXISTS (";
        if (!string.IsNullOrWhiteSpace(prefixOperator))
        {
            header = prefixOperator + " " + header;
        }

        var lines = new List<string>
        {
            Indent(indentLevel) + header
        };

        lines.AddRange(FormatQueryExpression(scalarSubquery.QueryExpression, indentLevel + 1));
        lines.Add(Indent(indentLevel) + ")");
        return lines;
    }

    /// <summary>
    /// IN / NOT IN サブクエリを展開整形する。
    /// 左辺は先頭行に置き、内部 SELECT は括弧内へ展開する。
    /// </summary>
    private List<string> FormatInPredicate(InPredicate inPredicate, int indentLevel, string? prefixOperator)
    {
        var leftExpression = GenerateLeafScript(inPredicate.Expression);
        var operatorText = inPredicate.NotDefined ? "NOT IN (" : "IN (";
        var header = leftExpression + " " + operatorText;
        if (!string.IsNullOrWhiteSpace(prefixOperator))
        {
            header = prefixOperator + " " + header;
        }

        var lines = new List<string>
        {
            Indent(indentLevel) + header
        };

        lines.AddRange(FormatQueryExpression(inPredicate.Subquery!.QueryExpression, indentLevel + 1));
        lines.Add(Indent(indentLevel) + ")");
        return lines;
    }

    /// <summary>
    /// GROUP BY 項目を 1 項目 1 行で整形する。
    /// </summary>
    private void AppendGroupingClause(List<string> lines, GroupByClause? groupByClause, int indentLevel)
    {
        if (groupByClause?.GroupingSpecifications is not { Count: > 0 } groupingSpecifications)
        {
            return;
        }

        lines.Add(Indent(indentLevel) + "GROUP BY");
        for (var index = 0; index < groupingSpecifications.Count; index++)
        {
            var line = Indent(indentLevel + 1) + GenerateLeafScript(groupingSpecifications[index]);
            if (index < groupingSpecifications.Count - 1)
            {
                line += ",";
            }

            lines.Add(line);
        }
    }

    /// <summary>
    /// ORDER BY 項目を 1 項目 1 行で整形する。
    /// </summary>
    private void AppendOrderByClause(List<string> lines, OrderByClause? orderByClause, int indentLevel)
    {
        if (orderByClause?.OrderByElements is not { Count: > 0 } orderByElements)
        {
            return;
        }

        lines.Add(Indent(indentLevel) + "ORDER BY");
        for (var index = 0; index < orderByElements.Count; index++)
        {
            var line = Indent(indentLevel + 1) + GenerateLeafScript(orderByElements[index]);
            if (index < orderByElements.Count - 1)
            {
                line += ",";
            }

            lines.Add(line);
        }
    }

    /// <summary>
    /// AND / OR の連鎖を同一演算子単位で平坦化する。
    /// </summary>
    private static bool TryFlattenLogicalChain(
        BooleanExpression expression,
        out string operatorKeyword,
        out List<BooleanExpression> operands)
    {
        if (expression is BooleanBinaryExpression andExpression
            && andExpression.BinaryExpressionType == BooleanBinaryExpressionType.And)
        {
            operatorKeyword = "AND";
            operands = [];
            CollectLogicalOperands(andExpression, BooleanBinaryExpressionType.And, operands);
            return true;
        }

        if (expression is BooleanBinaryExpression orExpression
            && orExpression.BinaryExpressionType == BooleanBinaryExpressionType.Or)
        {
            operatorKeyword = "OR";
            operands = [];
            CollectLogicalOperands(orExpression, BooleanBinaryExpressionType.Or, operands);
            return true;
        }

        operatorKeyword = string.Empty;
        operands = [];
        return false;
    }

    /// <summary>
    /// 同じ演算子で連なる論理式を再帰的に収集する。
    /// </summary>
    private static void CollectLogicalOperands(
        BooleanExpression expression,
        BooleanBinaryExpressionType targetType,
        List<BooleanExpression> operands)
    {
        if (expression is BooleanBinaryExpression binaryExpression
            && binaryExpression.BinaryExpressionType == targetType)
        {
            CollectLogicalOperands(binaryExpression.FirstExpression, targetType, operands);
            CollectLogicalOperands(binaryExpression.SecondExpression, targetType, operands);
            return;
        }

        operands.Add(expression);
    }

    /// <summary>
    /// 末尾の行へ必要なときだけカンマを付ける。
    /// </summary>
    private static void AppendCommaToLastLine(List<string> lines, bool shouldAppend)
    {
        if (!shouldAppend || lines.Count == 0)
        {
            return;
        }

        lines[^1] += ",";
    }

    /// <summary>
    /// 複数行ブロックの先頭行へ AND / OR などの接続詞を付ける。
    /// </summary>
    private static void ApplyPrefixToFirstLine(List<string> lines, int indentLevel, string prefixOperator)
    {
        if (lines.Count == 0)
        {
            return;
        }

        lines[0] = Indent(indentLevel) + prefixOperator + " " + lines[0].TrimStart();
    }

    /// <summary>
    /// LIKE 条件の ESCAPE 句が単純な場合の接尾辞を組み立てる。
    /// </summary>
    private string BuildLikeEscapeSuffix(LikePredicate likePredicate, bool escapeIsComplex)
    {
        if (likePredicate.EscapeExpression is null || escapeIsComplex)
        {
            return string.Empty;
        }

        return " ESCAPE " + GenerateLeafScript(likePredicate.EscapeExpression);
    }

    /// <summary>
    /// LIKE 条件の ESCAPE 句を必要に応じて別行へ追加する。
    /// </summary>
    private void AppendLikeEscapeExpression(
        List<string> lines,
        ScalarExpression? escapeExpression,
        int indentLevel,
        bool escapeIsComplex)
    {
        if (escapeExpression is null || lines.Count == 0)
        {
            return;
        }

        if (!escapeIsComplex)
        {
            lines.Add(Indent(indentLevel) + "ESCAPE " + GenerateLeafScript(escapeExpression));
            return;
        }

        var escapeLines = FormatScalarExpressionBlock(escapeExpression, indentLevel);
        ApplyPrefixToFirstLine(escapeLines, indentLevel, "ESCAPE");
        lines.AddRange(escapeLines);
    }

    /// <summary>
    /// BETWEEN の後半境界値を追加する。
    /// 複雑な式なら AND を先頭へ付けた複数行ブロックとして出力する。
    /// </summary>
    private void AppendBetweenThirdExpression(
        List<string> lines,
        ScalarExpression thirdExpression,
        int indentLevel,
        bool thirdIsComplex)
    {
        if (!thirdIsComplex)
        {
            lines.Add(Indent(indentLevel) + "AND " + GenerateLeafScript(thirdExpression));
            return;
        }

        var thirdLines = FormatScalarExpressionBlock(thirdExpression, indentLevel);
        ApplyPrefixToFirstLine(thirdLines, indentLevel, "AND");
        lines.AddRange(thirdLines);
    }

    /// <summary>
    /// TOP 句を SELECT ヘッダー向けへ組み立てる。
    /// </summary>
    private string BuildTopExpression(TopRowFilter topRowFilter)
    {
        var text = "TOP " + GenerateLeafScript(topRowFilter.Expression);
        if (topRowFilter.Percent)
        {
            text += " PERCENT";
        }

        if (topRowFilter.WithTies)
        {
            text += " WITH TIES";
        }

        return text;
    }

    /// <summary>
    /// 集合演算子の表示文字列を返す。
    /// </summary>
    private static string FormatSetOperation(BinaryQueryExpression binaryQueryExpression)
    {
        return binaryQueryExpression.BinaryQueryExpressionType switch
        {
            BinaryQueryExpressionType.Union when binaryQueryExpression.All => "UNION ALL",
            BinaryQueryExpressionType.Union => "UNION",
            BinaryQueryExpressionType.Except => "EXCEPT",
            BinaryQueryExpressionType.Intersect => "INTERSECT",
            _ => binaryQueryExpression.BinaryQueryExpressionType.ToString().ToUpperInvariant()
        };
    }

    /// <summary>
    /// 比較演算子を文字列へ変換する。
    /// </summary>
    private static string FormatComparisonOperator(BooleanComparisonType comparisonType)
    {
        return comparisonType switch
        {
            BooleanComparisonType.Equals => "=",
            BooleanComparisonType.GreaterThan => ">",
            BooleanComparisonType.LessThan => "<",
            BooleanComparisonType.GreaterThanOrEqualTo => ">=",
            BooleanComparisonType.LessThanOrEqualTo => "<=",
            BooleanComparisonType.NotEqualToBrackets => "<>",
            BooleanComparisonType.NotEqualToExclamation => "!=",
            BooleanComparisonType.NotLessThan => "!<",
            BooleanComparisonType.NotGreaterThan => "!>",
            BooleanComparisonType.IsDistinctFrom => "IS DISTINCT FROM",
            BooleanComparisonType.IsNotDistinctFrom => "IS NOT DISTINCT FROM",
            _ => comparisonType.ToString().ToUpperInvariant()
        };
    }

    /// <summary>
    /// QualifiedJoinType を表示用の JOIN 文字列へ変換する。
    /// </summary>
    private static string FormatQualifiedJoinType(QualifiedJoinType qualifiedJoinType)
    {
        return qualifiedJoinType switch
        {
            QualifiedJoinType.Inner => "INNER JOIN",
            QualifiedJoinType.LeftOuter => "LEFT JOIN",
            QualifiedJoinType.RightOuter => "RIGHT JOIN",
            QualifiedJoinType.FullOuter => "FULL JOIN",
            _ => qualifiedJoinType.ToString().ToUpperInvariant()
        };
    }

    /// <summary>
    /// UnqualifiedJoinType を表示用の JOIN 文字列へ変換する。
    /// </summary>
    private static string FormatUnqualifiedJoinType(UnqualifiedJoinType unqualifiedJoinType)
    {
        return unqualifiedJoinType switch
        {
            UnqualifiedJoinType.CrossJoin => "CROSS JOIN",
            UnqualifiedJoinType.CrossApply => "CROSS APPLY",
            UnqualifiedJoinType.OuterApply => "OUTER APPLY",
            _ => unqualifiedJoinType.ToString().ToUpperInvariant()
        };
    }

    /// <summary>
    /// 断片を葉ノード向けの 1 行テキストへ整形する。
    /// 余分な改行と連続空白を畳み、句構造の外側だけを簡潔に保つ。
    /// </summary>
    private string GenerateLeafScript(TSqlFragment fragment)
    {
        _leafGenerator.GenerateScript(fragment, out var script);
        return Regex.Replace(script, @"\s+", " ").Trim();
    }

    /// <summary>
    /// スカラー式を複数行整形する価値があるかを判定する。
    /// </summary>
    private static bool RequiresMultilineScalarExpression(ScalarExpression expression)
    {
        return expression switch
        {
            CaseExpression => true,
            ScalarSubquery => true,
            ParenthesisExpression parenthesisExpression
                => RequiresMultilineScalarExpression(parenthesisExpression.Expression),
            FunctionCall functionCall
                => ShouldFormatFunctionCallAsMultiline(functionCall),
            CoalesceExpression coalesceExpression
                => coalesceExpression.Expressions.Any(RequiresMultilineScalarExpression),
            NullIfExpression nullIfExpression
                => RequiresMultilineScalarExpression(nullIfExpression.FirstExpression)
                    || RequiresMultilineScalarExpression(nullIfExpression.SecondExpression),
            _ => false
        };
    }

    /// <summary>
    /// 関数呼び出しを複数行へ開くべきか判定する。
    /// ウィンドウ関数などは既定整形へ任せ、複雑引数だけを対象にする。
    /// </summary>
    private static bool ShouldFormatFunctionCallAsMultiline(FunctionCall functionCall)
    {
        if (functionCall.OverClause is not null || functionCall.WithinGroupClause is not null)
        {
            return false;
        }

        return functionCall.Parameters.Any(RequiresMultilineScalarExpression);
    }

    /// <summary>
    /// 関数名を呼び出しターゲット込みで組み立てる。
    /// </summary>
    private string BuildFunctionCallName(FunctionCall functionCall)
    {
        var builder = new StringBuilder();
        if (functionCall.CallTarget is not null)
        {
            builder.Append(GenerateLeafScript(functionCall.CallTarget));
            builder.Append('.');
        }

        builder.Append(functionCall.FunctionName.Value);
        return builder.ToString();
    }

    /// <summary>
    /// フォールバック用の生成結果を返す。
    /// </summary>
    private string GenerateFallbackScript(TSqlFragment fragment)
    {
        _fallbackGenerator.GenerateScript(fragment, out var script);
        return script;
    }

    /// <summary>
    /// 文全体の生成結果を行末込みで正規化する。
    /// </summary>
    private static string NormalizeGeneratedStatementText(string text)
    {
        return text.ReplaceLineEndings(Environment.NewLine).Trim();
    }

    /// <summary>
    /// 末尾セミコロンを保証する。
    /// </summary>
    private static string EnsureSemicolon(string text)
    {
        var trimmed = text.Trim();
        return trimmed.EndsWith(';')
            ? trimmed
            : trimmed + ";";
    }

    /// <summary>
    /// インデント文字列を返す。
    /// </summary>
    private static string Indent(int indentLevel)
    {
        return new string(' ', indentLevel * IndentationSize);
    }

    /// <summary>
    /// フォールバック整形用の ScriptDom オプション。
    /// DML や未対応文種別でも最低限読みやすい改行を維持する。
    /// </summary>
    private static SqlScriptGeneratorOptions CreateFallbackOptions()
    {
        return new SqlScriptGeneratorOptions
        {
            KeywordCasing = KeywordCasing.Uppercase,
            IncludeSemicolons = true,
            IndentationSize = IndentationSize,
            NewLineBeforeFromClause = true,
            NewLineBeforeWhereClause = true,
            NewLineBeforeGroupByClause = true,
            NewLineBeforeHavingClause = true,
            NewLineBeforeOrderByClause = true,
            NewLineBeforeJoinClause = true,
            NewLineBeforeOffsetClause = true,
            NewLineBeforeOutputClause = true,
            AlignClauseBodies = false,
            MultilineSelectElementsList = true,
            MultilineWherePredicatesList = true,
            IndentSetClause = true,
            AlignSetClauseItem = false,
            MultilineSetClauseItems = true,
            MultilineInsertTargetsList = true,
            MultilineInsertSourcesList = true,
            NewLineBeforeOpenParenthesisInMultilineList = true,
            NewLineBeforeCloseParenthesisInMultilineList = true
        };
    }

    /// <summary>
    /// 葉ノード整形用の ScriptDom オプション。
    /// 1 行前提のためセミコロンや過剰な改行は付けない。
    /// </summary>
    private static SqlScriptGeneratorOptions CreateLeafOptions()
    {
        return new SqlScriptGeneratorOptions
        {
            KeywordCasing = KeywordCasing.Uppercase,
            IncludeSemicolons = false,
            IndentationSize = IndentationSize,
            AlignClauseBodies = false,
            MultilineSelectElementsList = false,
            MultilineWherePredicatesList = false,
            MultilineSetClauseItems = false,
            MultilineInsertTargetsList = false,
            MultilineInsertSourcesList = false
        };
    }
}
