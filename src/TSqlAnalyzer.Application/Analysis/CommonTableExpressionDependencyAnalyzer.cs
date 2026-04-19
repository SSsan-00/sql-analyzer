using TSqlAnalyzer.Domain.Analysis;

namespace TSqlAnalyzer.Application.Analysis;

/// <summary>
/// CTE の参照関係を集計する補助。
/// 依存順表示や循環検出を、解析側と表示側で同じ判定にそろえる。
/// </summary>
internal static class CommonTableExpressionDependencyAnalyzer
{
    /// <summary>
    /// 指定クエリ以下で参照されている CTE 名を重複なく返す。
    /// knownNames がある場合は、その集合に含まれる名前だけへ絞る。
    /// </summary>
    public static IReadOnlyList<string> GetReferencedNames(
        QueryExpressionAnalysis? query,
        ISet<string>? knownNames = null)
    {
        if (query is null)
        {
            return [];
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectReferencedNames(query, names);

        if (knownNames is not null)
        {
            names.RemoveWhere(name => !knownNames.Contains(name));
        }

        return names
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// CTE 一覧から依存順と循環対象を計算する。
    /// 依存順は Kahn 法で求め、取り切れなかった名前を循環として返す。
    /// </summary>
    public static CommonTableExpressionDependencyReport Analyze(IReadOnlyList<CommonTableExpressionAnalysis> commonTableExpressions)
    {
        if (commonTableExpressions.Count == 0)
        {
            return new CommonTableExpressionDependencyReport([], []);
        }

        var commonTableExpressionNames = commonTableExpressions
            .Select(commonTableExpression => commonTableExpression.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dependenciesByName = commonTableExpressions.ToDictionary(
            commonTableExpression => commonTableExpression.Name,
            commonTableExpression => GetReferencedNames(commonTableExpression.Query, commonTableExpressionNames)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var indegreeByName = dependenciesByName.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Count,
            StringComparer.OrdinalIgnoreCase);
        var dependentsByName = commonTableExpressionNames.ToDictionary(
            name => name,
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (name, dependencies) in dependenciesByName)
        {
            foreach (var dependency in dependencies)
            {
                dependentsByName[dependency].Add(name);
            }
        }

        var queue = new Queue<string>(indegreeByName
            .Where(pair => pair.Value == 0)
            .Select(pair => pair.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        var dependencyOrder = new List<string>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            dependencyOrder.Add(current);

            foreach (var dependent in dependentsByName[current].OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                indegreeByName[dependent]--;
                if (indegreeByName[dependent] == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        var cyclicNames = indegreeByName
            .Where(pair => pair.Value > 0)
            .Select(pair => pair.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CommonTableExpressionDependencyReport(dependencyOrder, cyclicNames);
    }

    /// <summary>
    /// クエリ以下から参照されている CTE 名を蓄積する。
    /// </summary>
    private static void CollectReferencedNames(QueryExpressionAnalysis query, ISet<string> names)
    {
        switch (query)
        {
            case SelectQueryAnalysis selectQuery:
                AddSourceReference(selectQuery.MainSource, names);

                foreach (var join in selectQuery.Joins)
                {
                    AddSourceReference(join.TargetSource, names);
                }

                foreach (var subquery in selectQuery.Subqueries)
                {
                    CollectReferencedNames(subquery.Query, names);
                }

                break;

            case SetOperationQueryAnalysis setOperationQuery:
                CollectReferencedNames(setOperationQuery.LeftQuery, names);
                CollectReferencedNames(setOperationQuery.RightQuery, names);
                break;
        }
    }

    /// <summary>
    /// ソースから CTE 参照名を拾う。
    /// </summary>
    private static void AddSourceReference(SourceAnalysis? source, ISet<string> names)
    {
        if (source is null)
        {
            return;
        }

        if (source.SourceKind == SourceKind.CommonTableExpressionReference && !string.IsNullOrWhiteSpace(source.SourceName))
        {
            names.Add(source.SourceName);
        }

        if (source.NestedQuery is not null)
        {
            CollectReferencedNames(source.NestedQuery, names);
        }
    }
}

/// <summary>
/// CTE 依存関係の集計結果。
/// 依存順と循環候補を分けて持つことで、TreeView にそのまま出しやすくする。
/// </summary>
internal sealed record CommonTableExpressionDependencyReport(
    IReadOnlyList<string> DependencyOrder,
    IReadOnlyList<string> CyclicNames);
