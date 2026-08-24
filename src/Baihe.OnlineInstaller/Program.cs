// 在线版安装程序入口 — 白鹤服务器启动器
// 流程: 查最新版本 → 镜像测速择优 → 多线程下载完整安装包 → 启动安装程序
using System;
using System.Windows.Forms;

namespace Baihe.OnlineInstaller
{
    internal static class Program
    {
        /// <summary>当前版本（与主程序同步）</summary>
        public const string AppVersion = "1.1.11";

        /// <summary>GitHub 仓库</summary>
        public const string RepoOwner = "pkoiuu";
        public const string RepoName = "mcbh";

        /// <summary>默认服务器（仅展示用）</summary>
        public const string ServerName = "白鹤服务器";

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
