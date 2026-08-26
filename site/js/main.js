/* ============================================================
 * 白鹤服务器启动器 · 官网脚本
 * 职责：下载链接装配 / 最新版本解析（GitHub API → 兜底）/
 *       首页公告渲染（news.json）/ 滚动入场动效
 * ============================================================ */

/* ★★★★★ 用户自定义配置区（改这里即可，其余无需动）★★★★★ */
const CONFIG = {
  // 在线版安装器直链（官网唯一下载入口；可随时替换为新地址）
  DOWNLOAD_ONLINE_URL: "https://hhj520.top/mc/BaiheOnlineSetup.exe",

  // GitHub 仓库（用于版本徽章查询与页面链接）
  GITHUB_REPO: "pkoiuu/mcbh",

  // 无法连到 GitHub API 时展示的兜底版本号
  FALLBACK_VERSION: "1.1.25",

  // 首页公告数据源（部署 workflow 会把仓库根 news.json 复制到站点根）
  NEWS_PATH: "./news.json",

  // 公告最多显示条数
  NEWS_LIMIT: 3,
};
/* ★★★★★ 配置区结束 ★★★★★ */

(function () {
  "use strict";

  var REPO = CONFIG.GITHUB_REPO;
  var API_LATEST = "https://api.github.com/repos/" + REPO + "/releases/latest";

  /* ---------- 1. 最新版本号解析（版本徽章展示用） ---------- */
  function fetchLatestVersion() {
    return new Promise(function (resolve) {
      var settled = false;
      var done = function (v, fromApi) {
        if (settled) return;
        settled = true;
        resolve({ version: v, fromApi: !!fromApi });
      };
      var timer = setTimeout(function () { done(CONFIG.FALLBACK_VERSION, false); }, 6000);
      var ctrl = typeof AbortController !== "undefined" ? new AbortController() : null;
      if (ctrl) {
        setTimeout(function () { try { ctrl.abort(); } catch (e) { } }, 5500);
      }
      var opts = ctrl ? { signal: ctrl.signal } : {};
      fetch(API_LATEST, opts)
        .then(function (r) { return r.ok ? r.json() : null; })
        .then(function (data) {
          clearTimeout(timer);
          var tag = data && data.tag_name ? String(data.tag_name).replace(/^v/i, "") : "";
          done(tag || CONFIG.FALLBACK_VERSION, !!tag);
        })
        .catch(function () {
          clearTimeout(timer);
          done(CONFIG.FALLBACK_VERSION, false);
        });
    });
  }

  /* ---------- 2. 版本徽章 / 年份刷新 ---------- */
  function paintVersion(res) {
    var els = document.querySelectorAll("[data-version]");
    for (var i = 0; i < els.length; i++) {
      els[i].textContent = "v" + res.version + (res.fromApi ? " · 最新" : "");
    }
    // 侧边栏窗口内的静态小版本号跟随主徽章
    var sideVer = document.querySelector(".side-ver");
    if (sideVer) sideVer.textContent = "v" + res.version;
    var yearEl = document.getElementById("year");
    if (yearEl) yearEl.textContent = String(new Date().getFullYear());
  }

  /* ---------- 3. 首页公告渲染（与启动器共用 news.json 数据源）---------- */
  function esc(s) {
    return String(s == null ? "" : s)
      .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;").replace(/'/g, "&#39;");
  }

  function loadNews() {
    var listEl = document.getElementById("news-list");
    var secEl = document.getElementById("news-section");
    if (!listEl) return;
    fetch(CONFIG.NEWS_PATH + "?t=" + Date.now())
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (data) {
        var items = data && Array.isArray(data.news) ? data.news.slice(0, CONFIG.NEWS_LIMIT) : [];
        if (!items.length) throw new Error("empty");
        var html = [];
        for (var i = 0; i < items.length; i++) {
          var n = items[i] || {};
          html.push(
            '<div class="news-item">' +
              "<time>" + esc(n.date) + "</time>" +
              "<div><b>" + esc(n.title) + "</b>" +
              "<p>" + esc(n.desc) + "</p></div>" +
            "</div>"
          );
        }
        listEl.innerHTML = html.join("");
      })
      .catch(function () {
        // 静默隐藏整个公告区块（预览环境无 news.json 时也不露红）
        if (secEl) secEl.style.display = "none";
      });
  }

  /* ---------- 4. 滚动入场动效 ---------- */
  function initReveal() {
    var els = document.querySelectorAll(".reveal");
    if (!("IntersectionObserver" in window)) {
      for (var i = 0; i < els.length; i++) els[i].classList.add("in");
      return;
    }
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) {
        if (en.isIntersecting) {
          en.target.classList.add("in");
          io.unobserve(en.target);
        }
      });
    }, { threshold: 0.12 });
    for (var j = 0; j < els.length; j++) io.observe(els[j]);
  }

  /* ---------- boot ---------- */
  document.addEventListener("DOMContentLoaded", function () {
    initReveal();
    // 下载入口统一从 CONFIG 注入（HTML href 仅为无 JS 兜底）
    var links = document.querySelectorAll("[data-dl-online]");
    for (var k = 0; k < links.length; k++) {
      if (CONFIG.DOWNLOAD_ONLINE_URL) links[k].href = CONFIG.DOWNLOAD_ONLINE_URL;
    }
    loadNews();
    fetchLatestVersion().then(paintVersion);
  });
})();
