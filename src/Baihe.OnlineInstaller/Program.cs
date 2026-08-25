// 在线版安装程序入口 — 白鹤服务器启动器
// 流程: 查最新版本 → 镜像测速择优 → 多线程下载完整安装包 → 启动安装程序
// 支持 --selftest 命令行：无界面自检（验证最新版本解析指向完整安装包而非在线安装器），
// 结果写入 %TEMP%\baihe_selftest.log，退出码 0=通过 / 1=失败
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Baihe.OnlineInstaller
{
    internal static class Program
    {
        /// <summary>当前版本（与主程序同步）</summary>
        public const string AppVersion = "1.1.19";

        /// <summary>GitHub 仓库</summary>
        public const string RepoOwner = "pkoiuu";
        public const string RepoName = "mcbh";

        /// <summary>默认服务器（仅展示用）</summary>
        public const string ServerName = "白鹤服务器";

        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0] == "--selftest")
            {
                SelfTestAsync().GetAwaiter().GetResult();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        /// <summary>无界面自检 — 验证 API 解析出的下载 URL 是完整安装包（非在线安装器）</summary>
        private static async Task SelfTestAsync()
        {
            var log = new StringBuilder();
            var ok = false;
            try
            {
                log.AppendLine("[selftest] BaiheOnlineSetup selftest start");

                var info = await UpdateService.GetLatestAsync();
                if (info == null)
                {
                    log.AppendLine("FAIL: 无法获取最新版本（GitHub API 不可达）");
                }
                else
                {
                    log.AppendLine($"latest version : {info.Version}");
                    log.AppendLine($"download url   : {info.BestUrl}");

                    ok = info.BestUrl.IndexOf("BaiheServer_Setup", StringComparison.OrdinalIgnoreCase) >= 0
                         && info.BestUrl.StartsWith("http://199.68.217.4:8090/", StringComparison.OrdinalIgnoreCase);
                    log.AppendLine(ok
                        ? "target check   : PASS（指向自建加速服务的完整安装包）"
                        : "target check   : FAIL（未指向自建加速服务！）");

                    // 加速服务可达性实测（带 token 探测）
                    try
                    {
                        var token = UpdateService.GetToken();
                        log.AppendLine($"token          : {(string.IsNullOrEmpty(token) ? "未配置（BAIHE_ONLINE_TOKEN）" : "已配置（长度 " + token.Length + "）")}");
                        if (!string.IsNullOrEmpty(token))
                        {
                            using var dl = new Downloader(new[] { info.BestUrl }, Path.Combine(Path.GetTempPath(), "baihe_probe.tmp"),
                                threads: 8, authHeaderName: "token", authHeaderValue: token);
                            var okProbe = await dl.ProbeForTestAsync();
                            log.AppendLine($"probe          : {okProbe}");
                        }
                    }
                    catch (Exception ex)
                    {
                        log.AppendLine($"probe          : 异常: {ex.Message}");
                    }
                }

                // 下载器超时保护实测 — 用两个不可达线路模拟"卡在连接下载服务器"，验证不会无限挂起
                try
                {
                    log.AppendLine("stall test     : 开始（两个不可达线路，应快速失败而非挂 30 分钟）...");
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    using (var dl = new Downloader(
                        new[] { "https://10.255.255.1/nope.exe", "https://192.0.2.1/nope.exe" },
                        Path.Combine(Path.GetTempPath(), "baihe_stall_test.tmp"),
                        threads: 8))
                    {
                        var result = await dl.DownloadAsync(
                            (d, t, s) => { },
                            s => { },
                            System.Threading.CancellationToken.None);
                        sw.Stop();
                        log.AppendLine($"stall test     : 返回={result}，耗时 {sw.ElapsedMilliseconds / 1000}s（<60s 即证明超时保护生效）");
                        if (sw.Elapsed.TotalSeconds < 60 && result == false)
                            log.AppendLine("stall test     : PASS（超时保护正常，不会卡死）");
                        else
                            log.AppendLine("stall test     : FAIL（超时保护异常）");
                    }
                }
                catch (Exception ex)
                {
                    log.AppendLine($"stall test     : 异常: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                log.AppendLine("EXCEPTION: " + ex);
            }
            log.AppendLine(ok ? "[selftest] RESULT: PASS" : "[selftest] RESULT: FAIL");
            Console.WriteLine(log.ToString());
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "baihe_selftest.log"), log.ToString()); }
            catch { }
            Environment.Exit(ok ? 0 : 1);
        }
    }
}
