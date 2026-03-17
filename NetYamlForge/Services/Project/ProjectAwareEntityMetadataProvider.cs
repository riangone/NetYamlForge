// ファイル概要: ProjectScope の現在プロジェクトの IEntityMetadataProvider に委譲するプロキシです。
// Scoped として登録し、リクエストごとに正しいプロジェクトのメタデータを提供します。

using System.Diagnostics.CodeAnalysis;
using NetYamlForge.Models;

namespace NetYamlForge.Services;

public class ProjectAwareEntityMetadataProvider : IEntityMetadataProvider
{
    private readonly IEntityMetadataProvider _inner;

    public ProjectAwareEntityMetadataProvider(ProjectScope scope)
    {
        _inner = scope.Current.EntityMetadata;
    }

    public EntityDefinition Get(string entityName) => _inner.Get(entityName);

    public IReadOnlyDictionary<string, EntityDefinition> GetAll() => _inner.GetAll();

    public bool TryGet(string entityName, [NotNullWhen(true)] out EntityDefinition? definition) =>
        _inner.TryGet(entityName, out definition);
}
