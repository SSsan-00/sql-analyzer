#nullable enable

namespace TSqlAnalyzer.WinForms;

partial class MainForm
{
    /// <summary>
    /// フォーム上のコンポーネント管理用コンテナー。
    /// </summary>
    private System.ComponentModel.IContainer? components = null;

    private TableLayoutPanel mainLayoutPanel = null!;
    private FlowLayoutPanel buttonPanel = null!;
    private Label inputLabel = null!;
    private Label resultLabel = null!;
    private SplitContainer mainSplitContainer = null!;
    private Button analyzeButton = null!;
    private Button clearButton = null!;
    private TextBox sqlTextBox = null!;
    private TreeView resultTreeView = null!;

    /// <summary>
    /// 利用中のリソースを破棄する。
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// 画面のレイアウトと各コントロールを初期化する。
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        mainLayoutPanel = new TableLayoutPanel();
        buttonPanel = new FlowLayoutPanel();
        analyzeButton = new Button();
        clearButton = new Button();
        mainSplitContainer = new SplitContainer();
        inputLabel = new Label();
        sqlTextBox = new TextBox();
        resultLabel = new Label();
        resultTreeView = new TreeView();
        mainLayoutPanel.SuspendLayout();
        buttonPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)mainSplitContainer).BeginInit();
        mainSplitContainer.Panel1.SuspendLayout();
        mainSplitContainer.Panel2.SuspendLayout();
        mainSplitContainer.SuspendLayout();
        SuspendLayout();
        // 
        // mainLayoutPanel
        // 画面全体を縦方向に「ボタン領域」と「本文領域」に分ける。
        // 
        mainLayoutPanel.ColumnCount = 1;
        mainLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        mainLayoutPanel.Controls.Add(buttonPanel, 0, 0);
        mainLayoutPanel.Controls.Add(mainSplitContainer, 0, 1);
        mainLayoutPanel.Dock = DockStyle.Fill;
        mainLayoutPanel.Location = new Point(0, 0);
        mainLayoutPanel.Name = "mainLayoutPanel";
        mainLayoutPanel.RowCount = 2;
        mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        mainLayoutPanel.Size = new Size(1200, 720);
        mainLayoutPanel.TabIndex = 0;
        // 
        // buttonPanel
        // 操作ボタンを左寄せで並べる。
        // 
        buttonPanel.AutoSize = true;
        buttonPanel.Controls.Add(analyzeButton);
        buttonPanel.Controls.Add(clearButton);
        buttonPanel.Dock = DockStyle.Fill;
        buttonPanel.Location = new Point(12, 12);
        buttonPanel.Margin = new Padding(12, 12, 12, 8);
        buttonPanel.Name = "buttonPanel";
        buttonPanel.Size = new Size(1176, 41);
        buttonPanel.TabIndex = 0;
        // 
        // analyzeButton
        // 入力 SQL を解析して TreeView へ反映する。
        // 
        analyzeButton.AutoSize = true;
        analyzeButton.Location = new Point(0, 0);
        analyzeButton.Margin = new Padding(0, 0, 8, 0);
        analyzeButton.Name = "analyzeButton";
        analyzeButton.Padding = new Padding(12, 6, 12, 6);
        analyzeButton.Size = new Size(82, 41);
        analyzeButton.TabIndex = 0;
        analyzeButton.Text = "解析";
        analyzeButton.UseVisualStyleBackColor = true;
        analyzeButton.Click += AnalyzeButton_Click;
        // 
        // clearButton
        // 入力と解析結果をまとめて消去する。
        // 
        clearButton.AutoSize = true;
        clearButton.Location = new Point(90, 0);
        clearButton.Margin = new Padding(0);
        clearButton.Name = "clearButton";
        clearButton.Padding = new Padding(12, 6, 12, 6);
        clearButton.Size = new Size(82, 41);
        clearButton.TabIndex = 1;
        clearButton.Text = "クリア";
        clearButton.UseVisualStyleBackColor = true;
        clearButton.Click += ClearButton_Click;
        // 
        // mainSplitContainer
        // 左に入力欄、右に解析結果を配置し、同時に見比べやすくする。
        // 
        mainSplitContainer.Dock = DockStyle.Fill;
        mainSplitContainer.Location = new Point(12, 61);
        mainSplitContainer.Margin = new Padding(12, 0, 12, 12);
        mainSplitContainer.Name = "mainSplitContainer";
        // 
        // mainSplitContainer.Panel1
        // 
        mainSplitContainer.Panel1.Controls.Add(sqlTextBox);
        mainSplitContainer.Panel1.Controls.Add(inputLabel);
        mainSplitContainer.Panel1.Padding = new Padding(0, 0, 8, 0);
        // 
        // mainSplitContainer.Panel2
        // 
        mainSplitContainer.Panel2.Controls.Add(resultTreeView);
        mainSplitContainer.Panel2.Controls.Add(resultLabel);
        mainSplitContainer.Panel2.Padding = new Padding(8, 0, 0, 0);
        mainSplitContainer.Size = new Size(1176, 647);
        mainSplitContainer.SplitterDistance = 560;
        mainSplitContainer.TabIndex = 1;
        // 
        // inputLabel
        // 入力欄の説明ラベル。
        // 
        inputLabel.AutoSize = true;
        inputLabel.Dock = DockStyle.Top;
        inputLabel.Location = new Point(0, 0);
        inputLabel.Margin = new Padding(0, 0, 0, 8);
        inputLabel.Name = "inputLabel";
        inputLabel.Padding = new Padding(0, 0, 0, 8);
        inputLabel.Size = new Size(87, 31);
        inputLabel.TabIndex = 0;
        inputLabel.Text = "T-SQL入力";
        // 
        // sqlTextBox
        // 複数行の SQL 入力欄。
        // 
        sqlTextBox.AcceptsReturn = true;
        sqlTextBox.AcceptsTab = true;
        sqlTextBox.Dock = DockStyle.Fill;
        sqlTextBox.Font = new Font("Consolas", 11F);
        sqlTextBox.Location = new Point(0, 31);
        sqlTextBox.Multiline = true;
        sqlTextBox.Name = "sqlTextBox";
        sqlTextBox.ScrollBars = ScrollBars.Both;
        sqlTextBox.Size = new Size(552, 616);
        sqlTextBox.TabIndex = 1;
        sqlTextBox.WordWrap = false;
        // 
        // resultLabel
        // 解析結果欄の説明ラベル。
        // 
        resultLabel.AutoSize = true;
        resultLabel.Dock = DockStyle.Top;
        resultLabel.Location = new Point(8, 0);
        resultLabel.Margin = new Padding(0, 0, 0, 8);
        resultLabel.Name = "resultLabel";
        resultLabel.Padding = new Padding(0, 0, 0, 8);
        resultLabel.Size = new Size(104, 31);
        resultLabel.TabIndex = 0;
        resultLabel.Text = "解析結果ツリー";
        // 
        // resultTreeView
        // 構造を追いやすいよう、階層表示を主役にする。
        // 
        resultTreeView.Dock = DockStyle.Fill;
        resultTreeView.HideSelection = false;
        resultTreeView.Location = new Point(8, 31);
        resultTreeView.Name = "resultTreeView";
        resultTreeView.Size = new Size(600, 616);
        resultTreeView.TabIndex = 1;
        // 
        // MainForm
        // 初期版らしくシンプルな構成にしつつ、将来の拡張に耐える余白を残す。
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1200, 720);
        Controls.Add(mainLayoutPanel);
        MinimumSize = new Size(960, 600);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "T-SQL解析GUIツール";
        mainLayoutPanel.ResumeLayout(false);
        mainLayoutPanel.PerformLayout();
        buttonPanel.ResumeLayout(false);
        buttonPanel.PerformLayout();
        mainSplitContainer.Panel1.ResumeLayout(false);
        mainSplitContainer.Panel1.PerformLayout();
        mainSplitContainer.Panel2.ResumeLayout(false);
        mainSplitContainer.Panel2.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)mainSplitContainer).EndInit();
        mainSplitContainer.ResumeLayout(false);
        ResumeLayout(false);
    }
}
