// 主窗口代码后置 — 聊天页注入（partial）
// 向外部聊天页面注入返回按钮与消息监控脚本
// 注意: 本文件是 MainWindow 的 partial 分部，共享主文件的 WebView/_isExternalNav 等成员

using System;
using System.Threading.Tasks;

namespace Baihe.Host;

public partial class MainWindow
{
private async Task InjectChatMonitorScriptAsync()
    {
        if (WebView.CoreWebView2 == null) return;

        var script = """
            (function() {
                if (window.__baihe_monitor__) return;
                window.__baihe_monitor__ = true;

                var lastNotifiedMsg = '';
                var notifyThrottle = null;

                function extractLatestMessage() {
                    var selectors = [
                        '.mx_EventTile_last .mx_EventTile_line',
                        '.mx_RoomView_MessageList .mx_EventTile:last-child .mx_EventTile_line',
                        '.mx_EventTile .mx_MTextBody',
                        '[data-testid="eventTileMessage"]'
                    ];
                    for (var i = 0; i < selectors.length; i++) {
                        var els = document.querySelectorAll(selectors[i]);
                        if (els.length > 0) {
                            var last = els[els.length - 1];
                            var text = last.textContent || last.innerText || '';
                            if (text && text.trim().length > 0) {
                                return text.trim().substring(0, 100);
                            }
                        }
                    }
                    return null;
                }

                function checkForNewMessage() {
                    var msg = extractLatestMessage();
                    if (msg && msg !== lastNotifiedMsg) {
                        lastNotifiedMsg = msg;
                        // 节流：避免短时间大量通知
                        if (notifyThrottle) clearTimeout(notifyThrottle);
                        notifyThrottle = setTimeout(function() {
                            window.chrome.webview.postMessage('__chat_notify__:' + msg);
                        }, 1000);
                    }
                }

                var observer = new MutationObserver(function(mutations) {
                    var hasNewContent = mutations.some(function(m) {
                        return m.addedNodes.length > 0;
                    });
                    if (hasNewContent) {
                        checkForNewMessage();
                    }
                });

                // 延迟启动观察器，等待 Element SPA 渲染完成
                setTimeout(function() {
                    var target = document.body;
                    if (target) {
                        observer.observe(target, {
                            childList: true,
                            subtree: true
                        });
                    }
                }, 5000);
            })();
        """;

        try
        {
            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebView2] 注入监控脚本失败: {ex.Message}");
        }
    }

private async Task InjectBackButtonAsync()
    {
        if (WebView.CoreWebView2 == null) return;

        var script = """
            (function() {
                if (document.getElementById('__baihe_back__')) {
                    // 已存在 — 更新点击逻辑（URL 可能已变化）
                    updateBackButton();
                    return;
                }

                // === 返回按钮 — 半透明小圆点，不遮挡内容 ===
                var btn = document.createElement('div');
                btn.id = '__baihe_back__';
                btn.innerHTML = '←';
                btn.style.cssText = [
                    'position:fixed',
                    'top:12px',
                    'left:12px',
                    'z-index:2147483647',
                    'width:32px',
                    'height:32px',
                    'line-height:32px',
                    'text-align:center',
                    'border-radius:50%',
                    'background:rgba(26,26,28,0.7)',
                    'color:#ffffff',
                    'font-size:16px',
                    'font-family:-apple-system,BlinkMacSystemFont,sans-serif',
                    'cursor:pointer',
                    'border:1px solid rgba(255,255,255,0.1)',
                    'backdrop-filter:blur(8px)',
                    '-webkit-backdrop-filter:blur(8px)',
                    'box-shadow:0 1px 6px rgba(0,0,0,0.2)',
                    'transition:all 0.2s ease',
                    'user-select:none',
                    'overflow:hidden',
                    'white-space:nowrap'
                ].join(';');

                // hover 时展开为带文字的胶囊
                btn.onmouseenter = function() {
                    btn.style.background = 'rgba(26,26,28,0.95)';
                    btn.style.width = 'auto';
                    btn.style.padding = '0 14px';
                    btn.style.borderRadius = '16px';
                    btn.innerHTML = '← ' + getBackButtonText();
                };
                btn.onmouseleave = function() {
                    btn.style.background = 'rgba(26,26,28,0.7)';
                    btn.style.width = '32px';
                    btn.style.padding = '0';
                    btn.style.borderRadius = '50%';
                    btn.innerHTML = '←';
                };
                btn.onmousedown = function() { btn.style.transform = 'scale(0.95)'; };
                btn.onmouseup = function() { btn.style.transform = 'scale(1)'; };
                btn.onclick = handleBackClick;
                document.body.appendChild(btn);

                function getBackButtonText() {
                    var host = location.hostname;
                    if (host === 'auth.hhj520.top') return '返回聊天';
                    return '返回主页';
                }

                function handleBackClick() {
                    var host = location.hostname;
                    if (host === 'auth.hhj520.top') {
                        // 在注册页面 — 导航回聊天主页
                        window.location.href = 'https://chat.hhj520.top';
                    } else {
                        // 在聊天主页 — 返回启动器
                        window.chrome.webview.postMessage('__nav_home__');
                    }
                }

                function updateBackButton() {
                    var existingBtn = document.getElementById('__baihe_back__');
                    if (existingBtn) {
                        existingBtn.onclick = handleBackClick;
                    }
                }
            })();
        """;

        try
        {
            await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebView2] 注入返回按钮失败: {ex.Message}");
        }
    }
}