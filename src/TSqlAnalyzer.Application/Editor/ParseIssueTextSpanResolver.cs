using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Application.Editor;

/// <summary>
/// パーサーが返した行・列情報を、エディター上で強調表示できる文字範囲へ変換する。
/// 行末や空白位置が返ってきた場合も、できるだけ利用者が原因箇所を見つけやすい範囲へ寄せる。
/// </summary>
public sealed class ParseIssueTextSpanResolver
{
    /// <summary>
    /// 構文エラー位置を SQL 文字列上の範囲へ変換する。
    /// </summary>
    public bool TryResolve(string sql, ParseIssue parseIssue, out TextSpan span)
    {
        span = default!;

        if (string.IsNullOrEmpty(sql) || parseIssue.Line <= 0 || parseIssue.Column <= 0)
        {
            return false;
        }

        if (!TryGetIndexFromLineAndColumn(sql, parseIssue.Line, parseIssue.Column, out var issueIndex))
        {
            return false;
        }

        var anchorIndex = FindAnchorIndex(sql, issueIndex);
        if (anchorIndex < 0 || anchorIndex >= sql.Length)
        {
            return false;
        }

        var tokenStart = anchorIndex;
        var tokenEnd = anchorIndex + 1;

        if (IsTokenCharacter(sql[anchorIndex]))
        {
            while (tokenStart > 0 && IsTokenCharacter(sql[tokenStart - 1]))
            {
                tokenStart--;
            }

            while (tokenEnd < sql.Length && IsTokenCharacter(sql[tokenEnd]))
            {
                tokenEnd++;
            }
        }

        span = new TextSpan(tokenStart, Math.Max(1, tokenEnd - tokenStart));
        return true;
    }

    /// <summary>
    /// 1 始まりの行・列を文字列インデックスへ変換する。
    /// 改行は CRLF / LF / CR のいずれにも対応する。
    /// </summary>
    private static bool TryGetIndexFromLineAndColumn(string sql, int line, int column, out int index)
    {
        index = 0;
        var currentLine = 1;
        var currentColumn = 1;

        while (index < sql.Length)
        {
            if (currentLine == line && currentColumn == column)
            {
                return true;
            }

            if (sql[index] == '\r')
            {
                index++;
                if (index < sql.Length && sql[index] == '\n')
                {
                    index++;
                }

                currentLine++;
                currentColumn = 1;
                continue;
            }

            if (sql[index] == '\n')
            {
                index++;
                currentLine++;
                currentColumn = 1;
                continue;
            }

            index++;
            currentColumn++;
        }

        if (currentLine == line && currentColumn == column)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 行・列が空白や行末を指している場合は、前後の文字を見て表示用の基準位置を決める。
    /// </summary>
    private static int FindAnchorIndex(string sql, int issueIndex)
    {
        if (sql.Length == 0)
        {
            return -1;
        }

        var clampedIndex = Math.Clamp(issueIndex, 0, sql.Length - 1);
        if (!char.IsWhiteSpace(sql[clampedIndex]))
        {
            return clampedIndex;
        }

        for (var index = clampedIndex - 1; index >= 0; index--)
        {
            if (sql[index] is '\r' or '\n')
            {
                break;
            }

            if (!char.IsWhiteSpace(sql[index]))
            {
                return index;
            }
        }

        for (var index = clampedIndex + 1; index < sql.Length; index++)
        {
            if (sql[index] is '\r' or '\n')
            {
                break;
            }

            if (!char.IsWhiteSpace(sql[index]))
            {
                return index;
            }
        }

        return clampedIndex;
    }

    /// <summary>
    /// エラー位置としてまとめて強調したい記号を判定する。
    /// 識別子だけでなく、修飾子付き列名や角括弧識別子も 1 まとまりとして扱う。
    /// </summary>
    private static bool IsTokenCharacter(char value)
    {
        return char.IsLetterOrDigit(value)
            || value is '_' or '#' or '@' or '$' or '.' or '[' or ']';
    }
}
