using Microsoft.AspNetCore.Mvc;

namespace NetYamlForge.Views.Shared.Components.AIQueryChat;

/// <summary>
/// AI 自然语言查询聊天组件
/// </summary>
public class AIQueryChatViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(string? project = null)
    {
        if (!string.IsNullOrEmpty(project))
        {
            ViewData["Project"] = project;
        }
        return View();
    }
}
