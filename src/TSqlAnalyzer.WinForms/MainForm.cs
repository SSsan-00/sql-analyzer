using TSqlAnalyzer.Application.Presentation;
using TSqlAnalyzer.Application.Services;
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

    private readonly IQueryAnalysisService _analysisService;
    private readonly QueryAnalysisTreeBuilder _treeBuilder;

    private DisplayTreeNode? _currentTree;
    private DisplayTreeNode? _displayedTree;
    private TextSpan? _highlightedSqlSpan;
    private string _treeSearchText = string.Empty;
    private bool _isTreeFilterActive;
    private bool _suppressSqlSelectionSync;
    private bool _suppressTreeSelectionSync;
    private bool _suppressTreeSearchTextChanged;

    /// <summary>
    /// 必要なサービスを受け取って初期化する。
    /// </summary>
    public MainForm(IQueryAnalysisService analysisService, QueryAnalysisTreeBuilder treeBuilder)
    {
        _analysisService = analysisService;
        _treeBuilder = treeBuilder;

        InitializeComponent();
        ConfigureResultTreeViewVisuals();
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
    /// 解析ボタン押下時の処理。
    /// 入力 SQL を解析し、TreeView 用モデルへ変換して画面へ反映する。
    /// </summary>
    private void AnalyzeButton_Click(object? sender, EventArgs e)
    {
        var analysis = _analysisService.Analyze(sqlTextBox.Text);
        var tree = _treeBuilder.Build(analysis);

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
        AnalysisTreeViewBinder.Bind(resultTreeView, tree);
        UpdateDetailText(resultTreeView.SelectedNode);
        SyncTreeSelectionFromSql();
    }

    /// <summary>
    /// 入力欄と表示結果をまとめて初期化する。
    /// </summary>
    private void ClearButton_Click(object? sender, EventArgs e)
    {
        _currentTree = null;
        _displayedTree = null;
        _isTreeFilterActive = false;
        _treeSearchText = string.Empty;
        _highlightedSqlSpan = null;
        sqlTextBox.Clear();
        resultTreeView.Nodes.Clear();
        detailTextBox.Clear();
        findTextBox.Clear();
        resultSearchTextBox.Clear();
        findPanel.Visible = false;
        sqlTextBox.Focus();
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
    /// SQL 入力欄で Ctrl+F が押されたら検索バーを開く。
    /// </summary>
    private void SqlTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.F)
        {
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
        if (_highlightedSqlSpan is null)
        {
            return;
        }

        var originalSelectionStart = sqlTextBox.SelectionStart;
        var originalSelectionLength = sqlTextBox.SelectionLength;

        if (TryClampSpan(_highlightedSqlSpan, out var clampedSpan))
        {
            sqlTextBox.Select(clampedSpan.Start, clampedSpan.Length);
            sqlTextBox.SelectionBackColor = sqlTextBox.BackColor;
        }

        _highlightedSqlSpan = null;

        if (originalSelectionStart >= 0 && originalSelectionStart <= sqlTextBox.TextLength)
        {
            var safeLength = Math.Min(originalSelectionLength, sqlTextBox.TextLength - originalSelectionStart);
            sqlTextBox.Select(originalSelectionStart, Math.Max(0, safeLength));
        }
    }

    /// <summary>
    /// TreeView 選択に対応する SQL 範囲へ黄色の背景色を付ける。
    /// 選択色だけに頼らず、TreeView から戻ったときも対応箇所を見つけやすくする。
    /// </summary>
    private void ApplySqlLinkedHighlight(TextSpan sourceSpan)
    {
        sqlTextBox.Select(sourceSpan.Start, sourceSpan.Length);
        sqlTextBox.SelectionBackColor = SqlLinkedHighlightBackColor;
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
}
