// 多线程下载器 — HTTP Range 分块并发下载，支持多 URL 候选自动切换与超时保护
// 思路: 对每个候选 URL 先用 Range bytes=0-0 探测总大小（15s 超时）；若服务器不支持 Range（返回 200）则回退单线程全量下载
// 分块写入同一目标文件的不同偏移，进度按已写入字节数汇总；某块 30s 无数据或请求超时会抛出并切换到下一个候选 URL
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Baihe.OnlineInstaller
{
    /// <summary>多线程下载器（支持取消、进度回调、多候选 URL 自动切换）</summary>
    public class Downloader : IDisposable
    {
        private readonly string[] _urls;
        private readonly string _destPath;
        private readonly int _threads;
        private readonly HttpClient _http;
        private readonly string _authHeaderName;
        private readonly string _authHeaderValue;

        /// <summary>探测超时（毫秒）— 用户网络慢时放宽到 30s</summary>
        private const int ProbeTimeoutMs = 30000;

        /// <summary>单块读取无数据超时（毫秒）</summary>
        private const int ReadStallTimeoutMs = 30000;

        private long _downloaded;
        private int _completedChunks;
        private int _lastTick;
        private long _lastBytes;
        private Action<long, long, double> _onProgress;
        private string _currentUrl = "";

        /// <summary>分块完成数（用于进度文案）</summary>
        public int CompletedChunks => _completedChunks;

        /// <summary>当前使用的 URL（诊断用）</summary>
        public string CurrentUrl => _currentUrl;

        /// <summary>
        /// 仅探测（不下载）— 供 selftest 验证线路可达性；返回 (是否成功, 总大小)
        /// </summary>
        public async Task<(bool ok, long total)> ProbeForTestAsync()
        {
            foreach (var url in _urls)
            {
                if (string.IsNullOrEmpty(url))
                    continue;
                try
                {
                    var (total, _) = await ProbeAsync(url, CancellationToken.None).ConfigureAwait(false);
                    if (total > 0)
                        return (true, total);
                }
                catch
                {
                    // 尝试下一个
                }
            }
            return (false, -1);
        }

        /// <summary>
        /// 构造下载器
        /// </summary>
        /// <param name="urls">候选 URL 列表（按优先级排序，逐个尝试直到成功）</param>
        /// <param name="destPath">目标文件路径</param>
        /// <param name="threads">下载线程数</param>
        /// <param name="authHeaderName">可选鉴权 header 名（如 "token"），为空则不添加</param>
        /// <param name="authHeaderValue">可选鉴权 header 值</param>
        public Downloader(string[] urls, string destPath, int threads = 8,
            string authHeaderName = "", string authHeaderValue = "")
        {
            _urls = urls.Length > 0 ? urls : new[] { "" };
            _destPath = destPath;
            _threads = Math.Max(1, Math.Min(threads, 16));
            _authHeaderName = authHeaderName;
            _authHeaderValue = authHeaderValue;
            // 单请求超时 5 分钟（响应头到达前的总时限）；流读取另有 stall 超时保护
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        }

        /// <summary>给请求附加鉴权 header（如自建加速服务的 token）</summary>
        private void ApplyAuth(HttpRequestMessage req)
        {
            if (!string.IsNullOrEmpty(_authHeaderName) && !string.IsNullOrEmpty(_authHeaderValue))
            {
                req.Headers.TryAddWithoutValidation(_authHeaderName, _authHeaderValue);
            }
        }

        /// <summary>
        /// 开始下载 — 依次尝试每个候选 URL；前 2 轮多线程分块，后 2 轮自动降级单线程全量下载兜底。
        /// onProgress(long downloaded, long total, double speedMBps) 高频回调
        /// </summary>
        public async Task<bool> DownloadAsync(
            Action<long, long, double> onProgress,
            Action<string> onStatus,
            CancellationToken token)
        {
            _onProgress = onProgress;

            const int totalRounds = 4; // 1-2 多线程, 3-4 单线程兜底
            for (var attempt = 1; attempt <= totalRounds; attempt++)
            {
                if (token.IsCancellationRequested)
                    return false;

                var forceSingle = attempt >= 3;
                foreach (var url in _urls)
                {
                    if (string.IsNullOrEmpty(url))
                        continue;
                    if (token.IsCancellationRequested)
                        return false;

                    _currentUrl = url;
                    _downloaded = 0;
                    _completedChunks = 0;
                    _lastTick = Environment.TickCount;
                    _lastBytes = 0;

                    try
                    {
                        if (await TryDownloadFromAsync(url, onStatus, token, forceSingle).ConfigureAwait(false))
                            return true;
                        onStatus?.Invoke(forceSingle ? "下载未完成，正在重试..." : "下载校验未通过，正在切换方式重试...");
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return false; // 用户取消
                    }
                    catch
                    {
                        onStatus?.Invoke(forceSingle ? "当前线路异常，正在重试..." : "当前线路异常，自动降级单线程重试...");
                    }
                }

                if (attempt < totalRounds)
                {
                    // 整轮重试前清理残留文件
                    try { if (File.Exists(_destPath)) File.Delete(_destPath); } catch { }
                    var mode = forceSingle ? "单线程" : "多线程";
                    onStatus?.Invoke($"第 {attempt} 次尝试失败，正在第 {attempt + 1} 次重试（{mode}）...");
                }
            }

            return false;
        }

        /// <summary>
        /// 从单个 URL 完整下载
        /// </summary>
        /// <param name="forceSingle">true=单线程全量下载（最可靠，接受 200/206，读到 EOF）；false=多线程分块（探测 Range 后并发）</param>
        private async Task<bool> TryDownloadFromAsync(string url, Action<string> onStatus, CancellationToken token, bool forceSingle)
        {
            if (forceSingle)
            {
                // 单线程全量下载 — 不探测、不设 Range，直接 GET 读到 EOF（Content-Length 可能不准，以实际写入为准）
                onStatus?.Invoke("使用单线程下载（更稳定）...");
                var written = await DownloadFullAsync(url, token).ConfigureAwait(false);
                if (written <= 0)
                    return false;
                _total = written;
                _onProgress?.Invoke(written, written, 0);
                return true;
            }

            // 1. 探测总大小与 Range 支持（30s 超时）
            onStatus?.Invoke("正在连接下载服务器...");
            var (total, rangeSupported) = await ProbeAsync(url, token);
            if (total <= 0)
                return false;

            onStatus?.Invoke(rangeSupported && _threads > 1
                ? $"开始下载（{_threads} 线程）..."
                : "服务器不支持断点续传，使用单线程下载...");

            // 2. 按支持情况选择下载方式
            if (rangeSupported && total > 1 && _threads > 1)
            {
                var chunkSize = (long)Math.Ceiling((double)total / _threads);
                var tasks = new Task[_threads];
                for (var i = 0; i < _threads; i++)
                {
                    var start = (long)i * chunkSize;
                    var end = Math.Min(start + chunkSize - 1, total - 1);
                    if (start >= total)
                    {
                        tasks[i] = Task.CompletedTask;
                        continue;
                    }
                    tasks[i] = DownloadChunkAsync(url, start, end, token);
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            else
            {
                await DownloadChunkAsync(url, 0, total - 1, token, isFull: true).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();

            // 3. 校验大小 — 不通过视为失败（抛异常触发上层自动重试），而不是直接返回 false
            var fi = new FileInfo(_destPath);
            if (total > 0 && (!fi.Exists || fi.Length != total))
                throw new IOException($"文件大小校验不通过（期望 {total}，实际 {(fi.Exists ? fi.Length : 0)}）");

            _onProgress?.Invoke(total, total, 0);
            return true;
        }

        /// <summary>单线程全量下载：GET 读到 EOF，返回实际写入字节数</summary>
        private async Task<long> DownloadFullAsync(string url, CancellationToken token)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAuth(req);

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)resp.StatusCode}");

            long written = 0;
            using (var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var fs = new FileStream(_destPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                var buffer = new byte[128 * 1024];
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var n = await ReadWithStallTimeoutAsync(stream, buffer, token).ConfigureAwait(false);
                    if (n <= 0)
                        break;
                    fs.Write(buffer, 0, n);
                    written += n;
                    Report(n);
                }
                fs.Flush();
            }
            return written;
        }
        private void Report(long delta)
        {
            Interlocked.Add(ref _downloaded, delta);
            var now = Environment.TickCount;
            if (now - _lastTick >= 200)
            {
                var elapsedSec = Math.Max((now - _lastTick) / 1000.0, 0.001);
                var downloaded = Volatile.Read(ref _downloaded);
                var speed = (double)(downloaded - _lastBytes) / 1024.0 / 1024.0 / elapsedSec;
                _lastTick = now;
                _lastBytes = downloaded;
                _onProgress?.Invoke(downloaded, _total, speed);
            }
        }

        private long _total; // 探测到的总大小（Report 上报用）

        /// <summary>Range 探测（15s 超时）：返回 (总大小, 是否支持 Range)。失败抛异常由上层切换线路</summary>
        private async Task<(long total, bool range)> ProbeAsync(string url, CancellationToken token)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(ProbeTimeoutMs);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(0, 0);
            ApplyAuth(req);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)resp.StatusCode}");

            // 支持 Range: 206 + Content-Range
            var contentRange = resp.Content.Headers.ContentRange;
            if (resp.StatusCode == System.Net.HttpStatusCode.PartialContent && contentRange != null)
            {
                _total = contentRange.Length ?? -1;
                return (_total, true);
            }

            // 不支持 Range（200）→ 读 Content-Length 全量
            _total = resp.Content.Headers.ContentLength ?? -1;
            return (_total, false);
        }

        /// <summary>下载一个分块到文件偏移（isFull=true 时从 0 写起，覆盖写）；30s 无数据自动中断</summary>
        private async Task DownloadChunkAsync(string url, long start, long end, CancellationToken token, bool isFull = false)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!isFull)
                req.Headers.Range = new RangeHeaderValue(start, end);
            ApplyAuth(req);

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)resp.StatusCode}");
            // Range 分块请求必须返回 206；若服务器返回 200（全量），说明并发/代理行为异常，
            // 此时按块写入会破坏文件，直接抛异常触发降级重试
            if (!isFull && resp.StatusCode != System.Net.HttpStatusCode.PartialContent)
                throw new HttpRequestException($"服务器未返回分块内容（HTTP {(int)resp.StatusCode}），自动降级重试");

            long written = 0;
            using (var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var fs = new FileStream(_destPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
            {
                var offset = isFull ? 0 : start;
                if (offset > 0)
                    fs.Seek(offset, SeekOrigin.Begin);

                var buffer = new byte[128 * 1024];
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var n = await ReadWithStallTimeoutAsync(stream, buffer, token).ConfigureAwait(false);
                    if (n <= 0)
                        break;
                    fs.Write(buffer, 0, n);
                    written += n;
                    Report(n);
                }
                fs.Flush();
            }

            // 块完整性校验 — 写入字节数必须等于期望块大小，否则说明连接提前断开/数据缺失，抛异常触发重试
            var expected = isFull ? (_total > 0 ? _total : end - start + 1) : (end - start + 1);
            if (written != expected)
                throw new IOException($"分块不完整（期望 {expected} 字节，实际 {written} 字节）");

            Interlocked.Increment(ref _completedChunks);
        }

        /// <summary>带 30s 无数据超时的流读取（防止镜像挂起导致无限等待）</summary>
        private static async Task<int> ReadWithStallTimeoutAsync(System.IO.Stream stream, byte[] buffer, CancellationToken token)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(ReadStallTimeoutMs);
            try
            {
                return await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException("读取数据超时（线路可能已中断）");
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
