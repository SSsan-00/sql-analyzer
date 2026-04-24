namespace TSqlAnalyzer.WinForms.UI;

/// <summary>
/// 短い名前入力に使う簡易ダイアログ。
/// ワークスペース名やクエリ名の追加・変更で使う。
/// </summary>
public static class TextPromptDialog
{
    /// <summary>
    /// 文字列入力ダイアログを表示する。
    /// Cancel 時は null を返す。
    /// </summary>
    public static string? Show(IWin32Window owner, string title, string labelText, string initialValue)
    {
        using var form = new Form
        {
            AutoScaleMode = AutoScaleMode.Font,
            ClientSize = new Size(420, 132),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            Text = title
        };

        var label = new Label
        {
            AutoSize = true,
            Location = new Point(12, 16),
            Text = labelText
        };

        var textBox = new TextBox
        {
            Location = new Point(12, 44),
            Size = new Size(392, 23),
            Text = initialValue ?? string.Empty
        };

        var okButton = new Button
        {
            DialogResult = DialogResult.OK,
            Location = new Point(248, 88),
            Size = new Size(75, 28),
            Text = "OK",
            UseVisualStyleBackColor = true
        };

        var cancelButton = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(329, 88),
            Size = new Size(75, 28),
            Text = "Cancel",
            UseVisualStyleBackColor = true
        };

        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;
        form.Controls.Add(label);
        form.Controls.Add(textBox);
        form.Controls.Add(okButton);
        form.Controls.Add(cancelButton);

        var result = form.ShowDialog(owner);
        if (result != DialogResult.OK)
        {
            return null;
        }

        return textBox.Text.Trim();
    }
}
