using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Application.Export;

/// <summary>
/// 解析結果から列確認用のプレーンテキストを作る。
/// WinForms の保存処理から切り離し、テストしやすい純粋な変換処理として実装する。
/// </summary>
public sealed class ColumnTextExportBuilder
{
    /// <summary>
    /// 出力ブロックを区切る固定文字列。
    /// 利用者が Excel などへ貼り付けた後に、別ブロックの境界を見つけやすくする。
    /// </summary>
    public static readonly string BlockSeparator = new('\\', 11);

    private const string NewLine = "\r\n";
    private const string OmittedInsertColumnsText = "(挿入列省略)";

    /// <summary>
    /// 解析結果からエクスポート用テキストを生成する。
    /// 対象がない場合は空文字を返し、UI 側で保存可否を判断できるようにする。
    /// </summary>
    public string Build(QueryAnalysisResult analysis)
    {
        var blocks = new List<IReadOnlyList<string>>();

        if (analysis.DataModification is not null)
        {
            AddDataModificationBlocks(analysis.DataModification, blocks);
        }
        else if (analysis.CreateStatement is not null)
        {
            AddCreateStatementBlocks(analysis.CreateStatement, blocks);
        }
        else
        {
            AddQueryBlocks(analysis.Query, blocks);
        }

        foreach (var commonTableExpression in analysis.CommonTableExpressions)
        {
            AddQueryBlocks(commonTableExpression.Query, blocks);
        }

        return BuildText(blocks);
    }

    /// <summary>
    /// DML 文種別ごとに、変更対象列や入力元値のブロックを追加する。
    /// </summary>
    private static void AddDataModificationBlocks(
        DataModificationAnalysis dataModification,
        ICollection<IReadOnlyList<string>> blocks)
    {
        switch (dataModification)
        {
            case InsertStatementAnalysis insertStatement:
                AddInsertBlocks(insertStatement, blocks);
                break;
            case UpdateStatementAnalysis updateStatement:
                AddUpdateBlocks(updateStatement, blocks);
                break;
        }
    }

    /// <summary>
    /// INSERT 文を「挿入先列ブロック」と「入力元ブロック」に分けて追加する。
    /// </summary>
    private static void AddInsertBlocks(
        InsertStatementAnalysis insertStatement,
        ICollection<IReadOnlyList<string>> blocks)
    {
        AddBlock(
            blocks,
            insertStatement.TargetColumns.Count > 0
                ? insertStatement.TargetColumns
                : [OmittedInsertColumnsText]);

        if (insertStatement.InsertSource is null)
        {
            return;
        }

        switch (insertStatement.InsertSource.SourceKind)
        {
            case InsertSourceKind.Values:
                AddValuesBlocks(insertStatement.InsertSource, blocks);
                break;
            case InsertSourceKind.Query:
                AddQueryBlocks(insertStatement.InsertSource.Query, blocks);
                break;
            case InsertSourceKind.Execute:
                AddBlock(blocks, [insertStatement.InsertSource.ExecuteText ?? insertStatement.InsertSource.DisplayText]);
                break;
            default:
                AddBlock(blocks, insertStatement.InsertSource.Items.Count > 0
                    ? insertStatement.InsertSource.Items
                    : [insertStatement.InsertSource.DisplayText]);
                break;
        }
    }

    /// <summary>
    /// VALUES の各行を、それぞれ独立した値ブロックとして追加する。
    /// </summary>
    private static void AddValuesBlocks(
        InsertSourceAnalysis insertSource,
        ICollection<IReadOnlyList<string>> blocks)
    {
        if (insertSource.MappingGroups.Count == 0)
        {
            AddBlock(blocks, insertSource.Items);
            return;
        }

        foreach (var mappingGroup in insertSource.MappingGroups)
        {
            AddBlock(blocks, mappingGroup.Mappings.Select(mapping => mapping.ValueText));
        }
    }

    /// <summary>
    /// UPDATE 文を「更新列ブロック」と「SET 値式ブロック」に分けて追加する。
    /// </summary>
    private static void AddUpdateBlocks(
        UpdateStatementAnalysis updateStatement,
        ICollection<IReadOnlyList<string>> blocks)
    {
        AddBlock(blocks, updateStatement.SetClauses.Select(setClause => setClause.TargetText));
        AddBlock(blocks, updateStatement.SetClauses.Select(setClause => setClause.ValueText));
    }

    /// <summary>
    /// CREATE 文のうち、内部 SELECT や列定義をエクスポートできるものを追加する。
    /// </summary>
    private static void AddCreateStatementBlocks(
        CreateStatementAnalysis createStatement,
        ICollection<IReadOnlyList<string>> blocks)
    {
        switch (createStatement)
        {
            case CreateViewAnalysis createView:
                AddQueryBlocks(createView.Query, blocks);
                break;
            case CreateTableAnalysis createTable:
                AddBlock(blocks, createTable.Columns.Select(column => column.Name));
                AddQueryBlocks(createTable.Query, blocks);
                break;
        }
    }

    /// <summary>
    /// SELECT 系のクエリ式をブロックへ変換する。
    /// 集合演算は左右を別 SELECT ブロックとして扱う。
    /// </summary>
    private static void AddQueryBlocks(
        QueryExpressionAnalysis? query,
        ICollection<IReadOnlyList<string>> blocks)
    {
        switch (query)
        {
            case SelectQueryAnalysis selectQuery:
                AddSelectBlock(selectQuery, blocks);
                AddNestedSelectBlocks(selectQuery, blocks);
                break;
            case SetOperationQueryAnalysis setOperationQuery:
                AddQueryBlocks(setOperationQuery.LeftQuery, blocks);
                AddQueryBlocks(setOperationQuery.RightQuery, blocks);
                break;
        }
    }

    /// <summary>
    /// SELECT の取得項目だけを対象に、参照列や別名、式を 1 ブロックへまとめる。
    /// </summary>
    private static void AddSelectBlock(
        SelectQueryAnalysis selectQuery,
        ICollection<IReadOnlyList<string>> blocks)
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var selectItem in selectQuery.SelectItems)
        {
            var itemLines = BuildSelectItemLines(selectItem);
            foreach (var line in itemLines)
            {
                if (seen.Add(line))
                {
                    lines.Add(line);
                }
            }
        }

        AddBlock(blocks, lines);
    }

    /// <summary>
    /// SELECT 項目 1 件から出力行を作る。
    /// 参照列がない場合は、別名、式の順に利用する。
    /// </summary>
    private static IEnumerable<string> BuildSelectItemLines(SelectItemAnalysis selectItem)
    {
        if (selectItem.ColumnReferences.Count > 0)
        {
            return selectItem.ColumnReferences.Select(FormatColumnReference);
        }

        if (!string.IsNullOrWhiteSpace(selectItem.Alias))
        {
            return [selectItem.Alias];
        }

        if (!string.IsNullOrWhiteSpace(selectItem.ExpressionText))
        {
            return [selectItem.ExpressionText];
        }

        return [selectItem.DisplayText];
    }

    /// <summary>
    /// FROM や JOIN 先など、別 SELECT ブロックとして扱う内部クエリを追加する。
    /// WHERE や JOIN ON の条件だけに現れる参照列は出力対象外にする。
    /// </summary>
    private static void AddNestedSelectBlocks(
        SelectQueryAnalysis selectQuery,
        ICollection<IReadOnlyList<string>> blocks)
    {
        AddQueryBlocks(selectQuery.MainSource?.NestedQuery, blocks);

        foreach (var join in selectQuery.Joins)
        {
            AddQueryBlocks(join.TargetSource.NestedQuery, blocks);
        }

        foreach (var subquery in selectQuery.Subqueries.Where(subquery => subquery.Location == "SELECT項目"))
        {
            AddQueryBlocks(subquery.Query, blocks);
        }
    }

    /// <summary>
    /// 列参照を「修飾子.列名」または「列名」の形式へ整える。
    /// </summary>
    private static string FormatColumnReference(ColumnReferenceAnalysis columnReference)
    {
        return string.IsNullOrWhiteSpace(columnReference.Qualifier)
            ? columnReference.ColumnName
            : $"{columnReference.Qualifier}.{columnReference.ColumnName}";
    }

    /// <summary>
    /// 空行を除外して 1 ブロックを追加する。
    /// </summary>
    private static void AddBlock(
        ICollection<IReadOnlyList<string>> blocks,
        IEnumerable<string> lines)
    {
        var normalizedLines = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToArray();

        if (normalizedLines.Length > 0)
        {
            blocks.Add(normalizedLines);
        }
    }

    /// <summary>
    /// ブロック一覧を CRLF 区切りのプレーンテキストへ変換する。
    /// </summary>
    private static string BuildText(IReadOnlyList<IReadOnlyList<string>> blocks)
    {
        return string.Join(
            $"{NewLine}{BlockSeparator}{NewLine}",
            blocks.Select(block => string.Join(NewLine, block)));
    }
}
