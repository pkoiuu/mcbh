// 多线程下载器 — HTTP Range 分块并发下载
// 思路: 先用 Range bytes=0-0 探测总大小；若服务器不支持 Range（返回 200）则回退单线程全量下载
// 分块写入同一目标文件的不同偏移，进度按已写入字节数汇总
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Baihe.OnlineInstaller
{
    /// <summary>多线程下载器（支持取消与进度回调）</summary>
    public class Downloader : IDisposable
    {
        private readonly string _url;
        private readonly string _destPath;
        private readonly int _threads;
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        private long _downloaded;
        private int _completedChunks;
        private int _lastTick;
        private long _lastBytes;
        private Action<long, long, double> _onProgress;

        /// <summary>分块完成数（用于进度文案）</summary>
        public int CompletedChunks => _completedChunks;

        public Downloader(string url, string destPath, int threads = 8)
        {
            _url = url;
            _destPath = destPath;
            _threads = Math.Max(1, Math.Min(threads, 16));
        }

        /// <summary>
        /// 开始下载。onProgress(long downloaded, long total, double speedMBps) 高频回调；
        /// 返回 true=成功（totalBytes 为总大小，可能为 -1 表示未知）
        /// </summary>
        public async Task<bool> DownloadAsync(
            Action<long, long, double> onProgress,
            Action<string> onStatus,
            CancellationToken token)
        {
            _downloaded = 0;
            _completedChunks = 0;
            _onProgress = onProgress;
            _lastTick = Environment.TickCount;
            _lastBytes = 0;

            // 1. 探测总大小与 Range 支持
            onStatus?.Invoke("正在连接下载服务器...");
            var (total, rangeSupported) = await ProbeAsync(token);
            if (total <= 0)
                return false;

            onStatus?.Invoke(rangeSupported && _threads > 1
                ? $"开始下载（{_threads} 线程）..."
                : "服务器不支持断点续传，使用单线程下载...");

            // 2. 按支持情况选择下载方式
            if (rangeSupported && total > 1 && _threads > 1)
            {
                // 多线程分块
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
                    tasks[i] = DownloadChunkAsync(start, end, token);
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            else
            {
                // 单线程全量
                await DownloadChunkAsync(0, total - 1, token, isFull: true).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();

            // 3. 校验大小
            var fi = new FileInfo(_destPath);
            if (total > 0 && fi.Exists && fi.Length != total)
                return false;

            onProgress?.Invoke(total, total, 0);
            return true;
        }

        /// <summary>累计进度并节流上报（200ms 一次）</summary>
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

        /// <summary>Range 探测：返回 (总大小, 是否支持 Range)。不支持时 total 为完整大小</summary>
        private async Task<(long total, bool range)> ProbeAsync(CancellationToken token)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, _url);
            req.Headers.Range = new RangeHeaderValue(0, 0);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return (-1, false);

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

        /// <summary>下载一个分块到文件偏移（isFull=true 时从 0 写起，覆盖写）</summary>
        private async Task DownloadChunkAsync(long start, long end, CancellationToken token, bool isFull = false)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, _url);
            if (!isFull)
                req.Headers.Range = new RangeHeaderValue(start, end);

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)resp.StatusCode}");

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
                    var n = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                    if (n <= 0)
                        break;
                    fs.Write(buffer, 0, n);
                    Report(n);
                }
                fs.Flush();
            }

            Interlocked.Increment(ref _completedChunks);
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
