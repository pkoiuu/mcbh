// 游戏实例模型 — 代表一个可启动的 Minecraft 版本
// 实例存储在 .minecraft/versions/&lt;id&gt;/ 目录下

using System;
using System.Collections.Generic;

namespace Baihe.Host.Models;

/// <summary>
/// 游戏实例 — 一个可启动的 Minecraft 版本配置
/// </summary>
public class GameInstance
{
    /// <summary>实例 ID（版本目录名）</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Minecraft 版本号（如 1.20.4）</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>版本类型（release, snapshot, old_beta 等）</summary>
    public string Type { get; set; } = "release";

    /// <summary>加载器类型（vanilla, fabric, forge）</summary>
    public string Loader { get; set; } = "vanilla";

    /// <summary>最后游玩时间</summary>
    public string LastPlayed { get; set; } = "从未";

    /// <summary>Mod 数量</summary>
    public int ModCount { get; set; } = 0;

    /// <summary>是否已安装（版本 JAR 存在）</summary>
    public bool IsInstalled { get; set; } = false;

    /// <summary>实例显示名称</summary>
    public string DisplayName => Loader == "vanilla" ? Version : $"{Version} · {Loader}";
}
