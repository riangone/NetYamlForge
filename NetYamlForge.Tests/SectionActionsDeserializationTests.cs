// ファイル概要: セクションアクション（ボタン）のデシリアライズテスト
using NetYamlForge.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Xunit;

namespace NetYamlForge.Tests;

public class SectionActionsDeserializationTests
{
    private static PageDefinition DeserializeYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new SectionColumnsConverter())
            .WithTypeConverter(new SectionHooksConverter())
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<PageDefinition>(yaml);
    }

    [Fact]
    public void SectionActions_ShouldDeserializeCorrectly()
    {
        var yaml = @"
title: テストページ
sections:
  - id: test_cards
    component: card_list
    sourceType: custom
    source: SELECT id, name FROM test
    columns: [id, name]
    actions:
      - label: 詳細
        url: ""/detail/{id}""
        class: btn-outline-primary
      - label: 試乗予約
        url: ""/appointments?vehicle_id={id}""
        class: btn-outline-secondary
";

        var page = DeserializeYaml(yaml);
        
        Assert.Single(page.Sections);
        var section = page.Sections[0];
        
        Assert.NotNull(section.Actions);
        Assert.Equal(2, section.Actions.Count);
        
        var detailAction = section.Actions[0];
        Assert.Equal("詳細", detailAction.Label);
        Assert.Equal("/detail/{id}", detailAction.Url);
        Assert.Equal("btn-outline-primary", detailAction.Class);
        
        var appointmentAction = section.Actions[1];
        Assert.Equal("試乗予約", appointmentAction.Label);
        Assert.Equal("/appointments?vehicle_id={id}", appointmentAction.Url);
        Assert.Equal("btn-outline-secondary", appointmentAction.Class);
    }

    [Fact]
    public void SectionActions_ShouldBeNull_WhenNotSpecified()
    {
        var yaml = @"
title: テストページ
sections:
  - id: test_cards
    component: card_list
    sourceType: custom
    source: SELECT id, name FROM test
    columns: [id, name]
";

        var page = DeserializeYaml(yaml);
        
        Assert.Single(page.Sections);
        var section = page.Sections[0];
        
        Assert.Null(section.Actions);
    }
}
