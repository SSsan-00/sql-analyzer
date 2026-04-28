using System.IO.Compression;
using System.Text;

namespace BootstrapProjectGenerator;

/// <summary>
/// 単一 csproj の bootstrap 配布物を生成する。
/// リポジトリ内の必要ファイルを zip 化して埋め込み、保存先へ生成物を書き出す。
/// </summary>
internal static class Program
{
    /// <summary>
    /// エントリーポイント。
    /// 引数未指定時はリポジトリ既定の manifest と出力先を使う。
    /// </summary>
    private static int Main(string[] args)
    {
        try
        {
            var options = ParseOptions(args);
            var repoRoot = ResolveRepositoryRoot(options.RepositoryRoot);
            var manifestPath = options.ManifestPath is not null
                ? Path.GetFullPath(options.ManifestPath)
                : Path.Combine(repoRoot, "bootstrap", "bundle-manifest.txt");
            var outputPath = options.OutputPath is not null
                ? Path.GetFullPath(options.OutputPath)
                : Path.Combine(repoRoot, "bootstrap", "TSqlAnalyzer.Bootstrap.csproj");

            if (options.RefreshManifest)
            {
                RefreshManifest(repoRoot, manifestPath);
            }

            var bundledFiles = LoadBundledFiles(repoRoot, manifestPath);
            var archivePayload = BuildArchivePayload(bundledFiles);
            var projectText = BuildBootstrapProjectText(archivePayload, bundledFiles.Count);

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            File.WriteAllText(outputPath, projectText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Console.WriteLine($"Generated: {outputPath}");
            Console.WriteLine($"Files: {bundledFiles.Count}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    /// <summary>
    /// コマンドライン引数を解釈する。
    /// </summary>
    private static GeneratorOptions ParseOptions(IReadOnlyList<string> args)
    {
        var nonOptionArgs = new List<string>();
        var refreshManifest = false;

        foreach (var arg in args)
        {
            if (string.Equals(arg, "--refresh-manifest", StringComparison.OrdinalIgnoreCase))
            {
                refreshManifest = true;
                continue;
            }

            nonOptionArgs.Add(arg);
        }

        if (nonOptionArgs.Count > 3)
        {
            throw new InvalidOperationException("引数が多すぎる。指定可能なのは [repoRoot] [manifestPath] [outputPath] と --refresh-manifest のみ。");
        }

        return new GeneratorOptions(
            RepositoryRoot: nonOptionArgs.ElementAtOrDefault(0),
            ManifestPath: nonOptionArgs.ElementAtOrDefault(1),
            OutputPath: nonOptionArgs.ElementAtOrDefault(2),
            RefreshManifest: refreshManifest);
    }

    /// <summary>
    /// リポジトリルートを決定する。
    /// repoRoot が指定されていればそれを使い、なければ実行ファイル位置から推定する。
    /// </summary>
    private static string ResolveRepositoryRoot(string? repoRoot)
    {
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            return Path.GetFullPath(repoRoot);
        }

        var candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        if (File.Exists(Path.Combine(candidate, "TSqlAnalyzer.slnx")))
        {
            return candidate;
        }

        var current = Directory.GetCurrentDirectory();
        if (File.Exists(Path.Combine(current, "TSqlAnalyzer.slnx")))
        {
            return current;
        }

        throw new InvalidOperationException("リポジトリルートを特定できない。第1引数にルートパスを渡すこと。");
    }

    /// <summary>
    /// 現在の実装から manifest を再生成する。
    /// bootstrap の配布対象は実行に必要なプロダクションコードのみ。
    /// </summary>
    private static void RefreshManifest(string repoRoot, string manifestPath)
    {
        var candidates = new[]
        {
            ".gitignore",
            "README.md",
            "TSqlAnalyzer.Runtime.slnx",
            "docs/CodeReadingGuide.md",
            "docs/WindowsBootstrapToExe.md"
        }.ToList();

        var srcRoot = Path.Combine(repoRoot, "src");
        if (!Directory.Exists(srcRoot))
        {
            throw new DirectoryNotFoundException($"src ディレクトリが見つからない: {srcRoot}");
        }

        candidates.AddRange(
            Directory.EnumerateFiles(srcRoot, "*", SearchOption.AllDirectories)
                .Select(path => NormalizeRelativePath(repoRoot, path)));

        var lines = new List<string>
        {
            "# 単一ファイル bootstrap に含める配布対象"
        };
        lines.AddRange(candidates
            .Select(path => path.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal));

        var manifestDirectory = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrEmpty(manifestDirectory))
        {
            Directory.CreateDirectory(manifestDirectory);
        }

        File.WriteAllLines(manifestPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"Refreshed manifest: {manifestPath}");
        Console.WriteLine($"Manifest files: {lines.Count - 1}");
    }

    private static string NormalizeRelativePath(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace('\\', '/');
    }

    /// <summary>
    /// manifest に列挙されたファイルを読み込む。
    /// 空行と # コメント行は無視する。
    /// </summary>
    private static List<BundledFile> LoadBundledFiles(string repoRoot, string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"manifest が見つからない: {manifestPath}");
        }

        var result = new List<BundledFile>();

        foreach (var rawLine in File.ReadAllLines(manifestPath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var normalizedPath = line.Replace('\\', '/');
            var absolutePath = Path.Combine(repoRoot, normalizedPath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException($"bundle 対象ファイルが見つからない: {normalizedPath}");
            }

            result.Add(new BundledFile(normalizedPath, File.ReadAllBytes(absolutePath)));
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException("bundle 対象ファイルが 0 件。manifest を見直すこと。");
        }

        return result;
    }

    /// <summary>
    /// bundle 対象ファイルを zip 化し、base64 文字列へ変換する。
    /// bootstrap 側はこの payload だけで自己展開できる。
    /// </summary>
    private static string BuildArchivePayload(IEnumerable<BundledFile> bundledFiles)
    {
        using var archiveStream = new MemoryStream();

        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var bundledFile in bundledFiles)
            {
                var entry = archive.CreateEntry(bundledFile.RelativePath, CompressionLevel.SmallestSize);
                using var entryStream = entry.Open();
                entryStream.Write(bundledFile.Content);
            }
        }

        return Convert.ToBase64String(archiveStream.ToArray());
    }

    /// <summary>
    /// 単一ファイル bootstrap 用の csproj テキストを組み立てる。
    /// build 時に展開し、必要なら展開先の実行用ソリューションを build する。
    /// </summary>
    private static string BuildBootstrapProjectText(string archivePayload, int bundledFileCount)
    {
        return $$"""
                 <!--
                   このファイルは tools/BootstrapProjectGenerator で生成した。
                   この 1 ファイルを任意ディレクトリへ保存し、dotnet build すると
                   展開先に TSqlAnalyzer のプロダクションコード一式を書き出せる。
                 -->
                 <Project Sdk="Microsoft.NET.Sdk">
                   <PropertyGroup>
                     <TargetFramework>net9.0</TargetFramework>
                     <OutputType>Library</OutputType>
                     <Nullable>enable</Nullable>
                     <ImplicitUsings>enable</ImplicitUsings>
                     <NoWarn>CS0162</NoWarn>
                     <EnableDefaultItems>false</EnableDefaultItems>
                     <ExtractRoot Condition="'$(ExtractRoot)' == ''">$(MSBuildProjectDirectory)/extracted/TSqlAnalyzer</ExtractRoot>
                     <RunExtractedBuild Condition="'$(RunExtractedBuild)' == ''">true</RunExtractedBuild>
                     <VerificationConfiguration Condition="'$(VerificationConfiguration)' == ''">Debug</VerificationConfiguration>
                     <VerificationSolutionPath Condition="'$(VerificationSolutionPath)' == ''">TSqlAnalyzer.Runtime.slnx</VerificationSolutionPath>
                     <DotNetCommand Condition="'$(DotNetCommand)' == ''">dotnet</DotNetCommand>
                     <BundledFileCount>{{bundledFileCount}}</BundledFileCount>
                     <ArchivePayload>{{archivePayload}}</ArchivePayload>
                   </PropertyGroup>

                   <UsingTask TaskName="ExtractBundledRepositoryTask"
                              TaskFactory="RoslynCodeTaskFactory"
                              AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll">
                     <ParameterGroup>
                       <RootDirectory ParameterType="System.String" Required="true" />
                       <ArchivePayload ParameterType="System.String" Required="true" />
                     </ParameterGroup>
                     <Task>
                       <Using Namespace="System" />
                       <Using Namespace="System.IO" />
                       <Using Namespace="System.IO.Compression" />
                       <Using Namespace="System.Text" />
                       <Code Type="Fragment" Language="cs"><![CDATA[
                         var archiveBytes = Convert.FromBase64String(ArchivePayload);
                         using var archiveStream = new MemoryStream(archiveBytes);
                         using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
                         var utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

                         foreach (var entry in archive.Entries)
                         {
                             if (string.IsNullOrEmpty(entry.Name))
                             {
                                 continue;
                             }

                             var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                             var destinationPath = Path.Combine(RootDirectory, relativePath);
                             var destinationDirectory = Path.GetDirectoryName(destinationPath);
                             if (!string.IsNullOrEmpty(destinationDirectory))
                             {
                                 Directory.CreateDirectory(destinationDirectory);
                             }

                             using var input = entry.Open();
                             using var memory = new MemoryStream();
                             input.CopyTo(memory);
                             File.WriteAllText(destinationPath, utf8WithoutBom.GetString(memory.ToArray()), utf8WithoutBom);
                         }

                         Log.LogMessage(MessageImportance.High, $"Extracted repository to {RootDirectory}");
                         return !Log.HasLoggedErrors;
                       ]]></Code>
                     </Task>
                   </UsingTask>

                   <Target Name="ExtractBundledRepository" BeforeTargets="Build">
                     <Message Importance="High" Text="Extracting $(BundledFileCount) bundled files to $(ExtractRoot)" />
                     <ExtractBundledRepositoryTask RootDirectory="$(ExtractRoot)" ArchivePayload="$(ArchivePayload)" />
                   </Target>

                   <Target Name="VerifyExtractedRepository" AfterTargets="Build">
                     <Message Importance="High" Text="Building extracted solution at $(ExtractRoot)/$(VerificationSolutionPath)" Condition="'$(RunExtractedBuild)' == 'true'" />
                     <Exec Command="&quot;$(DotNetCommand)&quot; build &quot;$(ExtractRoot)/$(VerificationSolutionPath)&quot; -c $(VerificationConfiguration) -m:1 -nr:false"
                           Condition="'$(RunExtractedBuild)' == 'true'" />
                   </Target>
                 </Project>
                 """;
    }

    /// <summary>
    /// bundle 対象ファイルを表す。
    /// relative path は zip entry 名としてそのまま使う。
    /// </summary>
    private sealed record BundledFile(string RelativePath, byte[] Content);

    private sealed record GeneratorOptions(
        string? RepositoryRoot,
        string? ManifestPath,
        string? OutputPath,
        bool RefreshManifest);
}
