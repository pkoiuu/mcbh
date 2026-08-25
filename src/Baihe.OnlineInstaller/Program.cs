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
        public const string AppVersion = "1.1.14";

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
                    log.AppendLine($"download url   : {info.DownloadUrl}");

                    ok = info.DownloadUrl.IndexOf("BaiheServer_Setup", StringComparison.OrdinalIgnoreCase) >= 0
                         && info.DownloadUrl.IndexOf("OnlineSetup", StringComparison.OrdinalIgnoreCase) < 0;
                    log.AppendLine(ok
                        ? "target check   : PASS（指向完整安装包）"
                        : "target check   : FAIL（未指向完整安装包！）");

                    // 线路择优（真实测速）
                    try
                    {
                        var best = await UpdateService.PickFastestAsync(info);
                        log.AppendLine($"best line      : {best.Source} ({best.SpeedMbps:0.0} MB/s)");
                        log.AppendLine($"best url       : {best.BestUrl}");
                    }
                    catch (Exception ex)
                    {
                        log.AppendLine($"line check     : 测速异常（不影响主结论）: {ex.Message}");
                    }
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
