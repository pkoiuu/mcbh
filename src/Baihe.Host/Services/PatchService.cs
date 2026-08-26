// 智能增量更新 · 补丁应用服务 — 下载差量包 → SHA256 复核 → 暂存 → 退出应用脚本
// 流程: update.patch IPC 触发本类后台任务;
//       前端收 patch.progress/complete/error;patchStaged 后由 update.patchRestart 触发:
//       生成 apply 脚本(等待主进程退出 → rename-then-copy 覆盖 → 删除 deletes → 启动新版)
// 安全: 补丁内文件逐个与 _meta.json 的 sha256 复核;zip 条目路径规范化防 zip-slip;
//       用户数据(account/settings/saves/config/options.txt 等)由构建侧 manifest 规则排除,
//       不在补丁 files/deletes 中 —— apply 只动清单内文件。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Baihe.Host.Ipc;

namespace Baihe.Host.Services;

/// <summary>构建信息 — 从 AssemblyMetadata 读取(正式构建均为空/false)</summary>
public static class BuildInfo
{
    private static string? Meta(string key)
    {
        try
        {
            foreach (var attr in Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
            {
                if (attr is AssemblyMetadataAttribute m && m.Key == key && !string.IsNullOrEmpty(m.Value))
                    return m.Value;
            }
        }
        catch { }
        return null;
    }

    /// <summary>测试渠道构建(test-release.yml 注入 ChannelOverride=test)</summary>
    public static bool IsTestChannel => string.Equals(Meta("Channel"), "test", StringComparison.OrdinalIgnoreCase);

    /// <summary>完整版本标签(test 构建 = tag 去 v 全值,如 "1.1.26-test2";正式构建为 null)</summary>
    public static string? TagFull => Meta("AppVersion");
}

/// <summary>
/// 差量补丁暂存与应用。
/// 静态状态:_staged(_stagedDir/_stagedMeta)表示补丁已就绪等待重启;跨请求共享,IpcRouter 单线程注册但 Task.Run 并发,加锁防护。
/// </summary>
public static class PatchService
{
    private sealed class StagedPatch
    {
        public string Dir = "";
        public string From = "";
        public string To = "";
        public List<string> Files = new();
        public List<string> Deletes = new();
        public Dictionary<string, string> Hashes = new();
    }

    private static readonly object _lock = new();
    private static StagedPatch? _staged;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { UseProxy = false };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BaiheLauncher-Patch/1.0");
        return client;
    }

    // ============================== 公共入口 ==============================

    /// <summary>补丁是否已暂存完成(前端「重启完成更新」按钮的前置条件)</summary>
    public static bool IsStaged
    {
        get { lock (_lock) { return _staged != null; } }
    }

    /// <summary>当前已暂存的目标版本号(未暂存返回 null)</summary>
    public static string? StagedTarget
    {
        get { lock (_lock) { return _staged?.To; } }
    }

    /// <summary>
    /// 下载并校验差量补丁,暂存到 %TEMP%。
    /// 抛出异常表示失败(由调用方推 patch.error);成功推送 patch.complete。
    /// </summary>
    public static async Task DownloadAndStageAsync(UpdateInfo info)
    {
        if (string.IsNullOrEmpty(info.PatchUrl))
            throw new InvalidOperationException("该版本没有可用的增量补丁");
        var token = UpdateService.GetOnlineToken();

        var destZip = Path.Combine(Path.GetTempPath(),
            $"BaihePatch_{info.CurrentVersion}_to_{info.LatestVersion}.zip");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, info.PatchUrl);
            if (!string.IsNullOrEmpty(token))
                req.Headers.TryAddWithoutValidation("token", token);

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength ?? -1L;
            long received = 0;
            var lastPushTick = Environment.TickCount64;
            var buffer = new byte[256 * 1024];

            using (var fs = new FileStream(destZip, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await using var stream = await resp.Content.ReadAsStreamAsync();
                while (true)
                {
                    var n = await stream.ReadAsync(buffer);
                    if (n <= 0) break;
                    fs.Write(buffer, 0, n);
                    received += n;
                    var now = Environment.TickCount64;
                    if (now - lastPushTick >= 300)
                    {
                        lastPushTick = now;
                        PushProgress(total, received);
                    }
                }
            }
            PushProgress(total > 0 ? total : received, received);

            if (total > 0 && new FileInfo(destZip).Length != total)
                throw new IOException($"补丁大小不符(期望 {total},实际 {new FileInfo(destZip).Length})");

            var staged = ExtractAndVerify(destZip);
            lock (_lock) { _staged = staged; }

            IpcRouter.PushEvent("patch.complete", new
            {
                files = staged.Files.Count,
                deletes = staged.Deletes.Count,
                from = staged.From,
                target = staged.To,
            });
        }
        catch
        {
            TryDelete(destZip);
            throw;
        }
    }

    /// <summary>
    /// 生成应用脚本并启动(等主程序退出后执行),随后调用方负责 Shutdown 主程序。
    /// 返回 false 表示未暂存或启动失败(error 说明原因)。
    /// </summary>
    public static bool TryPrepareAndLaunch(out string error)
    {
        error = "";
        StagedPatch? st;
        lock (_lock) { st = _staged; }
        if (st == null)
        {
            error = "补丁尚未下载或校验未通过";
            return false;
        }

        try
        {
            var appRoot = AppContext.BaseDirectory.TrimEnd('\\', '/');
            var applyScript = Path.Combine(st.Dir, "baihe_apply_update.ps1");

            var sb = new StringBuilder();
            sb.AppendLine("# auto-generated by Baihe Launcher patch service - do not edit");
            sb.AppendLine("$ErrorActionPreference = 'Continue'");
            sb.AppendLine("Start-Sleep -Seconds 3");
            sb.AppendLine($"$app   = '{PsEscape(appRoot)}'");
            sb.AppendLine($"$stage = '{PsEscape(st.Dir)}'");
            sb.AppendLine($"$doneFile = Join-Path $app 'update_done.marker'");
            sb.AppendLine("$tryCopy = { param($src,$dst)");
            sb.AppendLine("  for ($i=0; $i -lt 5; $i++) {");
            sb.AppendLine("    try {");
            sb.AppendLine("      if (Test-Path $dst) { Move-Item $dst ($dst + '.old_update_bak') -Force -ErrorAction Stop }");
            sb.AppendLine("      Copy-Item $src $dst -Force");
            sb.AppendLine("      if (Test-Path ($dst + '.old_update_bak')) { Remove-Item ($dst + '.old_update_bak') -Force -ErrorAction SilentlyContinue }");
            sb.AppendLine("      return $true");
            sb.AppendLine("    } catch { Start-Sleep -Milliseconds 600 }");
            sb.AppendLine("  }");
            sb.AppendLine("  return $false");
            sb.AppendLine("}");
            foreach (var rel in st.Deletes)
            {
                sb.AppendLine($"Remove-Item (Join-Path $app '{PsEscape(rel)}') -Force -Recurse -ErrorAction SilentlyContinue");
            }
            foreach (var rel in st.Files)
            {
                sb.AppendLine($"$src = Join-Path $stage '{PsEscape(rel)}'");
                sb.AppendLine($"$dst = Join-Path $app   '{PsEscape(rel)}'");
                sb.AppendLine("$dstDir = Split-Path $dst -Parent");
                sb.AppendLine("if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Force -Path $dstDir | Out-Null }");
                sb.AppendLine("& $tryCopy $src $dst | Out-Null");
            }
            sb.AppendLine("Set-Content -Path $doneFile -Value ('applied to ' + [DateTime]::UtcNow.ToString('o'))");
            sb.AppendLine($"Start-Process -FilePath (Join-Path $app 'Baihe.exe') -WorkingDirectory $app");

            System.IO.File.WriteAllText(applyScript, sb.ToString(), new UTF8Encoding(false));

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{applyScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // ============================== 内部实现 ==============================

    private static void PushProgress(long total, long received)
    {
        var percent = total > 0 ? Math.Min(100.0, Math.Round(received * 100.0 / total, 1)) : 0;
        IpcRouter.PushEvent("patch.progress", new
        {
            percent,
            receivedMB = Math.Round(received / 1048576.0, 1),
            totalMB = total > 0 ? Math.Round(total / 1048576.0, 1) : 0,
        });
    }

    /// <summary>解压到 %TEMP%\baihe_stage_{to} 并逐文件 SHA256 复核</summary>
    private static StagedPatch ExtractAndVerify(string zipPath)
    {
        string? metaFrom = null, metaTo = null;
        List<string> files = new(), deletes = new();
        Dictionary<string, string>? hashes = null;
        var stage = "";

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            var metaEntry = archive.GetEntry("_meta.json")
                ?? throw new InvalidDataException("补丁缺少 _meta.json");
            string metaJson;
            using (var r = new StreamReader(metaEntry.Open()))
                metaJson = r.ReadToEnd();
            using var doc = System.Text.Json.JsonDocument.Parse(metaJson);
            var root = doc.RootElement;
            metaFrom = root.GetProperty("from").GetString() ?? "";
            metaTo = root.GetProperty("to").GetString() ?? "";
            files = root.TryGetProperty("files", out var fEl)
                ? fEl.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList()
                : new List<string>();
            deletes = root.TryGetProperty("deletes", out var dEl)
                ? dEl.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList()
                : new List<string>();
            hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("hashes", out var hEl))
            {
                foreach (var p in hEl.EnumerateObject())
                    hashes[p.Name] = p.Value.GetString() ?? "";
            }

            // 目录清洗:目标 stage 必须全新
            stage = Path.Combine(Path.GetTempPath(), "baihe_patch_stage_" + (metaTo ?? Guid.NewGuid().ToString("N")));
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            Directory.CreateDirectory(stage);

            var stageWithSep = stage.EndsWith('/') ? stage : stage + '/';
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.Length == 0 || name.EndsWith('/')) continue;      // 目录条目跳过
                if (name.Contains("..") || Path.IsPathRooted(name))
                    throw new InvalidDataException("非法补丁路径(zip-slip 防护): " + name);
                var target = Path.Combine(stage, name.Replace('/', Path.DirectorySeparatorChar));
                var fullTarget = Path.GetFullPath(target);
                if (!fullTarget.StartsWith(stageWithSep, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("越界补丁路径: " + name);
                Directory.CreateDirectory(Path.GetDirectoryName(fullTarget)!);
                entry.ExtractToFile(fullTarget, overwrite: true);
            }
        }

        // 逐文件 SHA256 与 _meta.hashes 对账
        foreach (var rel in files)
        {
            if (!hashes.TryGetValue(rel, out var expect))
                throw new InvalidDataException("补丁缺少校验信息: " + rel);
            var full = Path.Combine(stage, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(full))
                throw new InvalidDataException("补丁文件缺失: " + rel);
            var actual = Sha256(full);
            if (!string.Equals(actual, expect, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA256 校验失败: " + rel);
        }

        TryDelete(zipPath);
        return new StagedPatch { Dir = stage, From = metaFrom ?? "", To = metaTo ?? "", Files = files, Deletes = deletes, Hashes = hashes! };
    }

    private static string Sha256(string path)
    {
        using var fs = System.IO.File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexStringLower(sha.ComputeHash(fs));
    }

    private static void TryDelete(string path)
    {
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }
    }

    private static string PsEscape(string s) => "'" + s.Replace("'", "''") + "'";
}

/// <summary>UpdateInfo 扩展字段容器 — 实际字段定义见 UpdateInfo 类(PatchAvailable/PatchUrl/PatchSizeBytes)</summary>
