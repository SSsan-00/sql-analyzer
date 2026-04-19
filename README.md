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
  - UPDATE
  - INSERT
  - DELETE
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
- CTE
- サブクエリ
- EXISTS / NOT EXISTS
- IN / NOT IN
- UNION / UNION ALL / EXCEPT / INTERSECT
- JOIN 表示を `種別 / 結合先 / ON条件` の形式で統一できる
- 派生テーブルや JOIN 先サブクエリの内部構造を TreeView で追える
- 集合演算ノードで `左概要 / 右概要 / 子集合演算数` を表示し、左右差を先に把握できる
- SELECT 項目で `種別 / 式 / 別名 / 集計関数` を分解表示できる
- `SELECT *` と `table.*` を区別し、`全列種別 / 修飾子` を表示できる
- UPDATE 文で `更新対象 / 更新内容 / 参照ソース / 結合 / 抽出条件 / 出力` を表示できる
- INSERT 文で `挿入対象 / 挿入列 / 入力元 / 出力` を表示できる
- DELETE 文で `削除対象 / 参照ソース / 結合 / 抽出条件 / 出力` を表示できる
- INSERT 入力元では `VALUES / SELECT / EXECUTE` を区別できる
- WHERE / HAVING 条件を `AND / OR / NOT` の論理木として表示できる
- WHERE / HAVING 条件で明示的に括弧で囲まれた条件グループを表示できる
- WHERE / HAVING 条件内の `比較 / NULL判定 / LIKE / BETWEEN / EXISTS / IN` を述語種別として表示できる
- 比較述語では `等価 (=) / 以上 (>=) / 不等価 (<>)` などの比較種別も表示できる
- NULL 判定では `IS NULL / IS NOT NULL`、BETWEEN 系では `BETWEEN / NOT BETWEEN` まで表示できる
- LIKE 系では `LIKE / NOT LIKE` まで表示できる
- CTE の参照関係を `メインクエリ / 各CTE -> 参照先CTE` の形で表示できる
- CTE の依存順と再帰的な参照を表示できる
- TreeView 表示用の UI 非依存モデルへ変換できる

## この初期版でまだ未対応のこと

- MERGE の詳細解析
- UPDATE / INSERT / DELETE の `OUTPUT` 句詳細分解
- INSERT ... EXECUTE の実行結果解析
- UPDATE / DELETE 対象の別名や特殊ターゲット構文の詳細分類
- PIVOT / UNPIVOT
- APPLY の正式対応
  - 現状は補足的な注意表示に留める
- 動的 SQL
- DECLARE
- IF / ELSE
- TRY / CATCH
- 一時テーブル操作の完全解析
- ストアドプロシージャ全体の制御フロー解析
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
TSqlAnalyzer.Runtime.slnx
```

- `TSqlAnalyzer.Domain`
  - 解析結果を表す独自モデルを保持する
- `TSqlAnalyzer.Application`
  - ScriptDom を用いた解析処理と TreeView 用表示モデル変換を担当する
- `TSqlAnalyzer.WinForms`
  - 画面表示、入力受付、TreeView 反映を担当する
- `TSqlAnalyzer.Tests`
  - 解析ロジックと表示モデル変換ロジックを xUnit で検証する
- `TSqlAnalyzer.Runtime.slnx`
  - bootstrap 展開先で使うプロダクションコード専用ソリューション

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

リポジトリを clone できない相手向けに、単一の `csproj` だけでプロダクションコード一式を展開できる bootstrap 配布物を用意している。

- 配布ファイル: `bootstrap/TSqlAnalyzer.Bootstrap.csproj`
- 想定用途: GitHub Web UI や社内ドキュメントから 1 ファイルだけをコピーし、ローカルで展開・build・publish を行う
- 詳細手順: `docs/WindowsBootstrapToExe.md`

重要:

- bootstrap 展開物には `tests/` を含めない
- 単一ファイル配布先では xUnit を使わず、`TSqlAnalyzer.Runtime.slnx` と WinForms プロジェクトだけで build / publish できるようにしている
- xUnit を使った TDD は、リポジトリを clone できる開発端末で `TSqlAnalyzer.slnx` に対して行う

### 最短手順

1. 空の作業ディレクトリを作る
2. `bootstrap/TSqlAnalyzer.Bootstrap.csproj` の内容を `TSqlAnalyzer.Bootstrap.csproj` として保存する
3. そのディレクトリで bootstrap を実行する

```powershell
dotnet build TSqlAnalyzer.Bootstrap.csproj
```

4. 展開されたソースへ移動する

```powershell
cd .\extracted\TSqlAnalyzer
```

5. 展開されたプロダクションコードをビルドする

```powershell
dotnet build .\TSqlAnalyzer.Runtime.slnx -c Release
```

6. 配布用 `exe` を作る

```powershell
dotnet publish .\src\TSqlAnalyzer.WinForms\TSqlAnalyzer.WinForms.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

7. 生成された `exe` を起動する

```powershell
.\src\TSqlAnalyzer.WinForms\bin\Release\net10.0-windows\win-x64\publish\TSqlAnalyzer.WinForms.exe
```

### bootstrap の既定動作

- `./extracted/TSqlAnalyzer/` にプロダクションコード一式を展開する
- 展開後の `TSqlAnalyzer.Runtime.slnx` に対して `dotnet build` を実行する
- テストプロジェクトは含めない

### よく使う切り替え

展開先を変える:

```powershell
dotnet build TSqlAnalyzer.Bootstrap.csproj -p:ExtractRoot=C:\work\TSqlAnalyzer
```

展開だけ行い、展開先の build は後で手動実行する:

```powershell
dotnet build TSqlAnalyzer.Bootstrap.csproj -p:RunExtractedBuild=false
```

### bootstrap 実行後の確認

展開後に WinForms 画面を起動する場合は、展開先ディレクトリで次を実行する。

```powershell
dotnet run --project src/TSqlAnalyzer.WinForms/TSqlAnalyzer.WinForms.csproj
```

## Windows での動作確認手順

clone できる場合の通常手順はこの節を使う。clone できない場合は `docs/WindowsBootstrapToExe.md` を参照する。
開発端末では `TSqlAnalyzer.slnx` を使って test まで行い、配布先では `TSqlAnalyzer.Runtime.slnx` と `WinForms` プロジェクトだけで実行確認する。

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
- `取得項目` 配下に `別名` と `集計関数` が出る
- `取得項目` 配下にワイルドカード項目の `全列種別` と `修飾子` が出る
- `集合演算` 配下に `概要` `左概要` `右概要` が出る
- `集合演算` 配下に `子集合演算数` が出る
- `結合` 配下に `JOIN #1` が出る
- `抽出条件` 配下に `条件論理` が出る
- `条件論理` 配下に括弧グループがある場合 `括弧: あり` が出る
- `条件論理` 配下に `述語種別: EXISTS` などの種別表示が出る
- `条件論理` 配下に `比較種別: 等価 (=)` などの比較種別表示が出る
- `条件論理` 配下に `NULL判定種別: IS NOT NULL` や `範囲種別: NOT BETWEEN` が出る
- `条件論理` 配下に `LIKE種別: NOT LIKE` が出る
- `共通テーブル式` 配下に `参照関係` が出る
- `共通テーブル式` 配下に `依存順` が出る
- `サブクエリ` 配下に WHERE 句由来のサブクエリが出る

## ビルド成果物作成手順

clone 済みリポジトリから `exe` を作る手順。単一ファイル bootstrap から始める場合も、展開後は同じ手順になる。

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

### プロダクションコードの build / publish に必須

- `TSqlAnalyzer.Runtime.slnx`
- `src/TSqlAnalyzer.Domain/TSqlAnalyzer.Domain.csproj`
- `src/TSqlAnalyzer.Domain/Analysis/QueryAnalysisModels.cs`
- `src/TSqlAnalyzer.Application/TSqlAnalyzer.Application.csproj`
- `src/TSqlAnalyzer.Application/Analysis/ISqlQueryAnalyzer.cs`
- `src/TSqlAnalyzer.Application/Analysis/CommonTableExpressionDependencyAnalyzer.cs`
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

### 開発端末での test に追加で必要

- `TSqlAnalyzer.slnx`
- `tests/TSqlAnalyzer.Tests/TSqlAnalyzer.Tests.csproj`
- `tests/TSqlAnalyzer.Tests/Analysis/QueryAnalysisServiceTests.cs`
- `tests/TSqlAnalyzer.Tests/Presentation/QueryAnalysisTreeBuilderTests.cs`

## テスト方針

初期版では GUI の見た目テストよりも、ロジック側の安定化を優先する。

- 単純な SELECT の解析
- JOIN を含む SELECT の解析
- WHERE を含む SELECT の解析
- UPDATE / INSERT / DELETE の解析
- EXISTS の検出
- IN / NOT IN の検出
- UNION / UNION ALL / EXCEPT / INTERSECT の識別
- JOIN 表示用モデルの構築
- TreeView 表示モデル変換
- 未入力時と未対応文種別の扱い

## 今後の拡張方針

- CTE 定義ノードの表示
- サブクエリの出現位置をより細かく分類
- 条件式論理木の詳細化
- CTE 依存関係の段階表示強化
- 詳細ペイン追加
- 解析結果のエクスポート
- Windows 向け単一 `exe` 配布フロー整備
  - 例: `dotnet publish` による自己完結配布

## 配布について

このリポジトリは開発用ソース一式。最終的な利用者配布形態は、環境構築不要の Windows 向け `exe` を想定する。
