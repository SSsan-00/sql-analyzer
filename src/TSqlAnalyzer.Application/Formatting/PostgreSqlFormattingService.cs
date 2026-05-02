using Google.Protobuf.Collections;
using PgSqlParser;
using TSqlAnalyzer.Domain.Analysis;

using PgNode = PgSqlParser.Node;

namespace TSqlAnalyzer.Application.Formatting;

/// <summary>
/// pgsqlparser-dotnet を使って PostgreSQL SQL を整形する。
/// T-SQL 整形で扱えない PostgreSQL 固有構文を、自動フォールバックで補う。
/// </summary>
internal sealed class PostgreSqlFormattingService
{
    private const int IndentationSize = 4;

    public SqlFormatResult Format(string sql)
    {
        var parseResult = Parser.Parse(sql);
        if (parseResult.Error is not null)
        {
            return new SqlFormatResult(
                false,
                sql,
                [CreateParseIssue(sql, parseResult.Error)]);
        }

        var parseValue = parseResult.Value;
        if (parseValue is null || parseValue.Stmts.Count == 0)
        {
            return new SqlFormatResult(true, string.Empty, []);
        }

        var formattedStatements = parseValue.Stmts
            .Select(statement => FormatStatement(statement.Stmt))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        return new SqlFormatResult(
            true,
            string.Join(Environment.NewLine + Environment.NewLine, formattedStatements),
            []);
    }

    private string FormatStatement(PgNode statement)
    {
        return statement.NodeCase switch
        {
            PgNode.NodeOneofCase.SelectStmt => EnsureSemicolon(string.Join(Environment.NewLine, FormatSelectStatement(statement.SelectStmt, 0))),
            PgNode.NodeOneofCase.InsertStmt => EnsureSemicolon(string.Join(Environment.NewLine, FormatInsertStatement(statement.InsertStmt, 0))),
            PgNode.NodeOneofCase.UpdateStmt => EnsureSemicolon(string.Join(Environment.NewLine, FormatUpdateStatement(statement.UpdateStmt, 0))),
            PgNode.NodeOneofCase.DeleteStmt => EnsureSemicolon(string.Join(Environment.NewLine, FormatDeleteStatement(statement.DeleteStmt, 0))),
            _ => EnsureSemicolon(ApplyFallbackLineBreaks(DeparseStatement(statement)))
        };
    }

    private List<string> FormatSelectStatement(SelectStmt selectStatement, int indentLevel)
    {
        var lines = new List<string>();
        AppendWithClause(lines, selectStatement.WithClause, indentLevel);
        lines.AddRange(FormatSelectBody(selectStatement, indentLevel));
        return lines;
    }

    private List<string> FormatSelectBody(SelectStmt selectStatement, int indentLevel)
    {
        if (selectStatement.Op != SetOperation.SetopNone
            && selectStatement.Larg is not null
            && selectStatement.Rarg is not null)
        {
            return FormatSetOperation(selectStatement, indentLevel);
        }

        if (selectStatement.ValuesLists.Count > 0)
        {
            return FormatValuesList(selectStatement.ValuesLists, indentLevel);
        }

        var lines = new List<string>
        {
            Indent(indentLevel) + BuildSelectHeader(selectStatement)
        };

        AppendSelectTargets(lines, selectStatement.TargetList, indentLevel + 1);
        AppendFromClause(lines, selectStatement.FromClause, indentLevel);
        AppendBooleanClause(lines, "WHERE", selectStatement.WhereClause, indentLevel);
        AppendExpressionListClause(lines, "GROUP BY", selectStatement.GroupClause, indentLevel);
        AppendBooleanClause(lines, "HAVING", selectStatement.HavingClause, indentLevel);
        AppendOrderByClause(lines, selectStatement.SortClause, indentLevel);
        AppendLimitClause(lines, selectStatement, indentLevel);
        return lines;
    }

    private List<string> FormatInsertStatement(InsertStmt insertStatement, int indentLevel)
    {
        var lines = new List<string>
        {
            Indent(indentLevel) + "INSERT INTO " + BuildRangeVarName(insertStatement.Relation)
        };

        if (insertStatement.Cols.Count > 0)
        {
            lines[^1] += " (";
            for (var index = 0; index < insertStatement.Cols.Count; index++)
            {
                var suffix = index < insertStatement.Cols.Count - 1 ? "," : string.Empty;
                lines.Add(Indent(indentLevel + 1) + BuildInsertColumnName(insertStatement.Cols[index]) + suffix);
            }

            lines.Add(Indent(indentLevel) + ")");
        }

        if (insertStatement.SelectStmt?.SelectStmt is { ValuesLists.Count: > 0 } valuesSelect)
        {
            lines.AddRange(FormatValuesList(valuesSelect.ValuesLists, indentLevel));
        }
        else if (insertStatement.SelectStmt?.SelectStmt is not null)
        {
            lines.AddRange(FormatSelectStatement(insertStatement.SelectStmt.SelectStmt, indentLevel));
        }

        AppendReturningClause(lines, insertStatement.ReturningList, indentLevel);

        if (insertStatement.OnConflictClause is not null)
        {
            AppendFallbackSuffix(lines, DeparseStatement(new PgNode { InsertStmt = insertStatement }), "ON CONFLICT", indentLevel);
        }

        return lines;
    }

    private List<string> FormatUpdateStatement(UpdateStmt updateStatement, int indentLevel)
    {
        var lines = new List<string>
        {
            Indent(indentLevel) + "UPDATE " + BuildRangeVarName(updateStatement.Relation)
        };

        if (updateStatement.TargetList.Count > 0)
        {
            lines.Add(Indent(indentLevel) + "SET");
            for (var index = 0; index < updateStatement.TargetList.Count; index++)
            {
                var suffix = index < updateStatement.TargetList.Count - 1 ? "," : string.Empty;
                lines.Add(Indent(indentLevel + 1) + FormatUpdateTarget(updateStatement.TargetList[index]) + suffix);
            }
        }

        AppendFromClause(lines, updateStatement.FromClause, indentLevel);
        AppendBooleanClause(lines, "WHERE", updateStatement.WhereClause, indentLevel);
        AppendReturningClause(lines, updateStatement.ReturningList, indentLevel);
        return lines;
    }

    private List<string> FormatDeleteStatement(DeleteStmt deleteStatement, int indentLevel)
    {
        var lines = new List<string>
        {
            Indent(indentLevel) + "DELETE FROM " + BuildRangeVarName(deleteStatement.Relation)
        };

        if (deleteStatement.UsingClause.Count > 0)
        {
            lines.AddRange(FormatFromItems(deleteStatement.UsingClause, indentLevel, "USING"));
        }

        AppendBooleanClause(lines, "WHERE", deleteStatement.WhereClause, indentLevel);
        AppendReturningClause(lines, deleteStatement.ReturningList, indentLevel);
        return lines;
    }

    private void AppendWithClause(List<string> lines, WithClause? withClause, int indentLevel)
    {
        if (withClause?.Ctes is not { Count: > 0 } commonTableExpressions)
        {
            return;
        }

        for (var index = 0; index < commonTableExpressions.Count; index++)
        {
            if (commonTableExpressions[index].NodeCase != PgNode.NodeOneofCase.CommonTableExpr)
            {
                continue;
            }

            var cte = commonTableExpressions[index].CommonTableExpr;
            var header = (index == 0 ? "WITH " : ", ") + cte.Ctename;
            if (cte.Aliascolnames.Count > 0)
            {
                header += " (" + string.Join(", ", cte.Aliascolnames.Select(ReadIdentifier)) + ")";
            }

            header += " AS (";
            lines.Add(Indent(indentLevel) + header);
            if (cte.Ctequery?.SelectStmt is not null)
            {
                lines.AddRange(FormatSelectStatement(cte.Ctequery.SelectStmt, indentLevel + 1));
            }
            else if (cte.Ctequery is not null)
            {
                lines.Add(Indent(indentLevel + 1) + DeparseStatement(cte.Ctequery));
            }

            lines.Add(Indent(indentLevel) + ")");
        }
    }

    private List<string> FormatSetOperation(SelectStmt selectStatement, int indentLevel)
    {
        var lines = new List<string>();
        lines.AddRange(FormatSelectBody(selectStatement.Larg!, indentLevel));
        lines.Add(Indent(indentLevel) + BuildSetOperationText(selectStatement));
        lines.AddRange(FormatSelectBody(selectStatement.Rarg!, indentLevel));
        AppendOrderByClause(lines, selectStatement.SortClause, indentLevel);
        AppendLimitClause(lines, selectStatement, indentLevel);
        return lines;
    }

    private List<string> FormatValuesList(RepeatedField<PgNode> valuesLists, int indentLevel)
    {
        var lines = new List<string>
        {
            Indent(indentLevel) + "VALUES"
        };

        for (var index = 0; index < valuesLists.Count; index++)
        {
            var values = valuesLists[index].NodeCase == PgNode.NodeOneofCase.List
                ? valuesLists[index].List.Items.Select(DeparseExpression)
                : [DeparseExpression(valuesLists[index])];
            var suffix = index < valuesLists.Count - 1 ? "," : string.Empty;
            lines.Add(Indent(indentLevel + 1) + "(" + string.Join(", ", values) + ")" + suffix);
        }

        return lines;
    }

    private static string BuildSelectHeader(SelectStmt selectStatement)
    {
        var parts = new List<string> { "SELECT" };
        if (selectStatement.DistinctClause.Count > 0)
        {
            parts.Add("DISTINCT");
        }

        return string.Join(" ", parts);
    }

    private void AppendSelectTargets(List<string> lines, RepeatedField<PgNode> targetList, int indentLevel)
    {
        if (targetList.Count == 0)
        {
            lines.Add(Indent(indentLevel) + "*");
            return;
        }

        for (var index = 0; index < targetList.Count; index++)
        {
            var targetLines = FormatSelectTarget(targetList[index], indentLevel);
            AppendCommaToLastLine(targetLines, index < targetList.Count - 1);
            lines.AddRange(targetLines);
        }
    }

    private List<string> FormatSelectTarget(PgNode target, int indentLevel)
    {
        if (target.NodeCase != PgNode.NodeOneofCase.ResTarget)
        {
            return [Indent(indentLevel) + DeparseExpression(target)];
        }

        var resTarget = target.ResTarget;
        var expressionLines = FormatExpressionBlock(resTarget.Val, indentLevel);
        if (!string.IsNullOrWhiteSpace(resTarget.Name))
        {
            expressionLines[^1] += " AS " + resTarget.Name;
        }

        return expressionLines;
    }

    private List<string> FormatExpressionBlock(PgNode? expression, int indentLevel)
    {
        if (expression is null)
        {
            return [Indent(indentLevel) + string.Empty];
        }

        return expression.NodeCase switch
        {
            PgNode.NodeOneofCase.CaseExpr => FormatCaseExpression(expression.CaseExpr, indentLevel),
            PgNode.NodeOneofCase.SubLink when expression.SubLink.Subselect?.SelectStmt is not null
                => FormatSubqueryExpression(expression.SubLink.Subselect.SelectStmt, indentLevel),
            _ => [Indent(indentLevel) + DeparseExpression(expression)]
        };
    }

    private List<string> FormatCaseExpression(CaseExpr caseExpression, int indentLevel)
    {
        var lines = new List<string>();
        if (caseExpression.Arg is null)
        {
            lines.Add(Indent(indentLevel) + "CASE");
        }
        else
        {
            lines.Add(Indent(indentLevel) + "CASE " + DeparseExpression(caseExpression.Arg));
        }

        foreach (var node in caseExpression.Args.Where(node => node.NodeCase == PgNode.NodeOneofCase.CaseWhen))
        {
            lines.Add(Indent(indentLevel + 1) + "WHEN " + DeparseExpression(node.CaseWhen.Expr));
            lines.Add(Indent(indentLevel + 2) + "THEN " + DeparseExpression(node.CaseWhen.Result));
        }

        if (caseExpression.Defresult is not null)
        {
            lines.Add(Indent(indentLevel + 1) + "ELSE");
            lines.Add(Indent(indentLevel + 2) + DeparseExpression(caseExpression.Defresult));
        }

        lines.Add(Indent(indentLevel) + "END");
        return lines;
    }

    private List<string> FormatSubqueryExpression(SelectStmt selectStatement, int indentLevel)
    {
        var lines = new List<string>
        {
            Indent(indentLevel) + "("
        };

        lines.AddRange(FormatSelectStatement(selectStatement, indentLevel + 1));
        lines.Add(Indent(indentLevel) + ")");
        return lines;
    }

    private void AppendFromClause(List<string> lines, RepeatedField<PgNode> fromClause, int indentLevel)
    {
        if (fromClause.Count == 0)
        {
            return;
        }

        lines.AddRange(FormatFromItems(fromClause, indentLevel, "FROM"));
    }

    private List<string> FormatFromItems(RepeatedField<PgNode> fromItems, int indentLevel, string firstLeader)
    {
        var lines = new List<string>();

        for (var index = 0; index < fromItems.Count; index++)
        {
            var leader = index == 0 ? firstLeader : ",";
            lines.AddRange(FormatFromItem(fromItems[index], indentLevel, leader));
        }

        return lines;
    }

    private List<string> FormatFromItem(PgNode item, int indentLevel, string leader)
    {
        return item.NodeCase switch
        {
            PgNode.NodeOneofCase.JoinExpr => FormatJoinExpression(item.JoinExpr, indentLevel, leader),
            PgNode.NodeOneofCase.RangeSubselect => FormatRangeSubselect(item.RangeSubselect, indentLevel, leader),
            _ => [Indent(indentLevel) + leader + " " + DeparseFromItem(item)]
        };
    }

    private List<string> FormatJoinExpression(JoinExpr joinExpression, int indentLevel, string leader)
    {
        var lines = joinExpression.Larg is not null
            ? FormatFromItem(joinExpression.Larg, indentLevel, leader)
            : [];

        if (joinExpression.Rarg is not null)
        {
            lines.AddRange(FormatFromItem(joinExpression.Rarg, indentLevel + 1, BuildJoinTypeText(joinExpression)));
        }

        if (joinExpression.Quals is not null)
        {
            lines.AddRange(FormatBooleanClauseLines("ON", joinExpression.Quals, indentLevel + 2));
        }
        else if (joinExpression.UsingClause.Count > 0)
        {
            lines.Add(Indent(indentLevel + 2) + "USING (" + string.Join(", ", joinExpression.UsingClause.Select(ReadIdentifier)) + ")");
        }

        return lines;
    }

    private List<string> FormatRangeSubselect(RangeSubselect rangeSubselect, int indentLevel, string leader)
    {
        var lines = new List<string>
        {
            Indent(indentLevel) + leader + " ("
        };

        if (rangeSubselect.Subquery?.SelectStmt is not null)
        {
            lines.AddRange(FormatSelectStatement(rangeSubselect.Subquery.SelectStmt, indentLevel + 1));
        }
        else if (rangeSubselect.Subquery is not null)
        {
            lines.Add(Indent(indentLevel + 1) + DeparseStatement(rangeSubselect.Subquery));
        }

        var closing = Indent(indentLevel) + ")";
        if (!string.IsNullOrWhiteSpace(rangeSubselect.Alias?.Aliasname))
        {
            closing += " " + rangeSubselect.Alias.Aliasname;
        }

        lines.Add(closing);
        return lines;
    }

    private void AppendBooleanClause(List<string> lines, string clauseKeyword, PgNode? expression, int indentLevel)
    {
        if (expression is null)
        {
            return;
        }

        lines.AddRange(FormatBooleanClauseLines(clauseKeyword, expression, indentLevel));
    }

    private List<string> FormatBooleanClauseLines(string clauseKeyword, PgNode expression, int indentLevel)
    {
        var lines = new List<string>
        {
            Indent(indentLevel) + clauseKeyword
        };

        if (TryFlattenLogicalChain(expression, out var operatorKeyword, out var operands))
        {
            lines.AddRange(FormatBooleanOperandBlock(operands[0], indentLevel + 1, null));
            for (var index = 1; index < operands.Count; index++)
            {
                lines.AddRange(FormatBooleanOperandBlock(operands[index], indentLevel + 1, operatorKeyword));
            }

            return lines;
        }

        lines.AddRange(FormatBooleanOperandBlock(expression, indentLevel + 1, null));
        return lines;
    }

    private List<string> FormatBooleanOperandBlock(PgNode expression, int indentLevel, string? prefixOperator)
    {
        if (expression.NodeCase == PgNode.NodeOneofCase.SubLink
            && expression.SubLink.Subselect?.SelectStmt is not null)
        {
            var header = expression.SubLink.SubLinkType == SubLinkType.ExistsSublink ? "EXISTS (" : DeparseExpression(expression);
            if (!string.IsNullOrWhiteSpace(prefixOperator))
            {
                header = prefixOperator + " " + header;
            }

            if (expression.SubLink.SubLinkType != SubLinkType.ExistsSublink)
            {
                return [Indent(indentLevel) + header];
            }

            var lines = new List<string>
            {
                Indent(indentLevel) + header
            };
            lines.AddRange(FormatSelectStatement(expression.SubLink.Subselect.SelectStmt, indentLevel + 1));
            lines.Add(Indent(indentLevel) + ")");
            return lines;
        }

        if (expression.NodeCase == PgNode.NodeOneofCase.AExpr
            && expression.AExpr.Kind == A_Expr_Kind.AexprIn
            && expression.AExpr.Rexpr?.SubLink?.Subselect?.SelectStmt is not null)
        {
            var header = DeparseExpression(expression.AExpr.Lexpr) + " IN (";
            if (!string.IsNullOrWhiteSpace(prefixOperator))
            {
                header = prefixOperator + " " + header;
            }

            var lines = new List<string>
            {
                Indent(indentLevel) + header
            };
            lines.AddRange(FormatSelectStatement(expression.AExpr.Rexpr.SubLink.Subselect.SelectStmt, indentLevel + 1));
            lines.Add(Indent(indentLevel) + ")");
            return lines;
        }

        if (expression.NodeCase == PgNode.NodeOneofCase.BoolExpr
            && expression.BoolExpr.Boolop == BoolExprType.NotExpr
            && expression.BoolExpr.Args.Count == 1)
        {
            var lines = FormatBooleanOperandBlock(expression.BoolExpr.Args[0], indentLevel, "NOT");
            if (!string.IsNullOrWhiteSpace(prefixOperator))
            {
                ApplyPrefixToFirstLine(lines, indentLevel, prefixOperator);
            }

            return lines;
        }

        var text = DeparseExpression(expression);
        if (!string.IsNullOrWhiteSpace(prefixOperator))
        {
            text = prefixOperator + " " + text;
        }

        return [Indent(indentLevel) + text];
    }

    private static bool TryFlattenLogicalChain(PgNode expression, out string operatorKeyword, out List<PgNode> operands)
    {
        if (expression.NodeCase == PgNode.NodeOneofCase.BoolExpr
            && expression.BoolExpr.Boolop is BoolExprType.AndExpr or BoolExprType.OrExpr)
        {
            operatorKeyword = expression.BoolExpr.Boolop == BoolExprType.AndExpr ? "AND" : "OR";
            operands = [];
            CollectLogicalOperands(expression, expression.BoolExpr.Boolop, operands);
            return true;
        }

        operatorKeyword = string.Empty;
        operands = [];
        return false;
    }

    private static void CollectLogicalOperands(PgNode expression, BoolExprType targetType, List<PgNode> operands)
    {
        if (expression.NodeCase == PgNode.NodeOneofCase.BoolExpr
            && expression.BoolExpr.Boolop == targetType)
        {
            foreach (var arg in expression.BoolExpr.Args)
            {
                CollectLogicalOperands(arg, targetType, operands);
            }

            return;
        }

        operands.Add(expression);
    }

    private void AppendExpressionListClause(List<string> lines, string clauseKeyword, RepeatedField<PgNode> items, int indentLevel)
    {
        if (items.Count == 0)
        {
            return;
        }

        lines.Add(Indent(indentLevel) + clauseKeyword);
        for (var index = 0; index < items.Count; index++)
        {
            var suffix = index < items.Count - 1 ? "," : string.Empty;
            lines.Add(Indent(indentLevel + 1) + DeparseExpression(items[index]) + suffix);
        }
    }

    private void AppendOrderByClause(List<string> lines, RepeatedField<PgNode> sortClause, int indentLevel)
    {
        if (sortClause.Count == 0)
        {
            return;
        }

        lines.Add(Indent(indentLevel) + "ORDER BY");
        for (var index = 0; index < sortClause.Count; index++)
        {
            var suffix = index < sortClause.Count - 1 ? "," : string.Empty;
            lines.Add(Indent(indentLevel + 1) + FormatSortBy(sortClause[index]) + suffix);
        }
    }

    private void AppendLimitClause(List<string> lines, SelectStmt selectStatement, int indentLevel)
    {
        if (selectStatement.LimitCount is not null)
        {
            lines.Add(Indent(indentLevel) + "LIMIT " + DeparseExpression(selectStatement.LimitCount));
        }

        if (selectStatement.LimitOffset is not null)
        {
            lines.Add(Indent(indentLevel) + "OFFSET " + DeparseExpression(selectStatement.LimitOffset));
        }
    }

    private void AppendReturningClause(List<string> lines, RepeatedField<PgNode> returningList, int indentLevel)
    {
        if (returningList.Count == 0)
        {
            return;
        }

        lines.Add(Indent(indentLevel) + "RETURNING");
        for (var index = 0; index < returningList.Count; index++)
        {
            var targetLines = FormatSelectTarget(returningList[index], indentLevel + 1);
            AppendCommaToLastLine(targetLines, index < returningList.Count - 1);
            lines.AddRange(targetLines);
        }
    }

    private void AppendFallbackSuffix(List<string> lines, string fullStatement, string marker, int indentLevel)
    {
        var markerIndex = fullStatement.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return;
        }

        lines.Add(Indent(indentLevel) + fullStatement[markerIndex..].Trim().TrimEnd(';'));
    }

    private string FormatSortBy(PgNode sortNode)
    {
        if (sortNode.NodeCase != PgNode.NodeOneofCase.SortBy)
        {
            return DeparseExpression(sortNode);
        }

        var expression = DeparseExpression(sortNode.SortBy.Node);
        return sortNode.SortBy.SortbyDir switch
        {
            SortByDir.SortbyAsc => expression + " ASC",
            SortByDir.SortbyDesc => expression + " DESC",
            SortByDir.SortbyUsing => expression + " USING " + string.Join(" ", sortNode.SortBy.UseOp.Select(ReadIdentifier)),
            _ => expression
        };
    }

    private string FormatUpdateTarget(PgNode targetNode)
    {
        if (targetNode.NodeCase != PgNode.NodeOneofCase.ResTarget)
        {
            return DeparseExpression(targetNode);
        }

        var target = targetNode.ResTarget;
        var targetName = string.IsNullOrWhiteSpace(target.Name)
            ? DeparseExpression(targetNode)
            : target.Name;
        return targetName + " = " + DeparseExpression(target.Val);
    }

    private static string BuildInsertColumnName(PgNode node)
    {
        return node.NodeCase == PgNode.NodeOneofCase.ResTarget
            ? node.ResTarget.Name
            : ReadIdentifier(node);
    }

    private static string BuildSetOperationText(SelectStmt selectStatement)
    {
        return selectStatement.Op switch
        {
            SetOperation.SetopUnion when selectStatement.All => "UNION ALL",
            SetOperation.SetopUnion => "UNION",
            SetOperation.SetopIntersect => "INTERSECT",
            SetOperation.SetopExcept => "EXCEPT",
            _ => selectStatement.Op.ToString().ToUpperInvariant()
        };
    }

    private static string BuildJoinTypeText(JoinExpr joinExpression)
    {
        if (joinExpression.IsNatural)
        {
            return "NATURAL JOIN";
        }

        return joinExpression.Jointype switch
        {
            PgSqlParser.JoinType.JoinLeft => "LEFT JOIN",
            PgSqlParser.JoinType.JoinRight => "RIGHT JOIN",
            PgSqlParser.JoinType.JoinFull => "FULL JOIN",
            PgSqlParser.JoinType.JoinInner => "INNER JOIN",
            _ => "JOIN"
        };
    }

    private static string BuildRangeVarName(RangeVar rangeVar)
    {
        var name = string.Join(
            ".",
            new[] { rangeVar.Catalogname, rangeVar.Schemaname, rangeVar.Relname }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        if (!string.IsNullOrWhiteSpace(rangeVar.Alias?.Aliasname))
        {
            name += " " + rangeVar.Alias.Aliasname;
        }

        return name;
    }

    private string DeparseFromItem(PgNode fromItem)
    {
        var text = DeparseStatement(new PgNode
        {
            SelectStmt = new SelectStmt
            {
                TargetList =
                {
                    new PgNode
                    {
                        ResTarget = new ResTarget
                        {
                            Val = new PgNode
                            {
                                ColumnRef = new ColumnRef
                                {
                                    Fields = { new PgNode { AStar = new A_Star() } }
                                }
                            }
                        }
                    }
                },
                FromClause = { fromItem }
            }
        });

        const string prefix = "SELECT * FROM ";
        return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? text[prefix.Length..].TrimEnd(';')
            : text.TrimEnd(';');
    }

    private string DeparseExpression(PgNode? expression)
    {
        if (expression is null)
        {
            return string.Empty;
        }

        var text = DeparseStatement(new PgNode
        {
            SelectStmt = new SelectStmt
            {
                TargetList =
                {
                    new PgNode
                    {
                        ResTarget = new ResTarget
                        {
                            Val = expression
                        }
                    }
                }
            }
        });

        const string prefix = "SELECT ";
        return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? text[prefix.Length..].TrimEnd(';')
            : text.TrimEnd(';');
    }

    private static string DeparseStatement(PgNode statement)
    {
        var result = Parser.Deparse(new ParseResult
        {
            Version = Parser.PgVersionNum,
            Stmts =
            {
                new RawStmt
                {
                    Stmt = statement
                }
            }
        });

        return result.Error is null
            ? result.Value ?? string.Empty
            : statement.ToString();
    }

    private static string ApplyFallbackLineBreaks(string sql)
    {
        var trimmed = sql.Trim().TrimEnd(';');
        var withBreaks = RegexReplaceKeyword(trimmed, "SELECT");
        withBreaks = RegexReplaceKeyword(withBreaks, "FROM");
        withBreaks = RegexReplaceKeyword(withBreaks, "WHERE");
        withBreaks = RegexReplaceKeyword(withBreaks, "GROUP BY");
        withBreaks = RegexReplaceKeyword(withBreaks, "HAVING");
        withBreaks = RegexReplaceKeyword(withBreaks, "ORDER BY");
        withBreaks = RegexReplaceKeyword(withBreaks, "LIMIT");
        withBreaks = RegexReplaceKeyword(withBreaks, "RETURNING");
        return withBreaks.Trim();
    }

    private static string RegexReplaceKeyword(string text, string keyword)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            text,
            $@"\s+{System.Text.RegularExpressions.Regex.Escape(keyword)}\b",
            Environment.NewLine + keyword,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static void AppendCommaToLastLine(List<string> lines, bool shouldAppend)
    {
        if (shouldAppend && lines.Count > 0)
        {
            lines[^1] += ",";
        }
    }

    private static void ApplyPrefixToFirstLine(List<string> lines, int indentLevel, string prefixOperator)
    {
        if (lines.Count > 0)
        {
            lines[0] = Indent(indentLevel) + prefixOperator + " " + lines[0].TrimStart();
        }
    }

    private static string ReadIdentifier(PgNode? node)
    {
        return node?.NodeCase switch
        {
            PgNode.NodeOneofCase.String => node.String.Sval,
            PgNode.NodeOneofCase.AStar => "*",
            PgNode.NodeOneofCase.Integer => node.Integer.Ival.ToString(),
            PgNode.NodeOneofCase.Float => node.Float.Fval,
            _ => string.Empty
        };
    }

    private static ParseIssue CreateParseIssue(string sql, Error error)
    {
        var cursorIndex = Math.Max(0, error.CursorPos - 1);
        var line = 1;
        var column = 1;

        for (var index = 0; index < Math.Min(cursorIndex, sql.Length); index++)
        {
            if (sql[index] == '\n')
            {
                line++;
                column = 1;
                continue;
            }

            column++;
        }

        return new ParseIssue(line, column, error.Message ?? "PostgreSQL SQL を構文解析できませんでした。");
    }

    private static string EnsureSemicolon(string text)
    {
        var trimmed = text.Trim();
        return trimmed.EndsWith(";", StringComparison.Ordinal)
            ? trimmed
            : trimmed + ";";
    }

    private static string Indent(int indentLevel)
    {
        return new string(' ', indentLevel * IndentationSize);
    }
}
