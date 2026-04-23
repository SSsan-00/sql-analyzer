using System.Text.RegularExpressions;

namespace TSqlAnalyzer.Application.Editor;

/// <summary>
/// DB 接続に依存しない入力補助候補を返す。
/// キーワードに加えて、現在の SQL から見つかる CTE 名、別名、参照済み列名を候補へ混ぜる。
/// </summary>
public sealed class SqlInputAssistService
{
    private static readonly IReadOnlyList<SqlInputAssistItem> KeywordItems =
    [
        Keyword("SELECT"),
        Keyword("FROM"),
        Keyword("WHERE"),
        Keyword("INNER JOIN"),
        Keyword("LEFT JOIN"),
        Keyword("RIGHT JOIN"),
        Keyword("FULL JOIN"),
        Keyword("CROSS JOIN"),
        Keyword("ON"),
        Keyword("GROUP BY"),
        Keyword("HAVING"),
        Keyword("ORDER BY"),
        Keyword("EXISTS"),
        Keyword("NOT EXISTS"),
        Keyword("IN"),
        Keyword("NOT IN"),
        Keyword("UNION"),
        Keyword("UNION ALL"),
        Keyword("EXCEPT"),
        Keyword("INTERSECT"),
        Keyword("CASE"),
        Keyword("WHEN"),
        Keyword("THEN"),
        Keyword("ELSE"),
        Keyword("END"),
        Keyword("TOP"),
        Keyword("DISTINCT"),
        Keyword("INSERT INTO"),
        Keyword("VALUES"),
        Keyword("UPDATE"),
        Keyword("SET"),
        Keyword("DELETE"),
        Keyword("CREATE VIEW"),
        Keyword("CREATE TABLE"),
        Keyword("LIKE"),
        Keyword("BETWEEN"),
        Keyword("IS NULL"),
        Keyword("IS NOT NULL"),
        Keyword("AND"),
        Keyword("OR"),
        Keyword("NOT")
    ];

    /// <summary>
    /// 指定位置で使えそうな候補を返す。
    /// 補完時に置き換える開始位置と長さも合わせて返し、UI 側がそのまま差し込めるようにする。
    /// </summary>
    public SqlInputAssistResult GetSuggestions(string sql, int caretIndex)
    {
        var normalizedSql = sql ?? string.Empty;
        var safeCaretIndex = Math.Clamp(caretIndex, 0, normalizedSql.Length);
        var tokenInfo = ExtractTokenInfo(normalizedSql, safeCaretIndex);

        var items = tokenInfo.Qualifier is null
            ? BuildGeneralSuggestions(normalizedSql, tokenInfo.Prefix)
            : BuildQualifiedSuggestions(normalizedSql, tokenInfo.Qualifier, tokenInfo.Prefix);

        return new SqlInputAssistResult(tokenInfo.ReplaceStart, tokenInfo.ReplaceLength, items);
    }

    /// <summary>
    /// 補完対象のトークンを取り出す。
    /// `u.Na` のような入力では `Na` だけを置き換え、`u.` は残す。
    /// </summary>
    private static SqlAssistTokenInfo ExtractTokenInfo(string sql, int caretIndex)
    {
        var tokenStart = caretIndex;
        while (tokenStart > 0 && IsEditorTokenCharacter(sql[tokenStart - 1]))
        {
            tokenStart--;
        }

        var tokenText = sql[tokenStart..caretIndex];
        var separatorIndex = tokenText.LastIndexOf('.');
        if (separatorIndex < 0)
        {
            return new SqlAssistTokenInfo(tokenText, null, tokenStart, tokenText.Length);
        }

        var qualifier = tokenText[..separatorIndex];
        var memberPrefix = tokenText[(separatorIndex + 1)..];
        return new SqlAssistTokenInfo(
            memberPrefix,
            qualifier,
            tokenStart + separatorIndex + 1,
            memberPrefix.Length);
    }

    /// <summary>
    /// 修飾子なし補完では、キーワードと現在の SQL から見つかったローカル名を返す。
    /// </summary>
    private static IReadOnlyList<SqlInputAssistItem> BuildGeneralSuggestions(string sql, string prefix)
    {
        var localSymbols = new List<SqlInputAssistItem>();
        localSymbols.AddRange(ExtractCommonTableExpressionItems(sql));
        localSymbols.AddRange(ExtractSourceAliasItems(sql));
        localSymbols.AddRange(ExtractSelectAliasItems(sql));

        var candidates = localSymbols
            .Concat(KeywordItems)
            .Where(item => IsMatch(item.DisplayText, prefix))
            .OrderBy(item => GetSortBucket(item.Kind))
            .ThenBy(item => GetPrefixScore(item.DisplayText, prefix))
            .ThenBy(item => item.DisplayText, StringComparer.OrdinalIgnoreCase);

        return Deduplicate(candidates);
    }

    /// <summary>
    /// `alias.` 形式では、その別名で既に使われている列名だけを候補へ出す。
    /// 既存 SQL からの再利用に絞ることで、DB 接続なしでもノイズを抑える。
    /// </summary>
    private static IReadOnlyList<SqlInputAssistItem> BuildQualifiedSuggestions(string sql, string qualifier, string prefix)
    {
        var candidates = ExtractQualifiedColumnItems(sql, qualifier)
            .Where(item => IsMatch(item.DisplayText, prefix))
            .OrderBy(item => GetPrefixScore(item.DisplayText, prefix))
            .ThenBy(item => item.DisplayText, StringComparer.OrdinalIgnoreCase);

        return Deduplicate(candidates);
    }

    /// <summary>
    /// WITH 句で定義されている CTE 名を抽出する。
    /// </summary>
    private static IEnumerable<SqlInputAssistItem> ExtractCommonTableExpressionItems(string sql)
    {
        var matches = Regex.Matches(
            sql,
            @"(?inx)
            (?:\bWITH\b|,)
            \s*
            (?<name>\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_#@$]*)
            \s*
            (?:\([^)]*\))?
            \s+AS\s*\(");

        foreach (Match match in matches)
        {
            var name = match.Groups["name"].Value;
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return new SqlInputAssistItem(name, name, "現在の SQL で定義された CTE", SqlInputAssistItemKind.CommonTableExpression);
            }
        }
    }

    /// <summary>
    /// FROM / JOIN などで使われているソース別名を抽出する。
    /// </summary>
    private static IEnumerable<SqlInputAssistItem> ExtractSourceAliasItems(string sql)
    {
        var matches = Regex.Matches(
            sql,
            @"(?inx)
            \b(?:FROM|JOIN)\b
            \s+
            (?:
                \([^)]*\)
                |
                (?:\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_#@$]*)
                (?:\s*\.\s*(?:\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_#@$]*)){0,3}
            )
            \s+
            \b(?:AS\s+)?
            (?<alias>[A-Za-z_][A-Za-z0-9_#@$]*)
            (?=\s|,|\)|$)");

        foreach (Match match in matches)
        {
            var alias = match.Groups["alias"].Value;
            if (!string.IsNullOrWhiteSpace(alias))
            {
                yield return new SqlInputAssistItem(alias, alias, "現在の SQL で使われている別名", SqlInputAssistItemKind.SourceAlias);
            }
        }
    }

    /// <summary>
    /// SELECT 項目の AS 別名を抽出する。
    /// ORDER BY などで再利用しやすい名前を候補へ含める。
    /// </summary>
    private static IEnumerable<SqlInputAssistItem> ExtractSelectAliasItems(string sql)
    {
        var matches = Regex.Matches(
            sql,
            @"(?inx)
            \bAS\s+
            (?<alias>\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_#@$]*)");

        foreach (Match match in matches)
        {
            var alias = match.Groups["alias"].Value;
            if (!string.IsNullOrWhiteSpace(alias))
            {
                yield return new SqlInputAssistItem(alias, alias, "現在の SQL で定義された SELECT 別名", SqlInputAssistItemKind.SelectAlias);
            }
        }
    }

    /// <summary>
    /// `alias.column` 形式の参照から、指定別名に属する列名候補を抽出する。
    /// </summary>
    private static IEnumerable<SqlInputAssistItem> ExtractQualifiedColumnItems(string sql, string qualifier)
    {
        var matches = Regex.Matches(
            sql,
            @"(?inx)
            (?<qualifier>\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_#@$]*)
            \s*\.\s*
            (?<column>\[[^\]]+\]|[A-Za-z_][A-Za-z0-9_#@$]*)");

        foreach (Match match in matches)
        {
            var currentQualifier = match.Groups["qualifier"].Value;
            if (!string.Equals(currentQualifier, qualifier, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var column = match.Groups["column"].Value;
            if (!string.IsNullOrWhiteSpace(column))
            {
                yield return new SqlInputAssistItem(column, column, $"{qualifier} で既に使われている列", SqlInputAssistItemKind.Column);
            }
        }
    }

    /// <summary>
    /// 同名候補を大文字小文字無視で 1 件へまとめる。
    /// </summary>
    private static IReadOnlyList<SqlInputAssistItem> Deduplicate(IEnumerable<SqlInputAssistItem> items)
    {
        var uniqueItems = new List<SqlInputAssistItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (seen.Add(item.DisplayText))
            {
                uniqueItems.Add(item);
            }
        }

        return uniqueItems;
    }

    /// <summary>
    /// 入力中の接頭辞に一致するかを判定する。
    /// 空接頭辞なら候補を全件返す。
    /// </summary>
    private static bool IsMatch(string value, string prefix)
    {
        return string.IsNullOrWhiteSpace(prefix)
            || value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 接頭辞一致を優先するための並び順スコアを返す。
    /// </summary>
    private static int GetPrefixScore(string value, string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return 0;
        }

        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    /// <summary>
    /// ローカル記号をキーワードより先に出すための並び順を返す。
    /// </summary>
    private static int GetSortBucket(SqlInputAssistItemKind kind)
    {
        return kind switch
        {
            SqlInputAssistItemKind.CommonTableExpression => 0,
            SqlInputAssistItemKind.SourceAlias => 1,
            SqlInputAssistItemKind.SelectAlias => 2,
            SqlInputAssistItemKind.Column => 3,
            _ => 4
        };
    }

    /// <summary>
    /// エディター上で 1 トークンとして扱う文字を判定する。
    /// </summary>
    private static bool IsEditorTokenCharacter(char value)
    {
        return char.IsLetterOrDigit(value)
            || value is '_' or '#' or '@' or '$' or '.';
    }

    /// <summary>
    /// キーワード候補を作る。
    /// 句キーワードは末尾に空白を付けて差し込めるようにする。
    /// </summary>
    private static SqlInputAssistItem Keyword(string text)
    {
        return new SqlInputAssistItem(text, text + " ", "SQL キーワード", SqlInputAssistItemKind.Keyword);
    }

    /// <summary>
    /// 補完対象トークンの情報。
    /// </summary>
    private sealed record SqlAssistTokenInfo(string Prefix, string? Qualifier, int ReplaceStart, int ReplaceLength);
}

/// <summary>
/// 補完候補 1 件分。
/// 表示文字列と実際に差し込む文字列を分け、将来のスニペット化にも備える。
/// </summary>
public sealed record SqlInputAssistItem(
    string DisplayText,
    string InsertText,
    string Description,
    SqlInputAssistItemKind Kind);

/// <summary>
/// 補完候補の分類。
/// UI 側でアイコンや表示順を変えやすくするために種類を持たせる。
/// </summary>
public enum SqlInputAssistItemKind
{
    Keyword,
    CommonTableExpression,
    SourceAlias,
    SelectAlias,
    Column
}

/// <summary>
/// 補完候補一覧と置き換え範囲。
/// UI 側はこの範囲だけを InsertText で置き換えればよい。
/// </summary>
public sealed record SqlInputAssistResult(
    int ReplaceStart,
    int ReplaceLength,
    IReadOnlyList<SqlInputAssistItem> Items);
