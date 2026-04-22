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
    private Label detailLabel = null!;
    private SplitContainer mainSplitContainer = null!;
    private FlowLayoutPanel findPanel = null!;
    private FlowLayoutPanel resultSearchPanel = null!;
    private Label findLabel = null!;
    private Label resultSearchLabel = null!;
    private TextBox findTextBox = null!;
    private TextBox resultSearchTextBox = null!;
    private Button findNextButton = null!;
    private Button findPreviousButton = null!;
    private Button closeFindButton = null!;
    private Button resultSearchNextButton = null!;
    private Button resultSearchPreviousButton = null!;
    private Button resultFilterButton = null!;
    private Button resultFilterClearButton = null!;
    private Button analyzeButton = null!;
    private Button clearButton = null!;
    private RichTextBox sqlTextBox = null!;
    private TreeView resultTreeView = null!;
    private Panel resultDetailPanel = null!;
    private RichTextBox detailTextBox = null!;

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
        sqlTextBox = new RichTextBox();
        findPanel = new FlowLayoutPanel();
        findLabel = new Label();
        findTextBox = new TextBox();
        findNextButton = new Button();
        findPreviousButton = new Button();
        closeFindButton = new Button();
        inputLabel = new Label();
        resultSearchPanel = new FlowLayoutPanel();
        resultSearchLabel = new Label();
        resultSearchTextBox = new TextBox();
        resultSearchNextButton = new Button();
        resultSearchPreviousButton = new Button();
        resultFilterButton = new Button();
        resultFilterClearButton = new Button();
        resultTreeView = new TreeView();
        resultDetailPanel = new Panel();
        detailTextBox = new RichTextBox();
        detailLabel = new Label();
        resultLabel = new Label();
        mainLayoutPanel.SuspendLayout();
        buttonPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)mainSplitContainer).BeginInit();
        mainSplitContainer.Panel1.SuspendLayout();
        mainSplitContainer.Panel2.SuspendLayout();
        mainSplitContainer.SuspendLayout();
        findPanel.SuspendLayout();
        resultSearchPanel.SuspendLayout();
        resultDetailPanel.SuspendLayout();
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
        mainLayoutPanel.Size = new Size(1280, 760);
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
        buttonPanel.Size = new Size(1256, 41);
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
        // 左に入力欄、右に解析結果と全文表示を配置する。
        //
        mainSplitContainer.Dock = DockStyle.Fill;
        mainSplitContainer.Location = new Point(12, 61);
        mainSplitContainer.Margin = new Padding(12, 0, 12, 12);
        mainSplitContainer.Name = "mainSplitContainer";
        //
        // mainSplitContainer.Panel1
        //
        mainSplitContainer.Panel1.Controls.Add(sqlTextBox);
        mainSplitContainer.Panel1.Controls.Add(findPanel);
        mainSplitContainer.Panel1.Controls.Add(inputLabel);
        mainSplitContainer.Panel1.Padding = new Padding(0, 0, 8, 0);
        //
        // mainSplitContainer.Panel2
        //
        mainSplitContainer.Panel2.Controls.Add(resultTreeView);
        mainSplitContainer.Panel2.Controls.Add(resultDetailPanel);
        mainSplitContainer.Panel2.Controls.Add(resultSearchPanel);
        mainSplitContainer.Panel2.Controls.Add(resultLabel);
        mainSplitContainer.Panel2.Padding = new Padding(8, 0, 0, 0);
        mainSplitContainer.Size = new Size(1256, 687);
        mainSplitContainer.SplitterDistance = 610;
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
        inputLabel.Size = new Size(104, 31);
        inputLabel.TabIndex = 0;
        inputLabel.Text = "T-SQLクエリ";
        //
        // findPanel
        // Ctrl+F で表示する検索バー。
        //
        findPanel.AutoSize = true;
        findPanel.Controls.Add(findLabel);
        findPanel.Controls.Add(findTextBox);
        findPanel.Controls.Add(findNextButton);
        findPanel.Controls.Add(findPreviousButton);
        findPanel.Controls.Add(closeFindButton);
        findPanel.Dock = DockStyle.Top;
        findPanel.Location = new Point(0, 31);
        findPanel.Margin = new Padding(0, 0, 0, 8);
        findPanel.Name = "findPanel";
        findPanel.Padding = new Padding(0, 0, 0, 8);
        findPanel.Size = new Size(602, 39);
        findPanel.TabIndex = 1;
        findPanel.Visible = false;
        //
        // findLabel
        // 検索欄の説明ラベル。
        //
        findLabel.AutoSize = true;
        findLabel.Location = new Point(0, 0);
        findLabel.Margin = new Padding(0, 7, 8, 0);
        findLabel.Name = "findLabel";
        findLabel.Size = new Size(31, 15);
        findLabel.TabIndex = 0;
        findLabel.Text = "検索";
        //
        // findTextBox
        // 検索文字列入力欄。
        //
        findTextBox.Location = new Point(39, 3);
        findTextBox.Name = "findTextBox";
        findTextBox.Size = new Size(220, 23);
        findTextBox.TabIndex = 1;
        findTextBox.KeyDown += FindTextBox_KeyDown;
        //
        // findNextButton
        // 次の一致位置へ移動する。
        //
        findNextButton.AutoSize = true;
        findNextButton.Location = new Point(267, 0);
        findNextButton.Name = "findNextButton";
        findNextButton.Size = new Size(57, 25);
        findNextButton.TabIndex = 2;
        findNextButton.Text = "次へ";
        findNextButton.UseVisualStyleBackColor = true;
        findNextButton.Click += FindNextButton_Click;
        //
        // findPreviousButton
        // 前の一致位置へ移動する。
        //
        findPreviousButton.AutoSize = true;
        findPreviousButton.Location = new Point(330, 0);
        findPreviousButton.Name = "findPreviousButton";
        findPreviousButton.Size = new Size(57, 25);
        findPreviousButton.TabIndex = 3;
        findPreviousButton.Text = "前へ";
        findPreviousButton.UseVisualStyleBackColor = true;
        findPreviousButton.Click += FindPreviousButton_Click;
        //
        // closeFindButton
        // 検索バーを閉じる。
        //
        closeFindButton.AutoSize = true;
        closeFindButton.Location = new Point(393, 0);
        closeFindButton.Name = "closeFindButton";
        closeFindButton.Size = new Size(57, 25);
        closeFindButton.TabIndex = 4;
        closeFindButton.Text = "閉じる";
        closeFindButton.UseVisualStyleBackColor = true;
        closeFindButton.Click += CloseFindButton_Click;
        //
        // sqlTextBox
        // 複数行の SQL 入力欄。
        // 選択範囲の追跡や強調表示を行うため RichTextBox を使う。
        //
        sqlTextBox.AcceptsTab = true;
        sqlTextBox.DetectUrls = false;
        sqlTextBox.Dock = DockStyle.Fill;
        sqlTextBox.Font = new Font("Consolas", 11F);
        sqlTextBox.HideSelection = false;
        sqlTextBox.Location = new Point(0, 70);
        sqlTextBox.Name = "sqlTextBox";
        sqlTextBox.Size = new Size(602, 617);
        sqlTextBox.TabIndex = 2;
        sqlTextBox.Text = "";
        sqlTextBox.WordWrap = false;
        sqlTextBox.SelectionChanged += SqlTextBox_SelectionChanged;
        sqlTextBox.KeyDown += SqlTextBox_KeyDown;
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
        resultLabel.Size = new Size(76, 31);
        resultLabel.TabIndex = 0;
        resultLabel.Text = "解析結果";
        //
        // resultSearchPanel
        // TreeView 内の検索と絞り込みを行う操作バー。
        //
        resultSearchPanel.AutoSize = true;
        resultSearchPanel.Controls.Add(resultSearchLabel);
        resultSearchPanel.Controls.Add(resultSearchTextBox);
        resultSearchPanel.Controls.Add(resultSearchNextButton);
        resultSearchPanel.Controls.Add(resultSearchPreviousButton);
        resultSearchPanel.Controls.Add(resultFilterButton);
        resultSearchPanel.Controls.Add(resultFilterClearButton);
        resultSearchPanel.Dock = DockStyle.Top;
        resultSearchPanel.Location = new Point(8, 31);
        resultSearchPanel.Margin = new Padding(0, 0, 0, 8);
        resultSearchPanel.Name = "resultSearchPanel";
        resultSearchPanel.Padding = new Padding(0, 0, 0, 8);
        resultSearchPanel.Size = new Size(630, 39);
        resultSearchPanel.TabIndex = 1;
        //
        // resultSearchLabel
        // TreeView 検索欄の説明ラベル。
        //
        resultSearchLabel.AutoSize = true;
        resultSearchLabel.Location = new Point(0, 0);
        resultSearchLabel.Margin = new Padding(0, 7, 8, 0);
        resultSearchLabel.Name = "resultSearchLabel";
        resultSearchLabel.Size = new Size(67, 15);
        resultSearchLabel.TabIndex = 0;
        resultSearchLabel.Text = "ツリー検索";
        //
        // resultSearchTextBox
        // TreeView 内で探す文字列を入力する。
        //
        resultSearchTextBox.Location = new Point(75, 3);
        resultSearchTextBox.Name = "resultSearchTextBox";
        resultSearchTextBox.Size = new Size(180, 23);
        resultSearchTextBox.TabIndex = 1;
        resultSearchTextBox.TextChanged += ResultSearchTextBox_TextChanged;
        resultSearchTextBox.KeyDown += ResultSearchTextBox_KeyDown;
        //
        // resultSearchNextButton
        // 次の一致ノードへ移動する。
        //
        resultSearchNextButton.AutoSize = true;
        resultSearchNextButton.Location = new Point(261, 0);
        resultSearchNextButton.Name = "resultSearchNextButton";
        resultSearchNextButton.Size = new Size(57, 25);
        resultSearchNextButton.TabIndex = 2;
        resultSearchNextButton.Text = "次へ";
        resultSearchNextButton.UseVisualStyleBackColor = true;
        resultSearchNextButton.Click += ResultSearchNextButton_Click;
        //
        // resultSearchPreviousButton
        // 前の一致ノードへ移動する。
        //
        resultSearchPreviousButton.AutoSize = true;
        resultSearchPreviousButton.Location = new Point(324, 0);
        resultSearchPreviousButton.Name = "resultSearchPreviousButton";
        resultSearchPreviousButton.Size = new Size(57, 25);
        resultSearchPreviousButton.TabIndex = 3;
        resultSearchPreviousButton.Text = "前へ";
        resultSearchPreviousButton.UseVisualStyleBackColor = true;
        resultSearchPreviousButton.Click += ResultSearchPreviousButton_Click;
        //
        // resultFilterButton
        // 検索語に一致するノードと祖先だけに TreeView を絞り込む。
        //
        resultFilterButton.AutoSize = true;
        resultFilterButton.Location = new Point(387, 0);
        resultFilterButton.Name = "resultFilterButton";
        resultFilterButton.Size = new Size(57, 25);
        resultFilterButton.TabIndex = 4;
        resultFilterButton.Text = "絞込";
        resultFilterButton.UseVisualStyleBackColor = true;
        resultFilterButton.Click += ResultFilterButton_Click;
        //
        // resultFilterClearButton
        // TreeView の絞り込みと検索語を解除する。
        //
        resultFilterClearButton.AutoSize = true;
        resultFilterClearButton.Location = new Point(450, 0);
        resultFilterClearButton.Name = "resultFilterClearButton";
        resultFilterClearButton.Size = new Size(57, 25);
        resultFilterClearButton.TabIndex = 5;
        resultFilterClearButton.Text = "解除";
        resultFilterClearButton.UseVisualStyleBackColor = true;
        resultFilterClearButton.Click += ResultFilterClearButton_Click;
        //
        // resultDetailPanel
        // 選択ノードや選択 SQL の全文表示欄。
        //
        resultDetailPanel.Controls.Add(detailTextBox);
        resultDetailPanel.Controls.Add(detailLabel);
        resultDetailPanel.Dock = DockStyle.Bottom;
        resultDetailPanel.Location = new Point(8, 503);
        resultDetailPanel.Name = "resultDetailPanel";
        resultDetailPanel.Padding = new Padding(0, 8, 0, 0);
        resultDetailPanel.Size = new Size(630, 184);
        resultDetailPanel.TabIndex = 2;
        //
        // detailLabel
        // 全文表示欄の説明ラベル。
        //
        detailLabel.AutoSize = true;
        detailLabel.Dock = DockStyle.Top;
        detailLabel.Location = new Point(0, 8);
        detailLabel.Name = "detailLabel";
        detailLabel.Padding = new Padding(0, 0, 0, 8);
        detailLabel.Size = new Size(59, 23);
        detailLabel.TabIndex = 0;
        detailLabel.Text = "全文表示";
        //
        // detailTextBox
        // 選択対象の全文や対応 SQL を表示する。
        //
        detailTextBox.DetectUrls = false;
        detailTextBox.Dock = DockStyle.Fill;
        detailTextBox.Font = new Font("Consolas", 10F);
        detailTextBox.HideSelection = false;
        detailTextBox.Location = new Point(0, 31);
        detailTextBox.Name = "detailTextBox";
        detailTextBox.ReadOnly = true;
        detailTextBox.Size = new Size(630, 153);
        detailTextBox.TabIndex = 1;
        detailTextBox.Text = "";
        detailTextBox.WordWrap = false;
        //
        // resultTreeView
        // 構造を追いやすいよう、階層表示を主役にする。
        // SQL 入力欄側から連動選択されたときも強く見えるよう、分類別のアイコンと独自描画を使う。
        //
        resultTreeView.Dock = DockStyle.Fill;
        resultTreeView.DrawMode = TreeViewDrawMode.OwnerDrawText;
        resultTreeView.FullRowSelect = true;
        resultTreeView.HideSelection = false;
        resultTreeView.Location = new Point(8, 70);
        resultTreeView.Name = "resultTreeView";
        resultTreeView.ShowNodeToolTips = true;
        resultTreeView.Size = new Size(630, 433);
        resultTreeView.TabIndex = 2;
        resultTreeView.AfterSelect += ResultTreeView_AfterSelect;
        resultTreeView.DrawNode += ResultTreeView_DrawNode;
        resultTreeView.KeyDown += ResultTreeView_KeyDown;
        resultTreeView.Leave += ResultTreeView_Leave;
        //
        // MainForm
        // シンプルな 2 ペイン構成を維持しつつ、検索と全文表示を足す。
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1280, 760);
        Controls.Add(mainLayoutPanel);
        KeyPreview = true;
        MinimumSize = new Size(1040, 640);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "T-SQL解析ツール";
        Deactivate += MainForm_Deactivate;
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
        findPanel.ResumeLayout(false);
        findPanel.PerformLayout();
        resultSearchPanel.ResumeLayout(false);
        resultSearchPanel.PerformLayout();
        resultDetailPanel.ResumeLayout(false);
        resultDetailPanel.PerformLayout();
        ResumeLayout(false);
    }
}
