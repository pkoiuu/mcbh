// 服务器列表服务 — 确保白鹤服务器自动出现在 Minecraft 多人游戏列表 (servers.dat)
// 原理: servers.dat 是未压缩的 NBT 文件，根 TAG_Compound 含 "servers" TAG_List，
//       每个元素是 TAG_Compound，含 name/ip/acceptTextures/hideAddress 等字段。
//       启动游戏前检查指定 ip 是否在列表中，不存在则自动追加。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Baihe.Host.Services;

/// <summary>
/// 服务器列表服务 — 自动把白鹤服务器添加到 Minecraft 服务器列表
/// </summary>
public static class ServerListService
{
    /// <summary>服务器在多人游戏列表中的显示名</summary>
    public const string BaiheServerName = "白鹤服务器";

    /// <summary>
    /// 确保 servers.dat 中存在名为「白鹤服务器」的条目（ip 匹配指定地址）
    /// 逻辑:
    ///  1. 已存在 name=白鹤服务器 且 ip 匹配 → 不操作
    ///  2. 存在同 ip 但名字不同（如手动添加的默认名）→ 改为「白鹤服务器」
    ///  3. 否则追加新条目
    /// </summary>
    /// <param name="address">服务器地址（不含端口），如 play.simpfun.cn</param>
    /// <param name="port">服务器端口</param>
    /// <returns>true=本次修改了文件；false=已符合要求或失败</returns>
    public static bool EnsureBaiheServer(string address, int port)
    {
        try
        {
            var mcDir = InstanceService.GetMcDirectory();
            var serversDatPath = Path.Combine(mcDir, "servers.dat");

            var hostPort = $"{address}:{port}";

            NbtHelper.NbtCompound? root;
            bool gzip = false;
            if (File.Exists(serversDatPath))
            {
                var raw = File.ReadAllBytes(serversDatPath);
                gzip = raw.Length >= 2 && raw[0] == 0x1F && raw[1] == 0x8B;
                root = NbtHelper.ReadFile(serversDatPath);
            }
            else
            {
                root = null;
            }

            // 文件不存在或解析失败 — 创建新的根结构
            if (root == null)
            {
                root = new NbtHelper.NbtCompound();
                var list = new NbtHelper.NbtList
                {
                    ElementType = 10, // TAG_Compound
                    Items = new List<NbtHelper.NbtTag>(),
                };
                root.Set("servers", list);
            }

            // 获取 servers 列表
            var servers = root.Get("servers") as NbtHelper.NbtList;
            if (servers == null)
            {
                servers = new NbtHelper.NbtList
                {
                    ElementType = 10,
                    Items = new List<NbtHelper.NbtTag>(),
                };
                root.Set("servers", servers);
            }

            // 情况 1: 已存在 name=白鹤服务器 且 ip 匹配 → 无需操作
            var hasBaihe = servers.Items.OfType<NbtHelper.NbtCompound>()
                .Any(c => string.Equals(c.GetString("name")?.Trim(), BaiheServerName, StringComparison.Ordinal)
                          && string.Equals((c.GetString("ip") ?? "").Trim(), hostPort, StringComparison.OrdinalIgnoreCase));
            if (hasBaihe)
                return false;

            // 情况 2: 存在同 ip 但名字不同 → 改名为「白鹤服务器」
            var sameIp = servers.Items.OfType<NbtHelper.NbtCompound>()
                .FirstOrDefault(c => string.Equals((c.GetString("ip") ?? "").Trim(), hostPort, StringComparison.OrdinalIgnoreCase));
            if (sameIp != null)
            {
                sameIp.Set("name", new NbtHelper.NbtString { Value = BaiheServerName });
                NbtHelper.WriteFile(serversDatPath, root, gzip);
                return true;
            }

            // 情况 3: 追加新条目
            var entry = new NbtHelper.NbtCompound();
            entry.Set("name", new NbtHelper.NbtString { Value = BaiheServerName });
            entry.Set("ip", new NbtHelper.NbtString { Value = hostPort });
            entry.Set("acceptTextures", new NbtHelper.NbtByte { Value = 1 });
            entry.Set("hideAddress", new NbtHelper.NbtByte { Value = 0 });
            servers.Items.Add(entry);

            NbtHelper.WriteFile(serversDatPath, root, gzip);
            return true;
        }
        catch
        {
            // 添加失败不影响游戏启动
            return false;
        }
    }
}
