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
    private readonly IQueryAnalysisService _analysisService;
    private readonly QueryAnalysisTreeBuilder _treeBuilder;

    private DisplayTreeNode? _currentTree;
    private bool _suppressSqlSelectionSync;
    private bool _suppressTreeSelectionSync;

    /// <summary>
    /// 必要なサービスを受け取って初期化する。
    /// </summary>
    public MainForm(IQueryAnalysisService analysisService, QueryAnalysisTreeBuilder treeBuilder)
    {
        _analysisService = analysisService;
        _treeBuilder = treeBuilder;

        InitializeComponent();
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
        sqlTextBox.Clear();
        resultTreeView.Nodes.Clear();
        detailTextBox.Clear();
        findTextBox.Clear();
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
    /// SQL 側の選択変更に合わせて、最も近い解析ノードを選択する。
    /// </summary>
    private void SqlTextBox_SelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressSqlSelectionSync)
        {
            return;
        }

        SyncTreeSelectionFromSql();
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
        if (_currentTree is null)
        {
            UpdateDetailTextForSelection();
            return;
        }

        var match = DisplayTreeNodeNavigator.FindBestMatch(
            _currentTree,
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
            return;
        }

        if (!TryClampSpan(sourceSpan, out var clampedSpan))
        {
            return;
        }

        _suppressSqlSelectionSync = true;
        try
        {
            sqlTextBox.Select(clampedSpan.Start, clampedSpan.Length);
            sqlTextBox.ScrollToCaret();
        }
        finally
        {
            _suppressSqlSelectionSync = false;
        }
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
