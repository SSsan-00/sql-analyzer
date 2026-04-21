using System.ComponentModel;
using System.Drawing.Drawing2D;
using TSqlAnalyzer.Application.Presentation;

namespace TSqlAnalyzer.WinForms;

/// <summary>
/// TreeView 用 ImageList をコードで生成するファクトリ。
/// 外部画像ファイルを持たず、標準 TreeView の ImageList 機能だけで分類アイコンを表示する。
/// </summary>
internal static class TreeViewImageListFactory
{
    private const int IconSize = 14;

    /// <summary>
    /// 表示分類ごとの小さな丸アイコンを持つ ImageList を作る。
    /// components に登録しておくことで、Form 破棄時に ImageList もまとめて解放される。
    /// </summary>
    public static ImageList Create(IContainer? components)
    {
        var imageList = components is null ? new ImageList() : new ImageList(components);
        imageList.ColorDepth = ColorDepth.Depth32Bit;
        imageList.ImageSize = new Size(IconSize, IconSize);
        imageList.TransparentColor = Color.Transparent;

        foreach (var kind in TreeNodeVisualCatalog.GetKinds())
        {
            var imageKey = TreeNodeVisualCatalog.GetImageKey(kind);
            var style = TreeNodeVisualCatalog.GetStyle(kind);
            imageList.Images.Add(imageKey, CreateIcon(style.AccentColor));
        }

        return imageList;
    }

    /// <summary>
    /// 分類色を使った丸アイコンを生成する。
    /// 小さいサイズでも潰れにくいよう、薄い外枠と内側のハイライトを入れる。
    /// </summary>
    private static Bitmap CreateIcon(Color accentColor)
    {
        var bitmap = new Bitmap(IconSize, IconSize);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var fillBrush = new SolidBrush(accentColor);
        using var highlightBrush = new SolidBrush(Color.FromArgb(80, Color.White));
        using var borderPen = new Pen(ControlPaint.Dark(accentColor), 1F);

        var outerBounds = new Rectangle(2, 2, IconSize - 5, IconSize - 5);
        var highlightBounds = new Rectangle(4, 4, 4, 4);

        graphics.FillEllipse(fillBrush, outerBounds);
        graphics.FillEllipse(highlightBrush, highlightBounds);
        graphics.DrawEllipse(borderPen, outerBounds);

        return bitmap;
    }
}
