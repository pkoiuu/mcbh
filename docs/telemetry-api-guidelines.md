# 白鹤服务器启动器 — 自建遥测 API 清单与守则

> 版本: 1.1.0 | 更新: 2026-07-24
>
> 本文档定义白鹤启动器自建数据收集 API 的技术选型、开发规范、安全守则、部署清单和运维要求。所有开发人员必须遵守本文档。

---

## 一、项目概述

### 1.1 目标

搭建一个轻量级自建 API 服务，收集白鹤启动器用户的：

- IP 地址及地理位置（省份/城市/运营商）
- 玩家登录用户名
- 用户邮箱（微软正版和第三方验证登录时有值）
- 用户微信名（启动器首次启动时弹窗收集）
- 操作系统环境
- 语言环境
- 模组名称及数量
- 启动器版本

### 1.2 服务规模

| 指标 | 预期值 |
|------|--------|
| 同时在线用户 | < 100 人 |
| 日均请求量 | < 1000 次 |
| 数据保留周期 | 12 个月 |
| 服务器规格 | 2 核 2G |

### 1.3 成本预算

| 项目 | 费用 |
|------|------|
| 阿里云轻量 2 核 2G | 38 元/年 |
| 域名（可选） | ~30 元/年 |
| SSL 证书 | 免费 (Let's Encrypt) |
| ip2region 数据库 | 免费开源 |
| **总计** | **~68 元/年** |

---

## 二、技术栈选型守则

### 2.1 必选项

| 层 | 技术 | 版本要求 | 理由 |
|---|------|---------|------|
| API 框架 | ASP.NET Core Minimal API | .NET 10 | 与启动器同生态，Minimal API 无需控制器 |
| 数据库 | SQLite | 3.x+ | <100 人场景足够，零运维，单文件备份 |
| IP 地理定位 | ip2region xdb | 2.0+ | 99.9% 准确率，10 微秒查询，离线无需外部 API |
| ORM | EF Core SQLite | 10.x | 与 .NET 10 原生集成 |
| 反向代理 | Nginx | 1.18+ | SSL 终止 + 隐藏真实端口 |
| 进程守护 | systemd | — | Linux 原生，崩溃自动重启 |
| SSL 证书 | Let's Encrypt | — | 免费，自动续期 |
| 看板可视化 | ECharts | 5.x+ | 内嵌 HTML，无需额外服务 |

### 2.2 禁止项

- **禁止**使用 Firebase / Google Analytics 等国内不稳定服务
- **禁止**将用户数据上传到第三方平台
- **禁止**使用 MongoDB（关系型数据不需要文档数据库）
- **禁止**使用 PostgreSQL（<100 人不需要，增加运维复杂度）
- **禁止**部署 Docker（2G 内存服务器跑容器浪费资源）
- **禁止**使用 Grafana / Metabase（内存占用过大，内嵌 ECharts 足够）

### 2.3 约定

- API 端口固定 `5000`，Nginx 监听 `443`
- 数据库文件路径 `/opt/telemetry/data/telemetry.db`
- ip2region 数据库路径 `/opt/telemetry/data/ip2region.xdb`
- 日志路径 `/opt/telemetry/logs/`
- 看板访问路径 `/admin`
- 上报接口路径 `/api/track/report`

---

## 三、API 设计规范

### 3.1 端点定义

| 端点 | 方法 | 用途 | 鉴权 |
|------|------|------|------|
| `/api/track/report` | POST | 客户端上报数据 | API Key |
| `/api/track/policy` | GET | 获取遥测策略（是否允许上报） | API Key |
| `/admin` | GET | 看板 HTML 页面 | 密码 Token |
| `/admin/api/overview` | GET | 概览统计 JSON | 密码 Token |
| `/admin/api/players` | GET | 玩家列表 JSON | 密码 Token |
| `/admin/api/provinces` | GET | 省份分布 JSON | 密码 Token |
| `/admin/api/trend` | GET | 活跃趋势 JSON | 密码 Token |

### 3.2 上报接口规范

**请求路径**: `POST /api/track/report`

**请求头**:
```
Content-Type: application/json
X-Api-Key: <启动器内置的 secret key>
```

**请求体**:
```json
{
  "uuid": "player-uuid-string",
  "username": "玩家用户名",
  "email": "user@example.com",
  "wechatName": "用户微信名",
  "launcherVersion": "1.0.1",
  "os": "Microsoft Windows 10.0.19045",
  "language": "zh-CN",
  "accountType": "Microsoft"
}
```

**字段说明**:

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| uuid | string | 是 | 玩家 UUID |
| username | string | 是 | 玩家游戏用户名 |
| email | string | 否 | 用户邮箱（微软正版和第三方验证登录时有值，离线模式为空字符串） |
| wechatName | string | 否 | 用户微信名（启动器首次启动时收集，离线模式也可能有值） |
| launcherVersion | string | 是 | 启动器版本号（三段格式：主.次.修） |
| os | string | 是 | 操作系统信息 |
| language | string | 是 | 语言环境（如 zh-CN） |
| accountType | string | 是 | 账户类型（Offline/Microsoft/ThirdParty） |

**成功响应** (200):
```json
{
  "success": true
}
```

**失败响应** (401):
```json
{
  "success": false,
  "error": "Invalid API Key"
}
```

### 3.3 策略接口规范

**请求路径**: `GET /api/track/policy`

**请求头**:
```
X-Api-Key: <启动器内置的 secret key>
```

**成功响应** (200):
```json
{
  "enabled": true
}
```

**说明**:
- 客户端在首次上报前请求此接口，决定是否允许上报
- `enabled: true` 表示允许上报，`enabled: false` 表示禁止上报
- 接口不可达时客户端按 fail-open 处理（允许上报）
- 服务端可通过此接口远程关闭数据收集，无需更新客户端

### 3.4 设计守则

1. **所有接口必须返回 JSON**，禁止返回 HTML（看板页面除外）
2. **时间字段统一使用 ISO 8601 格式**（如 `2026-07-24T02:00:00Z`）
3. **所有字符串使用 UTF-8 编码**
4. **分页接口必须支持 `page` 和 `pageSize` 参数**，默认 `pageSize=20`
5. **错误响应必须包含 `success: false` 和 `error` 字段**
6. **上报接口必须幂等**：相同 UUID 的重复上报不报错，只更新 `last_active`
7. **IP 地址必须从 `X-Real-IP` 或 `X-Forwarded-For` 头获取**，不信任客户端上报的 IP
8. **accountType 仅接受枚举值**：`Offline`、`Microsoft`、`ThirdParty`，其他值拒绝并返回 400
9. **请求体最大 64KB**，超出返回 413
10. **所有接口响应时间必须 < 200ms**（SQLite + 本地 ip2region 足以保证）

---

## 四、数据收集规范

### 4.1 收集字段清单

| 字段 | 来源 | 收集时机 | 是否敏感 | 存储方式 |
|------|------|---------|---------|---------|
| uuid | 客户端 Auth 系统 | 游戏启动时 | 否 | 明文 |
| username | 客户端 Auth 系统 | 游戏启动时 | 中 | 明文 |
| email | 客户端 Auth 系统（微软/第三方登录） | 游戏启动时 | 是 | 明文 |
| wechatName | 客户端首次启动弹窗收集 | 游戏启动时 | 中 | 明文 |
| ip | 服务端从请求头获取 | 每次请求 | 是 | 明文（用于省份解析后可脱敏） |
| country | ip2region 解析 | 每次请求 | 否 | 明文 |
| province | ip2region 解析 | 每次请求 | 否 | 明文 |
| city | ip2region 解析 | 每次请求 | 否 | 明文 |
| isp | ip2region 解析 | 每次请求 | 否 | 明文 |
| os | 客户端 Environment.OSVersion | 游戏启动时 | 否 | 明文 |
| language | 客户端 CultureInfo | 游戏启动时 | 否 | 明文 |
| launcherVersion | 客户端 Assembly | 游戏启动时 | 否 | 明文 |
| accountType | 客户端 Auth 系统 | 游戏启动时 | 否 | 明文 |

### 4.2 禁止收集的字段

- **禁止**收集玩家游戏内密码
- **禁止**收集玩家 Microsoft 账号 token
- **禁止**收集玩家游戏存档内容
- **禁止**收集玩家聊天记录
- **禁止**收集 MAC 地址
- **禁止**收集硬件序列号
- **禁止**收集浏览器历史

### 4.3 IP 地址处理守则

1. IP 地址从 Nginx 转发的 `X-Real-IP` 头获取，**禁止信任客户端请求体中的 IP**
2. IP 解析后立即提取省份/城市/运营商，存入数据库
3. 原始 IP 地址保留用于排障，但看板展示时**只展示省份级别**，不展示完整 IP
4. 日志中的 IP 地址保留完整形式，日志文件权限设为 `600`

### 4.4 数据库表结构

```sql
CREATE TABLE player_reports (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    uuid TEXT NOT NULL,
    username TEXT NOT NULL,
    email TEXT DEFAULT '',
    wechat_name TEXT DEFAULT '',
    ip TEXT,
    country TEXT,
    province TEXT,
    city TEXT,
    isp TEXT,
    os TEXT,
    language TEXT,
    launcher_version TEXT,
    account_type TEXT DEFAULT 'Offline',
    reported_at TEXT NOT NULL
);

CREATE INDEX idx_reports_uuid ON player_reports(uuid);
CREATE INDEX idx_reports_province ON player_reports(province);
CREATE INDEX idx_reports_time ON player_reports(reported_at);
CREATE INDEX idx_reports_username ON player_reports(username);
CREATE INDEX idx_reports_wechat ON player_reports(wechat_name);
```

---

## 五、安全守则

### 5.1 传输安全

- [ ] **必须使用 HTTPS**，禁止 HTTP 明文传输
- [ ] SSL 证书使用 Let's Encrypt，配置自动续期
- [ ] Nginx 配置 HSTS 头：`Strict-Transport-Security: max-age=31536000`
- [ ] 禁用 TLS 1.0 和 1.1，只允许 TLS 1.2+
- [ ] Nginx 配置安全头：`X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`

### 5.2 接口鉴权

- [ ] 上报接口 `/api/track/report` 必须验证 `X-Api-Key` 头
- [ ] API Key 在启动器编译时硬编码，使用 `const string` 而非配置文件
- [ ] 看板接口 `/admin/*` 必须验证密码 Token
- [ ] 管理密码通过环境变量传入，**禁止硬编码在代码中**
- [ ] Token 有效期 24 小时，过期需重新登录
- [ ] 密码错误 5 次锁定 IP 15 分钟

### 5.3 输入验证

- [ ] 所有字符串字段必须做长度限制（username ≤ 16, email ≤ 256, wechatName ≤ 32, accountType ≤ 16）
- [ ] UUID 必须做格式校验（正则或 GUID.TryParse）
- [ ] accountType 必须做枚举校验（仅允许 Offline/Microsoft/ThirdParty）
- [ ] 请求体大小限制 64KB
- [ ] 请求频率限制：同一 IP 每分钟最多 30 次请求

### 5.4 数据库安全

- [ ] 数据库文件权限设为 `600`（仅 owner 可读写）
- [ ] 数据库文件所有者为 `www-data`
- [ ] 禁止数据库文件通过 Nginx 直接访问
- [ ] 每日自动备份到独立目录，保留 7 天

### 5.5 服务器安全

- [ ] SSH 禁止 root 远程登录
- [ ] SSH 使用密钥认证，禁止密码登录
- [ ] 防火墙只开放 22(SSH)、80(重定向)、443(HTTPS) 端口
- [ ] API 端口 5000 只监听 127.0.0.1，禁止外部访问
- [ ] 安装 fail2ban 防暴力破解

---

## 六、部署清单

### 6.1 服务器初始化

- [ ] 购买阿里云轻量应用服务器 2 核 2G，Ubuntu 22.04
- [ ] 配置 SSH 密钥登录，禁用密码登录
- [ ] 更新系统：`apt update && apt upgrade -y`
- [ ] 安装基础工具：`apt install -y nginx ufw fail2ban`
- [ ] 配置防火墙：`ufw allow 22 && ufw allow 80 && ufw allow 443 && ufw enable`

### 6.2 .NET 运行时

- [ ] 安装 .NET 10 Runtime：`apt install -y dotnet-runtime-10.0`
- [ ] 验证安装：`dotnet --info`

### 6.3 应用部署

- [ ] 创建应用目录：`mkdir -p /opt/telemetry/data /opt/telemetry/logs`
- [ ] 发布 API 项目：`dotnet publish -c Release -r linux-x64 --self-contained -o /opt/telemetry`
- [ ] 下载 ip2region.xdb 到 `/opt/telemetry/data/`
- [ ] 设置文件权限：`chown -R www-data:www-data /opt/telemetry`
- [ ] 设置环境变量（管理密码）：写入 `/etc/telemetry.env`
  ```
  TELEMETRY_ADMIN_PASSWORD=你的强密码
  TELEMETRY_API_KEY=你的API密钥
  ```

### 6.4 systemd 服务

- [ ] 创建 `/etc/systemd/system/telemetry-api.service`
  ```ini
  [Unit]
  Description=Baihe Telemetry API
  After=network.target

  [Service]
  WorkingDirectory=/opt/telemetry
  EnvironmentFile=/etc/telemetry.env
  ExecStart=/usr/bin/dotnet /opt/telemetry/TelemetryApi.dll --urls=http://127.0.0.1:5000
  Restart=always
  RestartSec=5
  User=www-data

  [Install]
  WantedBy=multi-user.target
  ```
- [ ] 启动服务：`systemctl enable telemetry-api && systemctl start telemetry-api`
- [ ] 验证服务：`systemctl status telemetry-api`

### 6.5 Nginx 反向代理

- [ ] 创建 `/etc/nginx/sites-available/telemetry`
  ```nginx
  server {
      listen 80;
      server_name telemetry.你的域名.com;
      return 301 https://$host$request_uri;
  }

  server {
      listen 443 ssl http2;
      server_name telemetry.你的域名.com;

      ssl_certificate     /etc/letsencrypt/live/telemetry.你的域名.com/fullchain.pem;
      ssl_certificate_key /etc/letsencrypt/live/telemetry.你的域名.com/privkey.pem;
      ssl_protocols       TLSv1.2 TLSv1.3;
      ssl_ciphers         HIGH:!aNULL:!MD5;

      add_header Strict-Transport-Security "max-age=31536000" always;
      add_header X-Content-Type-Options "nosniff" always;
      add_header X-Frame-Options "DENY" always;

      client_max_body_size 64K;

      location / {
          proxy_pass http://127.0.0.1:5000;
          proxy_set_header Host $host;
          proxy_set_header X-Real-IP $remote_addr;
          proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
          proxy_set_header X-Forwarded-Proto $scheme;
      }
  }
  ```
- [ ] 启用站点：`ln -s /etc/nginx/sites-available/telemetry /etc/nginx/sites-enabled/`
- [ ] 测试配置：`nginx -t`
- [ ] 重载：`systemctl reload nginx`

### 6.6 SSL 证书

- [ ] 安装 certbot：`apt install -y certbot python3-certbot-nginx`
- [ ] 申请证书：`certbot --nginx -d telemetry.你的域名.com`
- [ ] 验证自动续期：`certbot renew --dry-run`

### 6.7 数据备份

- [ ] 创建备份脚本 `/opt/telemetry/backup.sh`
  ```bash
  #!/bin/bash
  DATE=$(date +%Y%m%d)
  cp /opt/telemetry/data/telemetry.db /opt/telemetry/backups/telemetry_${DATE}.db
  find /opt/telemetry/backups/ -name "telemetry_*.db" -mtime +7 -delete
  ```
- [ ] 设置定时任务：`crontab -e` 添加 `0 3 * * * /opt/telemetry/backup.sh`

### 6.8 部署验证

- [ ] `curl -k https://localhost/api/track/report -X POST -H "Content-Type: application/json" -H "X-Api-Key: 你的key" -d '{"uuid":"test","username":"test"}'` 返回 200
- [ ] 浏览器访问 `https://telemetry.你的域名.com/admin` 可看板页面
- [ ] 看板密码输入正确后能看到空数据表格
- [ ] `systemctl status telemetry-api` 显示 active (running)
- [ ] `journalctl -u telemetry-api -f` 无错误日志

---

## 七、客户端集成守则

### 7.1 上报时机

| 事件 | 上报内容 | 方式 |
|------|---------|------|
| 游戏启动时 | UUID/用户名/邮箱/微信名/OS/语言/版本/模组列表/IP | 异步 POST，不阻塞 UI，每会话仅首次 |

> **注意**: 遥测上报已从"启动器启动 + 用户登录 + 游戏启动"三个时机整合为仅在"游戏启动时"统一上报一次。这样确保所有信息（包括游戏用户名）都已就绪，避免上报不完整的数据。每会话仅首次游戏启动时上报，后续不重复。

### 7.2 客户端守则

- [ ] 上报必须使用 `async/await`，**禁止使用 `.Result` 阻塞 UI 线程**
- [ ] 上报失败时静默处理，**禁止弹窗报错**（遥测不应影响用户体验）
- [ ] HttpClient 必须是 `static readonly` 单例，**禁止每次 new**
- [ ] 上报超时设为 5 秒，超时后放弃本次上报
- [ ] API Key 编译时嵌入，**禁止写入配置文件**
- [ ] 首次启动时弹窗告知数据收集内容，提供"拒绝"选项
- [ ] 拒绝后不上报任何数据，但启动器功能不受影响
- [ ] 上报请求不携带任何认证 token（与微软登录系统隔离）

### 7.3 客户端数据收集代码规范

```csharp
// 正确：异步、静默、单例 HttpClient
private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

public static async Task ReportAsync(string uuid, string username, string? email = null, string? wechatName = null, string accountType = "Offline")
{
    try
    {
        var payload = new
        {
            uuid,
            username,
            email = email ?? string.Empty,
            wechatName = wechatName ?? string.Empty,
            accountType = accountType ?? "Offline",
            launcherVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3),
            os = Environment.OSVersion.ToString(),
            language = CultureInfo.CurrentUICulture.Name,
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Api-Key", ApiKey);

        await _httpClient.PostAsync($"{ServerUrl}/api/track/report", content);
    }
    catch
    {
        // 静默处理，不影响用户体验
    }
}
```

---

## 八、看板设计规范

### 8.1 页面结构

```
/admin (登录页)
  └─ 输入密码 → 验证成功
       └─ /admin (看板首页)
            ├─ 概览卡片：总玩家 / 今日活跃 / 总上报 / 模组总数
            ├─ 省份分布：中国地图热力图
            ├─ 活跃趋势：折线图（近 30 天）
            ├─ 玩家列表：表格（用户名/省份/OS/模组数/最近上线）
            └─ 模组排行：柱状图
```

### 8.2 看板守则

- [ ] 看板必须使用响应式布局，支持手机端查看
- [ ] 地图使用 ECharts 中国地图组件，按省份着色
- [ ] 表格支持分页（每页 20 条）和搜索
- [ ] 所有图表数据通过 AJAX 从 `/admin/api/*` 获取
- [ ] 看板页面内嵌在 API 项目中，**禁止单独部署前端项目**
- [ ] 看板深色/浅色主题跟随系统设置
- [ ] 表格中 IP 地址**只显示省份**，不显示完整 IP
- [ ] 看板页面设置 `X-Frame-Options: DENY`，防止被嵌入 iframe

---

## 九、运维守则

### 9.1 日常检查

- [ ] 每日检查 `systemctl status telemetry-api` 服务状态
- [ ] 每日检查 `journalctl -u telemetry-api --since "1 hour ago"` 错误日志
- [ ] 每周检查磁盘空间 `df -h`，确保 > 20% 可用
- [ ] 每周检查数据库大小 `ls -lh /opt/telemetry/data/telemetry.db`

### 9.2 数据维护

- [ ] 每月检查备份文件是否正常生成
- [ ] 每季度清理 12 个月以上的旧数据
- [ ] 每季度更新 ip2region.xdb 数据库（从 GitHub 下载最新版）

### 9.3 安全更新

- [ ] 每月 `apt update && apt upgrade -y` 更新系统
- [ ] 每月检查 .NET Runtime 安全补丁
- [ ] 每半年更换管理密码和 API Key
- [ ] 每半年检查 SSL 证书续期状态

### 9.4 应急处理

- [ ] 服务崩溃：`systemctl restart telemetry-api`
- [ ] 数据库锁死：停止服务 → 备份数据库 → 重启服务
- [ ] 磁盘满：清理日志 `> /opt/telemetry/logs/*.log`
- [ ] 被攻击：Nginx 封禁 IP `deny IP;`，检查异常请求日志

---

## 十、隐私合规守则

### 10.1 用户知情权

- [ ] 启动器首次启动时弹窗，明确告知收集的数据类型
- [ ] 提供完整隐私政策链接
- [ ] 提供"拒绝收集"选项，拒绝后功能不受影响

### 10.2 数据最小化

- [ ] 只收集本文档第四章定义的字段，**禁止额外收集**
- [ ] 新增收集字段必须更新本文档并重新通知用户

### 10.3 数据安全

- [ ] 数据传输全程 HTTPS 加密
- [ ] 数据库文件权限 600
- [ ] 管理密码使用强密码（16 位+，含大小写数字符号）
- [ ] API Key 使用 32 位随机字符串

### 10.4 数据保留

- [ ] 用户数据保留 12 个月
- [ ] 用户可请求删除自己的数据（联系管理员）
- [ ] 服务停止时，所有数据必须在 30 天内删除

---

## 十一、开发检查清单

### 11.1 编码前

- [ ] 阅读本文档全部内容
- [ ] 确认技术栈与第二章一致
- [ ] 确认数据库表结构与第四章一致
- [ ] 确认 API 端点与第三章一致

### 11.2 编码中

- [ ] 所有异步方法不使用 `.Result`
- [ ] 所有用户输入做长度和格式校验
- [ ] 所有错误有 try-catch 处理
- [ ] 所有日志不包含敏感信息（密码、完整 IP 在日志中可用但不在看板）
- [ ] 代码风格与启动器项目保持一致

### 11.3 编码后

- [ ] `dotnet build` 编译通过
- [ ] 本地 `curl` 测试所有端点
- [ ] 检查输入校验是否覆盖异常输入（空字符串、超长字符串、非法 JSON）
- [ ] 检查鉴权逻辑是否生效（无 API Key / 错误 API Key / 无 Token）
- [ ] 检查看板页面在手机端显示正常
- [ ] 代码提交前 self-review

### 11.4 部署后

- [ ] 完成第六章全部部署清单
- [ ] 完成第六章 6.8 部署验证
- [ ] 从启动器实际测试一次完整上报流程
- [ ] 看板能看到上报的数据
- [ ] 确认定时备份任务正常运行

---

## 十二、版本历史

| 版本 | 日期 | 变更 |
|------|------|------|
| 1.1.0 | 2026-07-24 | 新增 email/wechatName 字段；新增 `/api/track/policy` 策略接口；上报时机统一为游戏启动时；每会话去重 |
| 1.0.0 | 2026-07-24 | 初始版本 |

---

> 本文档为白鹤服务器启动器项目的强制性技术规范。任何对本规范的例外或变更，必须经过评审并更新本文档。
