using TSqlAnalyzer.Application.Analysis;
using TSqlAnalyzer.Application.Editor;
using TSqlAnalyzer.Application.Export;
using TSqlAnalyzer.Application.Formatting;
using TSqlAnalyzer.Application.Presentation;
using TSqlAnalyzer.Application.Services;

namespace TSqlAnalyzer.WinForms;

/// <summary>
/// アプリケーションの起動点。
/// ここで依存関係を手動で組み立て、MainForm へ渡す。
/// </summary>
internal static class Program
{
    /// <summary>
    /// WinForms アプリケーションを起動する。
    /// </summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var analysisService = new QueryAnalysisService(new ScriptDomQueryAnalyzer());
        var treeBuilder = new QueryAnalysisTreeBuilder();
        var columnTextExportBuilder = new ColumnTextExportBuilder();
        var sqlFormattingService = new SqlFormattingService();
        var parseIssueTextSpanResolver = new ParseIssueTextSpanResolver();
        var sqlInputAssistService = new SqlInputAssistService();

        System.Windows.Forms.Application.Run(new MainForm(
            analysisService,
            treeBuilder,
            columnTextExportBuilder,
            sqlFormattingService,
            parseIssueTextSpanResolver,
            sqlInputAssistService));
    }
}
