// dungeon-forge の探索 UI ページを配信する MVC コントローラー。
// views/Dungeon/Explore.cshtml は以前どのルートからも到達できない孤児ファイルだったため、
// FormForgeController（{project}/FormForge → ~/projects/{project}/views/*.cshtml）と同じ
// パターンでここにルートを張る。認証は Program.cs の FallbackPolicy（Cookie 必須）に委ねる。
//
// ゲームロジックは一切ここに置かない。ページは JS から
// /api/{project}/dungeon/{state,move,attack,use-item,pick-up} を叩くだけで、
// 実装は Services/DungeonCombatService.cs 側にある。

using Microsoft.AspNetCore.Mvc;
using NetYamlForge.Controllers;
using NetYamlForge.Services;

namespace NetYamlForge.Projects.DungeonForge.Controllers;

[Route("{project}/Dungeon")]
public class DungeonExploreController(ProjectScope scope) : BaseProjectController
{
    private string ProjectName => scope.IsSet ? scope.Current.Name : "dungeon-forge";

    // GET /{project}/Dungeon/Explore
    [HttpGet("Explore")]
    public IActionResult Explore()
    {
        return View($"~/projects/{ProjectName}/views/Dungeon/Explore.cshtml");
    }
}
