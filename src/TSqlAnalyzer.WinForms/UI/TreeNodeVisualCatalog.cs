using TSqlAnalyzer.Application.Presentation;

namespace TSqlAnalyzer.WinForms;

/// <summary>
/// 標準 TreeView を拡張描画するときに使う見た目情報。
/// 解析モデルへ UI 色を混ぜないため、WinForms 側だけで管理する。
/// </summary>
internal sealed record TreeNodeVisualStyle(
    Color ForeColor,
    FontStyle FontStyle,
    Color AccentColor,
    Color BackgroundColor);

/// <summary>
/// 表示ノード分類から TreeView の色、太字、アイコンキーを決める。
/// MainForm から分離しておくことで、TreeView の見た目調整をこのクラスへ集約する。
/// </summary>
internal static class TreeNodeVisualCatalog
{
    private static readonly TreeNodeVisualStyle DefaultStyle = new(
        Color.FromArgb(35, 35, 35),
        FontStyle.Regular,
        Color.FromArgb(130, 130, 130),
        Color.Empty);

    /// <summary>
    /// すべての表示分類を返す。
    /// ImageList 生成時に、分類ごとの小さなアイコンを漏れなく登録するために使う。
    /// </summary>
    public static IEnumerable<DisplayTreeNodeKind> GetKinds()
    {
        return Enum.GetValues<DisplayTreeNodeKind>();
    }

    /// <summary>
    /// TreeNode に設定する ImageKey を返す。
    /// 分類名をそのままキーにして、ノード作成側と ImageList 作成側の対応を単純に保つ。
    /// </summary>
    public static string GetImageKey(DisplayTreeNodeKind kind)
    {
        return kind.ToString();
    }

    /// <summary>
    /// 分類に対応する描画スタイルを返す。
    /// ForeColor は文字色、FontStyle は強調度、AccentColor はアイコン色、BackgroundColor は見出し行の背景色として使う。
    /// </summary>
    public static TreeNodeVisualStyle GetStyle(DisplayTreeNodeKind kind)
    {
        return kind switch
        {
            DisplayTreeNodeKind.Root => new TreeNodeVisualStyle(Color.FromArgb(18, 55, 105), FontStyle.Bold, Color.FromArgb(18, 55, 105), Color.FromArgb(232, 241, 252)),
            DisplayTreeNodeKind.Section => new TreeNodeVisualStyle(Color.FromArgb(35, 80, 130), FontStyle.Bold, Color.FromArgb(35, 80, 130), Color.FromArgb(239, 246, 252)),
            DisplayTreeNodeKind.Select => new TreeNodeVisualStyle(Color.FromArgb(0, 90, 150), FontStyle.Regular, Color.FromArgb(0, 130, 180), Color.Empty),
            DisplayTreeNodeKind.Source => new TreeNodeVisualStyle(Color.FromArgb(70, 70, 70), FontStyle.Regular, Color.FromArgb(115, 115, 115), Color.Empty),
            DisplayTreeNodeKind.Join => new TreeNodeVisualStyle(Color.FromArgb(0, 110, 140), FontStyle.Bold, Color.FromArgb(0, 150, 170), Color.FromArgb(232, 248, 249)),
            DisplayTreeNodeKind.Condition => new TreeNodeVisualStyle(Color.FromArgb(150, 80, 0), FontStyle.Regular, Color.FromArgb(220, 135, 20), Color.FromArgb(255, 247, 235)),
            DisplayTreeNodeKind.SetOperation => new TreeNodeVisualStyle(Color.FromArgb(80, 95, 125), FontStyle.Bold, Color.FromArgb(80, 105, 150), Color.FromArgb(240, 244, 250)),
            DisplayTreeNodeKind.CommonTableExpression => new TreeNodeVisualStyle(Color.FromArgb(70, 100, 120), FontStyle.Bold, Color.FromArgb(80, 130, 155), Color.FromArgb(238, 247, 250)),
            DisplayTreeNodeKind.Subquery => new TreeNodeVisualStyle(Color.FromArgb(85, 85, 85), FontStyle.Regular, Color.FromArgb(145, 145, 145), Color.Empty),
            DisplayTreeNodeKind.DataModification => new TreeNodeVisualStyle(Color.FromArgb(20, 120, 80), FontStyle.Bold, Color.FromArgb(35, 155, 95), Color.FromArgb(235, 250, 242)),
            DisplayTreeNodeKind.Create => new TreeNodeVisualStyle(Color.FromArgb(95, 115, 40), FontStyle.Bold, Color.FromArgb(135, 155, 55), Color.FromArgb(247, 250, 235)),
            DisplayTreeNodeKind.ColumnReference => new TreeNodeVisualStyle(Color.FromArgb(95, 95, 95), FontStyle.Regular, Color.FromArgb(160, 160, 160), Color.Empty),
            _ => DefaultStyle
        };
    }
}
