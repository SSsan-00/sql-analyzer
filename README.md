# T-SQL解析GUIツール

巨大で複雑な T-SQL クエリを、構文上の事実にもとづいて分解し、読みやすい TreeView で追えるようにする WinForms ツールの初期実装。

このリポジトリは、最終的に利用者へ `exe` 配布することを見据えつつ、開発段階では `dotnet build` / `dotnet test` を回しながら育てやすい土台を用意することを目的とする。

## この初期版でできること

- WinForms 画面を起動し、T-SQL を貼り付けて解析できる
- 「解析」ボタンで解析サービスを呼び出し、結果を TreeView に表示できる
- 「クリア」ボタンで入力欄と結果表示を初期化できる
- `Microsoft.SqlServer.TransactSql.ScriptDom` を使って T-SQL を構文解析できる
- 次の構造を独自モデルとして保持できる
  - SELECT
  - FROM
  - JOIN
    - INNER JOIN
    - LEFT JOIN
    - RIGHT JOIN
    - FULL JOIN
    - CROSS JOIN
  - WHERE
  - GROUP BY
  - HAVING
  - ORDER BY
  - TOP
  - DISTINCT
  - サブクエリ
  - EXISTS / NOT EXISTS
  - IN / NOT IN
  - UNION / UNION ALL / EXCEPT / INTERSECT
- JOIN 表示を `種別 / 結合先 / ON条件` の形式で統一できる
- TreeView 表示用の UI 非依存モデルへ変換できる

## この初期版でまだ未対応のこと

- MERGE の詳細解析
- PIVOT / UNPIVOT
- APPLY の正式対応
  - 現状は補足的な注意表示に留める
- 動的 SQL
- DECLARE
- IF / ELSE
- TRY / CATCH
- 一時テーブル操作の完全解析
- ストアドプロシージャ全体の制御フロー解析
- CTE 定義一覧の詳細表示
- 詳細ペインやプロパティ表示などの補助 UI

## 設計方針

- UI ロジックと解析ロジックを分離する
- ScriptDom の生オブジェクトを Form に渡さず、独自の解析モデルへ変換する
- TreeView 用の表示木も WinForms 依存型ではなく、中立的なノードモデルへ一度変換する
- JOIN 先は「テーブル」ではなく「ソース」として扱い、将来の派生テーブルや集合演算結果に広げやすくする
- 集合演算は左右のクエリを再帰的に保持する形にする
- 注意点や警告を解析結果に含め、未対応構文や複数文入力にも対応しやすくする

## プロジェクト構成

```text
src/
  TSqlAnalyzer.Domain
  TSqlAnalyzer.Application
  TSqlAnalyzer.WinForms
tests/
  TSqlAnalyzer.Tests
TSqlAnalyzer.slnx
```

- `TSqlAnalyzer.Domain`
  - 解析結果を表す独自モデルを保持する
- `TSqlAnalyzer.Application`
  - ScriptDom を用いた解析処理と TreeView 用表示モデル変換を担当する
- `TSqlAnalyzer.WinForms`
  - 画面表示、入力受付、TreeView 反映を担当する
- `TSqlAnalyzer.Tests`
  - 解析ロジックと表示モデル変換ロジックを xUnit で検証する

## 開発方法

### 前提

- .NET SDK 10 以降
- NuGet へアクセスできる開発環境
- 実行対象は最終的に Windows を想定

### 初回セットアップ

```bash
dotnet restore TSqlAnalyzer.slnx
```

### ビルド

```bash
dotnet build TSqlAnalyzer.slnx
```

### テスト

```bash
dotnet test TSqlAnalyzer.slnx
```

### WinForms アプリ起動

Windows 環境で次を実行する。

```bash
dotnet run --project src/TSqlAnalyzer.WinForms/TSqlAnalyzer.WinForms.csproj
```

## 単一ファイル bootstrap 配布

リポジトリを clone できない相手向けに、単一の `csproj` だけでソース一式を展開できる bootstrap 配布物を用意している。

- 配布ファイル: `bootstrap/TSqlAnalyzer.Bootstrap.csproj`
- 想定用途: GitHub Web UI や社内ドキュメントから 1 ファイルだけをコピーし、ローカルで展開・build/test を行う

### 利用手順

1. 空の作業ディレクトリを作る。
2. `bootstrap/TSqlAnalyzer.Bootstrap.csproj` の内容を `TSqlAnalyzer.Bootstrap.csproj` という名前で保存する。
3. そのディレクトリで次を実行する。

```powershell
dotnet build TSqlAnalyzer.Bootstrap.csproj
```

### 既定動作

- `./extracted/TSqlAnalyzer/` にソース一式を展開する
- 展開後の `TSqlAnalyzer.slnx` に対して `dotnet build` を実行する
- 続けて `dotnet test` を実行する

### よく使う切り替え

展開先を変える:

```powershell
dotnet build TSqlAnalyzer.Bootstrap.csproj -p:ExtractRoot=C:\work\TSqlAnalyzer
```

展開だけ行い、展開先の build/test は後で手動実行する:

```powershell
dotnet build TSqlAnalyzer.Bootstrap.csproj -p:RunExtractedBuild=false -p:RunExtractedTest=false
```

### 展開後の確認

展開後に WinForms 画面を起動する場合は、展開先ディレクトリで次を実行する。

```powershell
dotnet run --project extracted/TSqlAnalyzer/src/TSqlAnalyzer.WinForms/TSqlAnalyzer.WinForms.csproj
```

## Windows での動作確認手順

### 前提

- Windows 11 または Windows 10
- .NET SDK 10 系
- PowerShell

### 手順

1. リポジトリを取得する。
2. PowerShell を開き、リポジトリのルートへ移動する。
3. 依存関係を復元する。

```powershell
dotnet restore TSqlAnalyzer.slnx
```

4. ソリューションをビルドする。

```powershell
dotnet build TSqlAnalyzer.slnx -c Debug
```

5. テストを実行する。

```powershell
dotnet test TSqlAnalyzer.slnx -c Debug
```

6. WinForms アプリを起動する。

```powershell
dotnet run --project src/TSqlAnalyzer.WinForms/TSqlAnalyzer.WinForms.csproj -c Debug
```

7. 起動した画面で次の確認を行う。

- フォームが表示される
- 入力欄に SQL を貼り付けられる
- `解析` ボタン押下で TreeView に構造が表示される
- `クリア` ボタン押下で入力欄と TreeView が空になる

### 動作確認用 SQL 例

```sql
SELECT
    u.Id,
    o.OrderNo
FROM dbo.Users u
LEFT JOIN dbo.Orders o
    ON u.Id = o.UserId
WHERE EXISTS (
    SELECT 1
    FROM dbo.Payments p
    WHERE p.OrderId = o.Id
)
ORDER BY u.Id;
```

### 期待結果

- `クエリ解析結果` がルート表示される
- `主構造` 配下に `取得項目` `主テーブル` `結合` `抽出条件` `並び順` が出る
- `結合` 配下に `JOIN #1` が出る
- `サブクエリ` 配下に WHERE 句由来のサブクエリが出る

## ビルド成果物作成手順

### 開発用ビルド

```powershell
dotnet build TSqlAnalyzer.slnx -c Release
```

主な出力先:

- `src/TSqlAnalyzer.WinForms/bin/Release/net10.0-windows/`
- `src/TSqlAnalyzer.Application/bin/Release/net10.0/`
- `src/TSqlAnalyzer.Domain/bin/Release/net10.0/`

### 配布用 publish

Windows 向け自己完結・単一ファイルの `exe` を作る場合は次を使う。

```powershell
dotnet publish src/TSqlAnalyzer.WinForms/TSqlAnalyzer.WinForms.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

出力先:

- `src/TSqlAnalyzer.WinForms/bin/Release/net10.0-windows/win-x64/publish/`

生成物の例:

- `TSqlAnalyzer.WinForms.exe`
- `TSqlAnalyzer.WinForms.pdb`
- 必要に応じて runtime 設定ファイル

### 配布前の確認

1. `dotnet test TSqlAnalyzer.slnx -c Release`
2. publish 出力先の `TSqlAnalyzer.WinForms.exe` を別ディレクトリへコピーする
3. その `exe` を起動し、前述の動作確認用 SQL で画面挙動を確認する

## bootstrap 再生成手順

開発側で配布用 bootstrap を更新する場合は、次を実行する。

```powershell
dotnet run --project tools/BootstrapProjectGenerator/BootstrapProjectGenerator.csproj
```

関連ファイル:

- `bootstrap/bundle-manifest.txt`
  - bootstrap に含めるファイル一覧
- `bootstrap/TSqlAnalyzer.Bootstrap.csproj`
  - 配布用の生成結果
- `tools/BootstrapProjectGenerator/`
  - 生成ツール本体

## ビルド成果物作成に必要なソース一覧

### 必須

- `TSqlAnalyzer.slnx`
- `src/TSqlAnalyzer.Domain/TSqlAnalyzer.Domain.csproj`
- `src/TSqlAnalyzer.Domain/Analysis/QueryAnalysisModels.cs`
- `src/TSqlAnalyzer.Application/TSqlAnalyzer.Application.csproj`
- `src/TSqlAnalyzer.Application/Analysis/ISqlQueryAnalyzer.cs`
- `src/TSqlAnalyzer.Application/Analysis/ScriptDomQueryAnalyzer.cs`
- `src/TSqlAnalyzer.Application/Presentation/DisplayTreeNode.cs`
- `src/TSqlAnalyzer.Application/Presentation/QueryAnalysisTreeBuilder.cs`
- `src/TSqlAnalyzer.Application/Services/IQueryAnalysisService.cs`
- `src/TSqlAnalyzer.Application/Services/QueryAnalysisService.cs`
- `src/TSqlAnalyzer.WinForms/TSqlAnalyzer.WinForms.csproj`
- `src/TSqlAnalyzer.WinForms/Program.cs`
- `src/TSqlAnalyzer.WinForms/MainForm.cs`
- `src/TSqlAnalyzer.WinForms/MainForm.Designer.cs`
- `src/TSqlAnalyzer.WinForms/UI/AnalysisTreeViewBinder.cs`

### 検証用

- `tests/TSqlAnalyzer.Tests/TSqlAnalyzer.Tests.csproj`
- `tests/TSqlAnalyzer.Tests/Analysis/QueryAnalysisServiceTests.cs`
- `tests/TSqlAnalyzer.Tests/Presentation/QueryAnalysisTreeBuilderTests.cs`

## テスト方針

初期版では GUI の見た目テストよりも、ロジック側の安定化を優先する。

- 単純な SELECT の解析
- JOIN を含む SELECT の解析
- WHERE を含む SELECT の解析
- EXISTS の検出
- IN / NOT IN の検出
- UNION / UNION ALL / EXCEPT / INTERSECT の識別
- JOIN 表示用モデルの構築
- TreeView 表示モデル変換
- 未入力時と未対応文種別の扱い

## 今後の拡張方針

- CTE 定義ノードの表示
- サブクエリの出現位置をより細かく分類
- 条件式の論理木表示
- 詳細ペイン追加
- 解析結果のエクスポート
- Windows 向け単一 `exe` 配布フロー整備
  - 例: `dotnet publish` による自己完結配布

## 配布について

このリポジトリは開発用ソース一式。最終的な利用者配布形態は、環境構築不要の Windows 向け `exe` を想定する。
