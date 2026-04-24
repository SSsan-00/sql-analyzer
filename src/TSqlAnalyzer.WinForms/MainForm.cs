using System.Text;
using TSqlAnalyzer.Application.Editor;
using TSqlAnalyzer.Application.Export;
using TSqlAnalyzer.Application.Formatting;
using TSqlAnalyzer.Application.Presentation;
using TSqlAnalyzer.Application.Services;
using TSqlAnalyzer.Application.Workspace;
using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.WinForms;

/// <summary>
/// メイン画面。
/// フォームは入力取得とイベント処理に責務を絞り、解析そのものはサービスへ委譲する。
/// </summary>
public partial class MainForm : Form
{
    private static readonly Color TreeSelectionBackColor = Color.FromArgb(0, 102, 204);
    private static readonly Color TreeSelectionForeColor = Color.White;
    private static readonly Color TreeSearchBackColor = Color.FromArgb(255, 248, 190);
    private static readonly Color TreeSearchAccentColor = Color.FromArgb(230, 145, 30);
    private static readonly Color SqlLinkedHighlightBackColor = Color.FromArgb(255, 243, 150);
    private static readonly Color SqlParseIssueHighlightBackColor = Color.FromArgb(255, 214, 214);

    private readonly IQueryAnalysisService _analysisService;
    private readonly QueryAnalysisTreeBuilder _treeBuilder;
    private readonly ColumnTextExportBuilder _columnTextExportBuilder;
    private readonly SqlFormattingService _sqlFormattingService;
    private readonly ParseIssueTextSpanResolver _parseIssueTextSpanResolver;
    private readonly SqlInputAssistService _sqlInputAssistService;
    private readonly Panel _parseIssuePanel;
    private readonly Label _parseIssueLabel;
    private readonly ListBox _parseIssueListBox;
    private readonly ListBox _completionListBox;

    private QueryAnalysisResult? _currentAnalysis;
    private DisplayTreeNode? _currentTree;
    private DisplayTreeNode? _displayedTree;
    private TextSpan? _highlightedSqlSpan;
    private TextSpan? _parseIssueHighlightedSqlSpan;
    private string _treeSearchText = string.Empty;
    private bool _isTreeFilterActive;
    private bool _suppressSqlSelectionSync;
    private bool _suppressTreeSelectionSync;
    private bool _suppressTreeSearchTextChanged;
    private bool _suppressCompletionRefresh;
    private SqlInputAssistResult? _currentInputAssistResult;

    /// <summary>
    /// 必要なサービスを受け取って初期化する。
    /// </summary>
    public MainForm(
        IQueryAnalysisService analysisService,
        QueryAnalysisTreeBuilder treeBuilder,
        ColumnTextExportBuilder columnTextExportBuilder,
        SqlFormattingService sqlFormattingService,
        ParseIssueTextSpanResolver parseIssueTextSpanResolver,
        SqlInputAssistService sqlInputAssistService,
        WorkspaceStateManager workspaceStateManager,
        JsonWorkspaceStateStore workspaceStateStore)
    {
        _analysisService = analysisService;
        _treeBuilder = treeBuilder;
        _columnTextExportBuilder = columnTextExportBuilder;
        _sqlFormattingService = sqlFormattingService;
        _parseIssueTextSpanResolver = parseIssueTextSpanResolver;
        _sqlInputAssistService = sqlInputAssistService;
        _workspaceStateManager = workspaceStateManager;
        _workspaceStateStore = workspaceStateStore;

        InitializeComponent();
        InitializeWorkspaceUi();
        _parseIssuePanel = CreateParseIssuePanel();
        _parseIssueLabel = CreateParseIssueLabel();
        _parseIssueListBox = CreateParseIssueListBox();
        _completionListBox = CreateCompletionListBox();
        _parseIssuePanel.Controls.Add(_parseIssueListBox);
        _parseIssuePanel.Controls.Add(_parseIssueLabel);
        mainSplitContainer.Panel1.Controls.Add(_parseIssuePanel);
        mainSplitContainer.Panel1.Controls.Add(_completionListBox);
        _parseIssuePanel.BringToFront();
        _completionListBox.BringToFront();

        ConfigureResultTreeViewVisuals();
        ConfigureEditorAssistControls();
        InitializeWorkspaceState();
    }

    /// <summary>
    /// 解析結果 TreeView の見た目を初期化する。
    /// 標準 TreeView の ImageList と独自描画だけを使い、外部 UI ライブラリには依存しない。
    /// </summary>
    private void ConfigureResultTreeViewVisuals()
    {
        resultTreeView.ImageList = TreeViewImageListFactory.Create(components);
    }

    /// <summary>
    /// SQL 入力欄に対するエラー表示と入力補助の補助 UI を初期化する。
    /// オーバーレイ方式にして、既存レイアウトへの影響を最小化する。
    /// </summary>
    private void ConfigureEditorAssistControls()
    {
        sqlTextBox.TextChanged += SqlTextBox_TextChanged;
        mainSplitContainer.Panel1.Resize += InputPane_Resize;
        sqlTextBox.Resize += InputPane_Resize;
        UpdateEditorOverlayLayout();
    }

    /// <summary>
    /// 構文エラー一覧を表示するオーバーレイパネルを作る。
    /// </summary>
    private static Panel CreateParseIssuePanel()
    {
        return new Panel
        {
            BackColor = Color.FromArgb(255, 246, 246),
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(8),
            Visible = false
        };
    }

    /// <summary>
    /// 構文エラー一覧の見出しラベルを作る。
    /// </summary>
    private static Label CreateParseIssueLabel()
    {
        return new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 6),
            Text = "構文エラー"
        };
    }

    /// <summary>
    /// 構文エラー一覧本体を作る。
    /// 選択変更で SQL 側の該当位置へ移動できるようにする。
    /// </summary>
    private ListBox CreateParseIssueListBox()
    {
        var listBox = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10F),
            HorizontalScrollbar = true,
            IntegralHeight = false
        };

        listBox.SelectedIndexChanged += ParseIssueListBox_SelectedIndexChanged;
        listBox.DoubleClick += ParseIssueListBox_DoubleClick;
        return listBox;
    }

    /// <summary>
    /// 補完候補を表示するポップアップ一覧を作る。
    /// SQL 入力欄の近くへ重ねて表示し、Ctrl+Space や矢印キーで選べるようにする。
    /// </summary>
    private ListBox CreateCompletionListBox()
    {
        var listBox = new ListBox
        {
            Font = new Font("Consolas", 10F),
            HorizontalScrollbar = true,
            IntegralHeight = false,
            Visible = false
        };

        listBox.Click += CompletionListBox_Click;
        listBox.DoubleClick += CompletionListBox_DoubleClick;
        return listBox;
    }

    /// <summary>
    /// 解析ボタン押下時の処理。
    /// 入力 SQL を解析し、TreeView 用モデルへ変換して画面へ反映する。
    /// </summary>
    private void AnalyzeButton_Click(object? sender, EventArgs e)
    {
        AnalyzeCurrentSql();
    }

    /// <summary>
    /// 入力欄の SQL を整形する。
    /// 整形後に既存解析結果があれば再解析し、位置連動の整合を保つ。
    /// </summary>
    private void FormatButton_Click(object? sender, EventArgs e)
    {
        var formatResult = _sqlFormattingService.Format(sqlTextBox.Text);
        if (!formatResult.IsSuccess)
        {
            ShowParseIssues(formatResult.ParseIssues);
            HideCompletionPopup();
            return;
        }

        var formattedText = formatResult.FormattedSql.ReplaceLineEndings(Environment.NewLine);
        var currentText = sqlTextBox.Text.ReplaceLineEndings(Environment.NewLine);
        var shouldRefreshAnalysis = _currentTree is not null || resultTreeView.Nodes.Count > 0;

        HideCompletionPopup();
        ClearParseIssues();

        if (!string.Equals(formattedText, currentText, StringComparison.Ordinal))
        {
            ReplaceSqlEditorText(formattedText);
        }

        if (shouldRefreshAnalysis)
        {
            AnalyzeCurrentSql();
            return;
        }

        ClearSqlLinkedHighlight();
        UpdateDetailTextForSelection();
    }

    /// <summary>
    /// 現在の入力欄を解析し、表示中の TreeView と詳細欄を更新する。
    /// </summary>
    private void AnalyzeCurrentSql()
    {
        HideCompletionPopup();

        var analysis = _analysisService.Analyze(sqlTextBox.Text);
        var tree = _treeBuilder.Build(analysis);

        _currentAnalysis = analysis;
        _currentTree = tree;
        _displayedTree = tree;
        _isTreeFilterActive = false;
        _treeSearchText = string.Empty;
        _suppressTreeSearchTextChanged = true;
        try
        {
            resultSearchTextBox.Clear();
        }
        finally
        {
            _suppressTreeSearchTextChanged = false;
        }

        ClearSqlLinkedHighlight();
        ShowParseIssues(analysis.ParseIssues);
        AnalysisTreeViewBinder.Bind(resultTreeView, tree);
        UpdateDetailText(resultTreeView.SelectedNode);
        SyncTreeSelectionFromSql();
    }

    /// <summary>
    /// 入力欄と表示結果をまとめて初期化する。
    /// </summary>
    private void ClearButton_Click(object? sender, EventArgs e)
    {
        _currentAnalysis = null;
        _currentTree = null;
        _displayedTree = null;
        _isTreeFilterActive = false;
        _treeSearchText = string.Empty;
        _highlightedSqlSpan = null;
        _parseIssueHighlightedSqlSpan = null;
        sqlTextBox.Clear();
        resultTreeView.Nodes.Clear();
        detailTextBox.Clear();
        findTextBox.Clear();
        resultSearchTextBox.Clear();
        findPanel.Visible = false;
        HideCompletionPopup();
        ClearParseIssues();
        sqlTextBox.Focus();
    }

    /// <summary>
    /// 解析結果から列確認用テキストを生成し、利用者が指定した場所へ保存する。
    /// テキストの組み立ては Application 層へ委譲し、フォームは保存操作だけを担当する。
    /// </summary>
    private void ExportTextButton_Click(object? sender, EventArgs e)
    {
        if (_currentAnalysis is null)
        {
            MessageBox.Show(
                this,
                "先に解析を実行してください。",
                "列情報エクスポート",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var exportText = _columnTextExportBuilder.Build(_currentAnalysis);
        if (string.IsNullOrWhiteSpace(exportText))
        {
            MessageBox.Show(
                this,
                "エクスポート対象の列情報がありません。",
                "列情報エクスポート",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var saveFileDialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "txt",
            FileName = $"tsql-column-export-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            Filter = "テキスト ファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
            OverwritePrompt = true,
            Title = "列情報エクスポート"
        };

        if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        File.WriteAllText(saveFileDialog.FileName, exportText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>
    /// TreeView の選択変更時に、対応する SQL を強調表示する。
    /// </summary>
    private void ResultTreeView_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        UpdateDetailText(e.Node);

        if (_suppressTreeSelectionSync)
        {
            return;
        }

        var displayNode = AnalysisTreeViewBinder.GetDisplayNode(e.Node);
        HighlightSqlSpan(displayNode?.SourceSpan);
    }

    /// <summary>
    /// 解析結果側のノードを表示分類に応じて描画する。
    /// 標準 TreeView のまま外部依存を増やさず、色と太字で構造の違いを追いやすくする。
    /// </summary>
    private void ResultTreeView_DrawNode(object? sender, DrawTreeNodeEventArgs e)
    {
        if (e.Node is null)
        {
            return;
        }

        var displayNode = AnalysisTreeViewBinder.GetDisplayNode(e.Node);
        var visualStyle = TreeNodeVisualCatalog.GetStyle(displayNode?.Kind ?? DisplayTreeNodeKind.Detail);
        var isSearchMatch = displayNode is not null && DisplayTreeSearch.IsMatch(displayNode, _treeSearchText);
        var isSelected = ((e.State & TreeNodeStates.Selected) == TreeNodeStates.Selected)
            || ReferenceEquals(resultTreeView.SelectedNode, e.Node);
        var baseFont = e.Node.NodeFont ?? resultTreeView.Font;
        using var drawFont = new Font(baseFont, baseFont.Style | visualStyle.FontStyle);
        var rowBounds = new Rectangle(
            e.Bounds.Left,
            e.Bounds.Top,
            Math.Max(0, resultTreeView.ClientSize.Width - e.Bounds.Left),
            e.Bounds.Height);

        if (isSelected)
        {
            using var backgroundBrush = new SolidBrush(TreeSelectionBackColor);
            e.Graphics.FillRectangle(backgroundBrush, rowBounds);
        }
        else
        {
            var backgroundColor = isSearchMatch
                ? TreeSearchBackColor
                : (visualStyle.BackgroundColor.IsEmpty
                    ? resultTreeView.BackColor
                    : visualStyle.BackgroundColor);
            var backgroundBounds = isSearchMatch || !visualStyle.BackgroundColor.IsEmpty ? rowBounds : e.Bounds;
            using var backgroundBrush = new SolidBrush(backgroundColor);
            e.Graphics.FillRectangle(backgroundBrush, backgroundBounds);

            if (isSearchMatch)
            {
                using var searchPen = new Pen(TreeSearchAccentColor, 3F);
                e.Graphics.DrawLine(
                    searchPen,
                    e.Bounds.Left,
                    e.Bounds.Top + 2,
                    e.Bounds.Left,
                    e.Bounds.Bottom - 2);
            }
            else if (!visualStyle.BackgroundColor.IsEmpty)
            {
                using var accentPen = new Pen(visualStyle.AccentColor, 2F);
                e.Graphics.DrawLine(
                    accentPen,
                    e.Bounds.Left,
                    e.Bounds.Top + 2,
                    e.Bounds.Left,
                    e.Bounds.Bottom - 2);
            }
        }

        TextRenderer.DrawText(
            e.Graphics,
            e.Node.Text,
            drawFont,
            e.Bounds,
            isSelected ? TreeSelectionForeColor : visualStyle.ForeColor,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    /// <summary>
    /// TreeView 検索語の変更に合わせて、検索ハイライトまたはフィルタ結果を更新する。
    /// </summary>
    private void ResultSearchTextBox_TextChanged(object? sender, EventArgs e)
    {
        if (_suppressTreeSearchTextChanged)
        {
            return;
        }

        _treeSearchText = resultSearchTextBox.Text.Trim();

        if (_isTreeFilterActive)
        {
            ApplyTreeFilter();
            return;
        }

        resultTreeView.Invalidate();
    }

    /// <summary>
    /// TreeView 検索欄では Enter / Shift+Enter / Escape をショートカットとして扱う。
    /// </summary>
    private void ResultSearchTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            SelectNextTreeSearchMatch(reverse: e.Shift);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            ResetTreeFilterAndSearch();
            e.SuppressKeyPress = true;
        }
    }

    /// <summary>
    /// TreeView 内の次の一致ノードへ移動する。
    /// </summary>
    private void ResultSearchNextButton_Click(object? sender, EventArgs e)
    {
        SelectNextTreeSearchMatch(reverse: false);
    }

    /// <summary>
    /// TreeView 内の前の一致ノードへ移動する。
    /// </summary>
    private void ResultSearchPreviousButton_Click(object? sender, EventArgs e)
    {
        SelectNextTreeSearchMatch(reverse: true);
    }

    /// <summary>
    /// 検索語に一致するノードと祖先だけを TreeView に表示する。
    /// </summary>
    private void ResultFilterButton_Click(object? sender, EventArgs e)
    {
        _treeSearchText = resultSearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(_treeSearchText))
        {
            resultSearchTextBox.Focus();
            return;
        }

        _isTreeFilterActive = true;
        ApplyTreeFilter();
    }

    /// <summary>
    /// TreeView の検索語と絞り込みを解除する。
    /// </summary>
    private void ResultFilterClearButton_Click(object? sender, EventArgs e)
    {
        ResetTreeFilterAndSearch();
    }

    /// <summary>
    /// TreeView 上のショートカットを扱う。
    /// Ctrl+F でツリー検索欄へ移動し、F3 / Shift+F3 で一致ノードを前後に辿る。
    /// </summary>
    private void ResultTreeView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.F)
        {
            resultSearchTextBox.Focus();
            resultSearchTextBox.SelectAll();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.F3)
        {
            SelectNextTreeSearchMatch(reverse: e.Shift);
            e.SuppressKeyPress = true;
        }
    }

    /// <summary>
    /// TreeView 検索語に一致する次または前のノードを選択する。
    /// 選択したノードの SQL 断片も通常の TreeView 選択と同じ経路で強調表示する。
    /// </summary>
    private void SelectNextTreeSearchMatch(bool reverse)
    {
        _treeSearchText = resultSearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(_treeSearchText))
        {
            resultSearchTextBox.Focus();
            return;
        }

        var matches = AnalysisTreeViewBinder.FindMatchingTreeNodes(resultTreeView, _treeSearchText);
        if (matches.Count == 0)
        {
            resultTreeView.Invalidate();
            return;
        }

        var currentIndex = resultTreeView.SelectedNode is null
            ? -1
            : matches
                .Select((node, index) => new { node, index })
                .FirstOrDefault(pair => ReferenceEquals(pair.node, resultTreeView.SelectedNode))
                ?.index ?? -1;

        var nextIndex = reverse
            ? currentIndex <= 0 ? matches.Count - 1 : currentIndex - 1
            : currentIndex < 0 || currentIndex >= matches.Count - 1 ? 0 : currentIndex + 1;

        var nextNode = matches[nextIndex];
        resultTreeView.SelectedNode = nextNode;
        nextNode.EnsureVisible();
        resultTreeView.Focus();
        resultTreeView.Invalidate();
    }

    /// <summary>
    /// 現在の検索語で TreeView を絞り込む。
    /// 一致がない場合も専用ノードを表示し、空白画面にならないようにする。
    /// </summary>
    private void ApplyTreeFilter()
    {
        if (_currentTree is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_treeSearchText))
        {
            _displayedTree = _currentTree;
            AnalysisTreeViewBinder.Bind(resultTreeView, _currentTree);
            return;
        }

        var filteredTree = DisplayTreeSearch.Filter(_currentTree, _treeSearchText)
            ?? new DisplayTreeNode(
                $"検索結果なし: {_treeSearchText}",
                [],
                Kind: DisplayTreeNodeKind.Section);

        _displayedTree = filteredTree;
        AnalysisTreeViewBinder.Bind(resultTreeView, filteredTree, expandAll: true);
        resultTreeView.Invalidate();
    }

    /// <summary>
    /// TreeView 検索と絞り込みを解除し、元の解析結果ツリーへ戻す。
    /// </summary>
    private void ResetTreeFilterAndSearch()
    {
        _treeSearchText = string.Empty;
        _isTreeFilterActive = false;
        _suppressTreeSearchTextChanged = true;
        try
        {
            resultSearchTextBox.Clear();
        }
        finally
        {
            _suppressTreeSearchTextChanged = false;
        }

        if (_currentTree is not null)
        {
            _displayedTree = _currentTree;
            AnalysisTreeViewBinder.Bind(resultTreeView, _currentTree);
            SyncTreeSelectionFromSql();
        }
        else
        {
            _displayedTree = null;
            resultTreeView.Nodes.Clear();
        }

        resultTreeView.Invalidate();
    }

    /// <summary>
    /// SQL 側の選択変更に合わせて、最も近い解析ノードを選択する。
    /// </summary>
    private void SqlTextBox_SelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressSqlSelectionSync)
        {
            return;
        }

        if (sqlTextBox.SelectionLength > 0)
        {
            HideCompletionPopup();
        }
        else if (_completionListBox.Visible)
        {
            UpdateCompletionPopupLocation();
        }

        ClearSqlLinkedHighlight();
        SyncTreeSelectionFromSql();
    }

    /// <summary>
    /// TreeView から別コントロールへ移動したら、SQL 側の一時ハイライトを解除する。
    /// 選択連動の文脈が切れた後に黄色い背景だけが残る状態を避ける。
    /// </summary>
    private void ResultTreeView_Leave(object? sender, EventArgs e)
    {
        ClearSqlLinkedHighlight();
    }

    /// <summary>
    /// アプリ外へフォーカスが移った場合も、SQL 側の一時ハイライトを解除する。
    /// TreeView の選択関係を確認している間だけ黄色背景を残し、文脈が切れたら消す。
    /// </summary>
    private void MainForm_Deactivate(object? sender, EventArgs e)
    {
        ClearSqlLinkedHighlight();
    }

    /// <summary>
    /// SQL が編集されたら、古い構文エラー表示を外し、補完ポップアップを必要に応じて更新する。
    /// 編集後の行位置に合わせてオーバーレイ位置も更新する。
    /// </summary>
    private void SqlTextBox_TextChanged(object? sender, EventArgs e)
    {
        if (!_suppressWorkspaceTextSync)
        {
            _workspaceState = _workspaceStateManager.UpdateSelectedQuerySql(_workspaceState, sqlTextBox.Text);
            ScheduleWorkspaceSave();
        }

        ClearParseIssues();
        UpdateEditorOverlayLayout();

        if (_suppressCompletionRefresh)
        {
            return;
        }

        if (_completionListBox.Visible)
        {
            RefreshCompletionPopup();
        }
    }

    /// <summary>
    /// 入力欄まわりのサイズが変わったら、エラー一覧と補完ポップアップの表示位置を補正する。
    /// </summary>
    private void InputPane_Resize(object? sender, EventArgs e)
    {
        UpdateEditorOverlayLayout();
    }

    /// <summary>
    /// 構文エラー一覧を表示し、先頭エラーへ自動移動する。
    /// 行・列だけでなく入力欄上の赤ハイライトも付け、原因箇所を見つけやすくする。
    /// </summary>
    private void ShowParseIssues(IReadOnlyList<ParseIssue> parseIssues)
    {
        ClearSqlParseIssueHighlight();

        _parseIssueListBox.BeginUpdate();
        try
        {
            _parseIssueListBox.Items.Clear();

            foreach (var parseIssue in parseIssues)
            {
                _parseIssueListBox.Items.Add(new ParseIssueListItem(parseIssue));
            }
        }
        finally
        {
            _parseIssueListBox.EndUpdate();
        }

        _parseIssuePanel.Visible = parseIssues.Count > 0;
        _parseIssueLabel.Text = parseIssues.Count switch
        {
            0 => "構文エラー",
            1 => "構文エラー 1件",
            _ => $"構文エラー {parseIssues.Count}件"
        };

        UpdateEditorOverlayLayout();

        if (parseIssues.Count == 0)
        {
            return;
        }

        _parseIssueListBox.SelectedIndex = 0;
        FocusParseIssue(parseIssues[0]);
    }

    /// <summary>
    /// 構文エラー一覧と赤ハイライトを解除する。
    /// SQL 編集後に位置情報が古くならないよう、再解析前の表示は残さない。
    /// </summary>
    private void ClearParseIssues()
    {
        ClearSqlParseIssueHighlight();

        if (_parseIssueListBox.Items.Count > 0)
        {
            _parseIssueListBox.BeginUpdate();
            try
            {
                _parseIssueListBox.Items.Clear();
            }
            finally
            {
                _parseIssueListBox.EndUpdate();
            }
        }

        _parseIssuePanel.Visible = false;
    }

    /// <summary>
    /// エラー一覧の選択変更時に、対応する SQL 位置へ移動する。
    /// </summary>
    private void ParseIssueListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_parseIssueListBox.SelectedItem is ParseIssueListItem item)
        {
            FocusParseIssue(item.ParseIssue);
        }
    }

    /// <summary>
    /// ダブルクリック時は SQL 入力欄へ戻し、修正作業へすぐ移れるようにする。
    /// </summary>
    private void ParseIssueListBox_DoubleClick(object? sender, EventArgs e)
    {
        if (_parseIssueListBox.SelectedItem is ParseIssueListItem item)
        {
            FocusParseIssue(item.ParseIssue);
            sqlTextBox.Focus();
        }
    }

    /// <summary>
    /// 構文エラー 1 件を入力欄上で赤く強調表示する。
    /// TreeView 連動ハイライトとは別扱いにし、構文エラーを明確に見せる。
    /// </summary>
    private void FocusParseIssue(ParseIssue parseIssue)
    {
        if (!_parseIssueTextSpanResolver.TryResolve(sqlTextBox.Text, parseIssue, out var sourceSpan))
        {
            ClearSqlParseIssueHighlight();
            return;
        }

        if (!TryClampSpan(sourceSpan, out var clampedSpan))
        {
            ClearSqlParseIssueHighlight();
            return;
        }

        _suppressSqlSelectionSync = true;
        try
        {
            ClearSqlLinkedHighlightCore();
            ClearSqlParseIssueHighlightCore();
            ApplySqlHighlight(clampedSpan, SqlParseIssueHighlightBackColor);
            _parseIssueHighlightedSqlSpan = clampedSpan;
            sqlTextBox.Select(clampedSpan.Start, clampedSpan.Length);
            sqlTextBox.ScrollToCaret();
        }
        finally
        {
            _suppressSqlSelectionSync = false;
        }
    }

    /// <summary>
    /// 構文エラー用の赤ハイライトを消す。
    /// </summary>
    private void ClearSqlParseIssueHighlight()
    {
        _suppressSqlSelectionSync = true;
        try
        {
            ClearSqlParseIssueHighlightCore();
        }
        finally
        {
            _suppressSqlSelectionSync = false;
        }
    }

    /// <summary>
    /// 赤ハイライトの内部解除処理。
    /// </summary>
    private void ClearSqlParseIssueHighlightCore()
    {
        ClearSqlHighlightCore(ref _parseIssueHighlightedSqlSpan);
    }

    /// <summary>
    /// Ctrl+Space や入力継続時に補完候補を更新して表示する。
    /// </summary>
    private void RefreshCompletionPopup()
    {
        var assistResult = _sqlInputAssistService.GetSuggestions(sqlTextBox.Text, sqlTextBox.SelectionStart);
        _currentInputAssistResult = assistResult;

        if (assistResult.Items.Count == 0)
        {
            HideCompletionPopup();
            return;
        }

        _completionListBox.BeginUpdate();
        try
        {
            _completionListBox.Items.Clear();

            foreach (var item in assistResult.Items.Take(20))
            {
                _completionListBox.Items.Add(new CompletionListItem(item));
            }
        }
        finally
        {
            _completionListBox.EndUpdate();
        }

        if (_completionListBox.Items.Count == 0)
        {
            HideCompletionPopup();
            return;
        }

        _completionListBox.SelectedIndex = 0;
        _completionListBox.Visible = true;
        UpdateCompletionPopupLocation();
    }

    /// <summary>
    /// 補完ポップアップを閉じる。
    /// </summary>
    private void HideCompletionPopup()
    {
        _completionListBox.Visible = false;
        _completionListBox.Items.Clear();
        _currentInputAssistResult = null;
    }

    /// <summary>
    /// 補完候補の選択位置を上下へ動かす。
    /// </summary>
    private void MoveCompletionSelection(int delta)
    {
        if (!_completionListBox.Visible || _completionListBox.Items.Count == 0)
        {
            return;
        }

        var nextIndex = _completionListBox.SelectedIndex;
        if (nextIndex < 0)
        {
            nextIndex = 0;
        }
        else
        {
            nextIndex = Math.Clamp(nextIndex + delta, 0, _completionListBox.Items.Count - 1);
        }

        _completionListBox.SelectedIndex = nextIndex;
    }

    /// <summary>
    /// 現在選択中の補完候補を SQL 入力欄へ差し込む。
    /// </summary>
    private void CommitSelectedCompletionItem()
    {
        if (!_completionListBox.Visible
            || _completionListBox.SelectedItem is not CompletionListItem completionListItem
            || _currentInputAssistResult is null)
        {
            return;
        }

        _suppressCompletionRefresh = true;
        _suppressSqlSelectionSync = true;
        try
        {
            var replaceStart = Math.Clamp(_currentInputAssistResult.ReplaceStart, 0, sqlTextBox.TextLength);
            var replaceLength = Math.Min(_currentInputAssistResult.ReplaceLength, sqlTextBox.TextLength - replaceStart);
            sqlTextBox.Select(replaceStart, Math.Max(0, replaceLength));
            sqlTextBox.SelectedText = completionListItem.Item.InsertText;
            sqlTextBox.Select(replaceStart + completionListItem.Item.InsertText.Length, 0);
        }
        finally
        {
            _suppressSqlSelectionSync = false;
            _suppressCompletionRefresh = false;
        }

        HideCompletionPopup();
        sqlTextBox.Focus();
    }

    /// <summary>
    /// 補完候補クリック時の反映処理。
    /// </summary>
    private void CompletionListBox_Click(object? sender, EventArgs e)
    {
        CommitSelectedCompletionItem();
    }

    /// <summary>
    /// ダブルクリックでも同じく補完を確定する。
    /// </summary>
    private void CompletionListBox_DoubleClick(object? sender, EventArgs e)
    {
        CommitSelectedCompletionItem();
    }

    /// <summary>
    /// SQL 入力欄上の補助オーバーレイ位置を更新する。
    /// 検索バーの表示有無やフォームリサイズに追従させる。
    /// </summary>
    private void UpdateEditorOverlayLayout()
    {
        var overlayLeft = sqlTextBox.Left + 8;
        var overlayTop = sqlTextBox.Top + 8;
        var overlayWidth = Math.Max(220, sqlTextBox.Width - 16);

        _parseIssuePanel.Bounds = new Rectangle(
            overlayLeft,
            overlayTop,
            overlayWidth,
            Math.Min(120, Math.Max(88, sqlTextBox.Height / 4)));

        if (_completionListBox.Visible)
        {
            UpdateCompletionPopupLocation();
        }
    }

    /// <summary>
    /// 補完ポップアップをキャレット付近へ配置する。
    /// </summary>
    private void UpdateCompletionPopupLocation()
    {
        if (!_completionListBox.Visible)
        {
            return;
        }

        var caretIndex = Math.Min(sqlTextBox.SelectionStart, sqlTextBox.TextLength);
        var caretPoint = sqlTextBox.GetPositionFromCharIndex(caretIndex);
        var lineHeight = Math.Max(18, TextRenderer.MeasureText("A", sqlTextBox.Font).Height);
        var width = Math.Min(420, Math.Max(260, sqlTextBox.Width / 2));
        var height = Math.Min(220, Math.Max(80, _completionListBox.Items.Count * 18 + 8));
        var left = sqlTextBox.Left + caretPoint.X + 4;
        var top = sqlTextBox.Top + caretPoint.Y + lineHeight + 6;

        left = Math.Min(left, Math.Max(sqlTextBox.Left + 8, mainSplitContainer.Panel1.ClientSize.Width - width - 12));
        top = Math.Min(top, Math.Max(sqlTextBox.Top + 8, mainSplitContainer.Panel1.ClientSize.Height - height - 12));

        _completionListBox.Bounds = new Rectangle(left, top, width, height);
        _completionListBox.BringToFront();
    }

    /// <summary>
    /// SQL 入力欄で Ctrl+F が押されたら検索バーを開く。
    /// </summary>
    private void SqlTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_completionListBox.Visible)
        {
            switch (e.KeyCode)
            {
                case Keys.Up:
                    MoveCompletionSelection(-1);
                    e.SuppressKeyPress = true;
                    return;
                case Keys.Down:
                    MoveCompletionSelection(1);
                    e.SuppressKeyPress = true;
                    return;
                case Keys.Enter:
                case Keys.Tab:
                    CommitSelectedCompletionItem();
                    e.SuppressKeyPress = true;
                    return;
                case Keys.Escape:
                    HideCompletionPopup();
                    e.SuppressKeyPress = true;
                    return;
            }
        }

        if (e.Control && e.Shift && e.KeyCode == Keys.F)
        {
            FormatButton_Click(sender, EventArgs.Empty);
            e.SuppressKeyPress = true;
            return;
        }

        if ((e.Control && e.KeyCode == Keys.Y)
            || (e.Control && e.Shift && e.KeyCode == Keys.Z))
        {
            if (sqlTextBox.CanRedo)
            {
                sqlTextBox.Redo();
            }

            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.Z)
        {
            if (sqlTextBox.CanUndo)
            {
                sqlTextBox.Undo();
            }

            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.Space)
        {
            RefreshCompletionPopup();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.F)
        {
            HideCompletionPopup();
            ShowFindPanel();
            e.SuppressKeyPress = true;
        }
    }

    /// <summary>
    /// 検索欄では Enter / Shift+Enter / Escape をショートカットとして扱う。
    /// </summary>
    private void FindTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            FindNextOccurrence(reverse: e.Shift);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            HideFindPanel();
            e.SuppressKeyPress = true;
        }
    }

    /// <summary>
    /// 次の一致位置へ移動する。
    /// </summary>
    private void FindNextButton_Click(object? sender, EventArgs e)
    {
        FindNextOccurrence(reverse: false);
    }

    /// <summary>
    /// 前の一致位置へ移動する。
    /// </summary>
    private void FindPreviousButton_Click(object? sender, EventArgs e)
    {
        FindNextOccurrence(reverse: true);
    }

    /// <summary>
    /// 検索バーを閉じて SQL 入力欄へ戻る。
    /// </summary>
    private void CloseFindButton_Click(object? sender, EventArgs e)
    {
        HideFindPanel();
    }

    /// <summary>
    /// SQL 側の選択位置から、最も近い解析ノードを選ぶ。
    /// </summary>
    private void SyncTreeSelectionFromSql()
    {
        var treeForSelection = _displayedTree ?? _currentTree;
        if (treeForSelection is null)
        {
            UpdateDetailTextForSelection();
            return;
        }

        var match = DisplayTreeNodeNavigator.FindBestMatch(
            treeForSelection,
            sqlTextBox.SelectionStart,
            sqlTextBox.SelectionLength);

        if (match is null)
        {
            UpdateDetailTextForSelection();
            return;
        }

        var treeNode = AnalysisTreeViewBinder.FindTreeNode(resultTreeView, match);
        if (treeNode is null)
        {
            UpdateDetailTextForSelection();
            return;
        }

        if (!ReferenceEquals(resultTreeView.SelectedNode, treeNode))
        {
            _suppressTreeSelectionSync = true;
            try
            {
                resultTreeView.SelectedNode = treeNode;
                treeNode.EnsureVisible();
            }
            finally
            {
                _suppressTreeSelectionSync = false;
            }
        }

        UpdateDetailText(treeNode);
    }

    /// <summary>
    /// TreeView 側の選択に合わせて SQL を選択状態へする。
    /// </summary>
    private void HighlightSqlSpan(TextSpan? sourceSpan)
    {
        if (sourceSpan is null)
        {
            ClearSqlLinkedHighlight();
            return;
        }

        if (!TryClampSpan(sourceSpan, out var clampedSpan))
        {
            ClearSqlLinkedHighlight();
            return;
        }

        _suppressSqlSelectionSync = true;
        try
        {
            ClearSqlLinkedHighlightCore();
            ClearSqlParseIssueHighlightCore();
            ApplySqlLinkedHighlight(clampedSpan);
            _highlightedSqlSpan = clampedSpan;
            sqlTextBox.Select(clampedSpan.Start, clampedSpan.Length);
            sqlTextBox.ScrollToCaret();
        }
        finally
        {
            _suppressSqlSelectionSync = false;
        }
    }

    /// <summary>
    /// SQL 側に残っている TreeView 連動ハイライトを消す。
    /// 解析し直しや TreeView の非 SQL ノード選択時に、古い対応箇所が残らないようにする。
    /// </summary>
    private void ClearSqlLinkedHighlight()
    {
        _suppressSqlSelectionSync = true;
        try
        {
            ClearSqlLinkedHighlightCore();
        }
        finally
        {
            _suppressSqlSelectionSync = false;
        }
    }

    /// <summary>
    /// 選択状態を保ちながら、前回の SQL 背景ハイライトを解除する。
    /// </summary>
    private void ClearSqlLinkedHighlightCore()
    {
        ClearSqlHighlightCore(ref _highlightedSqlSpan);
    }

    /// <summary>
    /// TreeView 選択に対応する SQL 範囲へ黄色の背景色を付ける。
    /// 選択色だけに頼らず、TreeView から戻ったときも対応箇所を見つけやすくする。
    /// </summary>
    private void ApplySqlLinkedHighlight(TextSpan sourceSpan)
    {
        ApplySqlHighlight(sourceSpan, SqlLinkedHighlightBackColor);
    }

    /// <summary>
    /// 指定されたハイライト状態を元に戻す共通処理。
    /// SQL 連動ハイライトと構文エラーハイライトの両方で使い回す。
    /// </summary>
    private void ClearSqlHighlightCore(ref TextSpan? highlightedSpan)
    {
        if (highlightedSpan is null)
        {
            return;
        }

        var originalSelectionStart = sqlTextBox.SelectionStart;
        var originalSelectionLength = sqlTextBox.SelectionLength;

        if (TryClampSpan(highlightedSpan, out var clampedSpan))
        {
            sqlTextBox.Select(clampedSpan.Start, clampedSpan.Length);
            sqlTextBox.SelectionBackColor = sqlTextBox.BackColor;
        }

        highlightedSpan = null;

        if (originalSelectionStart >= 0 && originalSelectionStart <= sqlTextBox.TextLength)
        {
            var safeLength = Math.Min(originalSelectionLength, sqlTextBox.TextLength - originalSelectionStart);
            sqlTextBox.Select(originalSelectionStart, Math.Max(0, safeLength));
        }
    }

    /// <summary>
    /// SQL 入力欄の任意範囲へ背景色ハイライトを付ける共通処理。
    /// </summary>
    private void ApplySqlHighlight(TextSpan sourceSpan, Color backColor)
    {
        sqlTextBox.Select(sourceSpan.Start, sourceSpan.Length);
        sqlTextBox.SelectionBackColor = backColor;
    }

    /// <summary>
    /// 選択中の Tree ノードに応じて、全文表示欄を更新する。
    /// </summary>
    private void UpdateDetailText(TreeNode? treeNode)
    {
        var displayNode = AnalysisTreeViewBinder.GetDisplayNode(treeNode);
        if (displayNode?.SourceSpan is { } sourceSpan
            && TryExtractSourceText(sourceSpan, out var sourceText))
        {
            detailTextBox.Text = sourceText;
            return;
        }

        detailTextBox.Text = treeNode?.Text ?? string.Empty;
    }

    /// <summary>
    /// SQL 側で選択中の文字列を全文表示欄へ出す。
    /// 対応する Tree ノードが見つからない場合の補助表示として使う。
    /// </summary>
    private void UpdateDetailTextForSelection()
    {
        if (sqlTextBox.SelectionLength > 0)
        {
            detailTextBox.Text = sqlTextBox.SelectedText;
            return;
        }

        detailTextBox.Clear();
    }

    /// <summary>
    /// 検索バーを表示し、検索語入力へフォーカスする。
    /// </summary>
    private void ShowFindPanel()
    {
        findPanel.Visible = true;
        UpdateEditorOverlayLayout();

        if (string.IsNullOrWhiteSpace(findTextBox.Text) && sqlTextBox.SelectionLength > 0)
        {
            findTextBox.Text = sqlTextBox.SelectedText;
        }

        findTextBox.Focus();
        findTextBox.SelectAll();
    }

    /// <summary>
    /// 検索バーを閉じる。
    /// </summary>
    private void HideFindPanel()
    {
        findPanel.Visible = false;
        UpdateEditorOverlayLayout();
        sqlTextBox.Focus();
    }

    /// <summary>
    /// 検索文字列の次または前の一致位置へ移動する。
    /// 末尾や先頭まで到達したら折り返して探す。
    /// </summary>
    private void FindNextOccurrence(bool reverse)
    {
        if (string.IsNullOrWhiteSpace(findTextBox.Text))
        {
            return;
        }

        var searchText = findTextBox.Text;
        var startIndex = reverse
            ? Math.Max(sqlTextBox.SelectionStart - 1, 0)
            : Math.Min(sqlTextBox.SelectionStart + Math.Max(sqlTextBox.SelectionLength, 1), sqlTextBox.TextLength);

        var index = reverse
            ? sqlTextBox.Find(searchText, 0, startIndex + 1, RichTextBoxFinds.Reverse)
            : sqlTextBox.Find(searchText, startIndex, RichTextBoxFinds.None);

        if (index < 0)
        {
            index = reverse
                ? sqlTextBox.Find(searchText, 0, sqlTextBox.TextLength, RichTextBoxFinds.Reverse)
                : sqlTextBox.Find(searchText, 0, RichTextBoxFinds.None);
        }

        if (index < 0)
        {
            return;
        }

        sqlTextBox.Select(index, searchText.Length);
        sqlTextBox.ScrollToCaret();
        sqlTextBox.Focus();
    }

    /// <summary>
    /// SQL 全文からスパンに対応する部分文字列を取り出す。
    /// </summary>
    private bool TryExtractSourceText(TextSpan sourceSpan, out string sourceText)
    {
        if (!TryClampSpan(sourceSpan, out var clampedSpan))
        {
            sourceText = string.Empty;
            return false;
        }

        sourceText = sqlTextBox.Text.Substring(clampedSpan.Start, clampedSpan.Length);
        return true;
    }

    /// <summary>
    /// スパンが現在の入力文字列範囲内に収まるよう補正する。
    /// </summary>
    private bool TryClampSpan(TextSpan sourceSpan, out TextSpan clampedSpan)
    {
        if (sourceSpan.Start < 0 || sourceSpan.Start >= sqlTextBox.TextLength)
        {
            clampedSpan = new TextSpan(0, 0);
            return false;
        }

        var maxLength = sqlTextBox.TextLength - sourceSpan.Start;
        var length = Math.Min(sourceSpan.Length, maxLength);
        if (length <= 0)
        {
            clampedSpan = new TextSpan(0, 0);
            return false;
        }

        clampedSpan = new TextSpan(sourceSpan.Start, length);
        return true;
    }

    /// <summary>
    /// 整形済み SQL で入力欄全体を置き換える。
    /// 全文選択への差し替えにまとめ、Undo で一括復元しやすくする。
    /// </summary>
    private void ReplaceSqlEditorText(string newText)
    {
        var selectionStart = Math.Min(sqlTextBox.SelectionStart, sqlTextBox.TextLength);

        _suppressSqlSelectionSync = true;
        try
        {
            ClearSqlLinkedHighlightCore();
            ClearSqlParseIssueHighlightCore();
            sqlTextBox.SelectAll();
            sqlTextBox.SelectedText = newText;

            var restoredSelectionStart = Math.Min(selectionStart, sqlTextBox.TextLength);
            sqlTextBox.Select(restoredSelectionStart, 0);
        }
        finally
        {
            _suppressSqlSelectionSync = false;
        }
    }

    /// <summary>
    /// 構文エラー一覧に保持する表示用アイテム。
    /// ToString の整形を専用型へ閉じ込め、ListBox から生のモデルを分離する。
    /// </summary>
    private sealed record ParseIssueListItem(ParseIssue ParseIssue)
    {
        public override string ToString()
        {
            return $"行 {ParseIssue.Line} / 列 {ParseIssue.Column}: {ParseIssue.Message}";
        }
    }

    /// <summary>
    /// 補完候補一覧に保持する表示用アイテム。
    /// 種別ラベルも一緒に表示し、候補の意味を見分けやすくする。
    /// </summary>
    private sealed record CompletionListItem(SqlInputAssistItem Item)
    {
        public override string ToString()
        {
            var kindText = Item.Kind switch
            {
                SqlInputAssistItemKind.Keyword => "キーワード",
                SqlInputAssistItemKind.CommonTableExpression => "CTE",
                SqlInputAssistItemKind.SourceAlias => "別名",
                SqlInputAssistItemKind.SelectAlias => "SELECT別名",
                SqlInputAssistItemKind.Column => "列",
                _ => "候補"
            };

            return $"{Item.DisplayText}    [{kindText}]";
        }
    }
}
