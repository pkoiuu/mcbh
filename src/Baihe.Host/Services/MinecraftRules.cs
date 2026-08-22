// Minecraft 版本 JSON 规则检查 — 统一 LaunchService 与 DownloadService 的 rules 过滤逻辑
// 语义（Windows 平台）:
//  - 无 rules → 默认允许
//  - 有 os 字段的 rule: 仅当 os.name == "windows" 时应用 action（allow→允许, deny→拒绝）
//  - 无 os 字段的 rule: 直接应用 action
//  - 依次应用所有 rule，后应用的覆盖前面的结果

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Baihe.Host.Services;

/// <summary>
/// Minecraft 版本 JSON rules 检查（统一实现）
/// </summary>
public static class MinecraftRules
{
    /// <summary>检查 rules 是否匹配当前平台（Windows）— JsonNode 版本（LaunchService 使用）</summary>
    public static bool Check(JsonNode? rulesNode)
    {
        if (rulesNode is not JsonArray rules)
            return true;

        var allowed = true;
        foreach (var ruleNode in rules)
        {
            if (ruleNode is not JsonObject rule)
                continue;

            var action = rule["action"]?.GetValue<string>() ?? "allow";
            var isApply = true; // 是否应用此 rule

            if (rule["os"] is JsonObject os)
            {
                var osName = os["name"]?.GetValue<string>() ?? "";
                isApply = osName == "windows"; // 仅 Windows 平台的 rule 生效
            }

            if (isApply)
                allowed = action == "allow";
        }

        return allowed;
    }

    /// <summary>检查 rules 是否匹配当前平台（Windows）— JsonElement 版本（DownloadService 使用）</summary>
    public static bool Check(JsonElement rules)
    {
        var allowed = true;

        foreach (var rule in rules.EnumerateArray())
        {
            var action = rule.TryGetProperty("action", out var actionProp) ? actionProp.GetString() : "allow";
            var isApply = true;

            if (rule.TryGetProperty("os", out var os))
            {
                var osName = os.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : "";
                isApply = osName == "windows";
            }

            if (isApply)
                allowed = action == "allow";
        }

        return allowed;
    }
}
