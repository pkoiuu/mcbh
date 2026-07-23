# P1 Spec: 内存智能推荐

## 1. 概述

启动器根据系统总物理内存自动计算推荐内存分配值，首次使用时自动设置，并在设置页显示推荐理由。用户可手动覆盖。

### 当前状态

- `SettingsService.cs`: `MemoryMB` 默认值 4096 (4GB)，范围限制 512-32768
- `Settings.svelte`: 内存滑块固定选项 `[2, 3, 4, 6, 8, 12, 16]` GB，提示文字硬编码"建议分配 2-4 GB"
- `LaunchService.cs`: 使用 `settings.MemoryMB` 作为 `-Xmx` 参数
- 无系统内存检测能力

### 目标

- 后端新增 `system.memory` IPC 命令，返回系统总物理内存（MB）
- 首次安装（settings.json 不存在）时，根据系统内存自动设置推荐值
- 设置页内存分配区域显示动态推荐值和理由（如"检测到 16GB 内存，推荐分配 6GB"）
- 滑块选项根据系统内存动态调整（不超过系统总内存的 70%）
- 用户手动调整后保存到 settings.json，后续不再自动覆盖

## 2. 架构设计

### 2.1 推荐算法

```
推荐内存 = min(系统总内存 × 0.5, 系统总内存 - 4GB)
```

规则:
- 预留 4GB 给系统和其他程序（浏览器、聊天软件等）
- 不超过系统总内存的 50%（避免影响系统稳定性）
- 最低 2GB（Minecraft 1.21+ 最低要求）
- 最高 16GB（超过 16GB 对 Minecraft 性能无显著提升）

示例:
- 8GB 系统 → 推荐 4GB (8-4=4, 8×0.5=4)
- 16GB 系统 → 推荐 6GB (16-4=12, 16×0.5=8, min=8... 实际取 min(8, 12) = 8)

修正算法:
```
推荐内存 = max(2GB, min(系统总内存 × 0.5, 系统总内存 - 4GB, 16GB))
```

示例:
- 8GB 系统 → min(4, 4, 16) = 4GB
- 16GB 系统 → min(8, 12, 16) = 8GB
- 32GB 系统 → min(16, 28, 16) = 16GB
- 4GB 系统 → max(2, min(2, 0, 16)) = 2GB (4-4=0, 回退到最低 2GB)

### 2.2 IPC 命令

#### system.memory

返回系统总物理内存信息。

**响应:**
```json
{
  "totalMB": 16384,
  "totalGB": 16,
  "recommendedMB": 8192,
  "recommendedGB": 8
}
```

**后端实现 (`MainWindow.xaml.cs`):**
```csharp
_ipcRouter.Register("system.memory", async _ =>
{
    var memStatus = new Microsoft.VisualBasic.Devices.ComputerInfo();
    var totalMB = (int)(memStatus.TotalPhysicalMemory / (1024 * 1024));
    var recommendedMB = CalculateRecommendedMemory(totalMB);
    return new
    {
        totalMB,
        totalGB = totalMB / 1024,
        recommendedMB,
        recommendedGB = recommendedMB / 1024
    };
});
```

**推荐值计算 (`SettingsService.cs` 新增静态方法):**
```csharp
public static int CalculateRecommendedMemory(int totalMB)
{
    var half = (int)(totalMB * 0.5);
    var reserved = totalMB - 4096;
    var recommended = Math.Min(half, reserved);
    recommended = Math.Min(recommended, 16384); // 上限 16GB
    recommended = Math.Max(recommended, 2048);  // 下限 2GB
    return recommended;
}
```

### 2.3 首次安装自动设置

修改 `SettingsService.GetAsync()`:
- 当 settings.json 不存在时（首次安装），调用系统内存检测计算推荐值
- 将推荐值作为 `MemoryMB` 的初始值写入

```csharp
if (!File.Exists(SettingsPath))
{
    _cached = new LauncherSettings
    {
        MemoryMB = CalculateRecommendedMemory(GetTotalPhysicalMemoryMB())
    };
    SaveAsync(_cached).Wait();
}
```

新增 `GetTotalPhysicalMemoryMB()` 辅助方法。

### 2.4 前端设置页改造

#### 数据加载
- 组件挂载时调用 `ipc('system.memory')` 获取系统内存信息
- 存储到 `systemMemory` 状态变量

#### 滑块选项动态化
- 当前: `const memoryOptions = [2, 3, 4, 6, 8, 12, 16]`
- 改为根据系统内存生成: 包含推荐值，最大不超过系统内存的 70%
- 保留 2GB 作为最低选项

```typescript
function generateMemoryOptions(totalGB: number): number[] {
  const maxGB = Math.floor(totalGB * 0.7)
  const options = [2, 3, 4, 6, 8, 12, 16].filter(g => g <= maxGB)
  // 确保至少有 2GB 和推荐值可选
  if (options.length < 2) options.push(maxGB)
  return [...new Set(options)].sort((a, b) => a - b)
}
```

#### 提示文字动态化
- 当前: `"建议分配 2-4 GB"`
- 改为: `"检测到 {totalGB}GB 内存 · 推荐 {recommendedGB}GB"` 
- 如果用户当前值 ≠ 推荐值，追加 `"（当前: {currentGB}GB）"`

#### 推荐值标记
- 滑块上推荐值位置添加一个小标记点（可选，不强制）

## 3. UI 设计

```
┌─────────────────────────────────────────────┐
│ 内存分配                                     │
│                                              │
│  [━━━━━●━━━━━━]  6 GB                       │
│  检测到 16GB 内存 · 推荐 8GB                  │
└─────────────────────────────────────────────┘
```

- 滑块样式保持不变（h-1.5, accent-[var(--primary)]）
- 提示文字从静态改为动态，颜色保持 `text-[var(--muted-foreground)]`
- 当用户值低于推荐值时，提示文字可显示警告色（可选）

## 4. 约束与边界

### 4.1 不修改的内容
- `LaunchService.cs` 中 `-Xmx` 参数的传递逻辑
- `SettingsService.SetAsync()` 中 512-32768 的范围限制
- settings.json 的文件格式

### 4.2 边界情况
- **系统内存检测失败**: 回退到默认 4096MB (4GB)
- **系统内存极低 (< 4GB)**: 推荐值锁定为 2GB
- **系统内存极大 (> 32GB)**: 推荐值锁定为 16GB
- **settings.json 已存在**: 不覆盖用户已有设置
- **IPC system.memory 调用失败**: 前端使用默认 memoryOptions 和静态提示

### 4.3 性能要求
- 系统内存检测 < 10ms（Windows API 调用）
- 不阻塞 UI 线程

## 5. 涉及文件

| 文件 | 改动类型 | 说明 |
|------|---------|------|
| `src/Baihe.Host/Services/SettingsService.cs` | 修改 | 新增 CalculateRecommendedMemory + GetTotalPhysicalMemoryMB 方法，首次安装时自动设置推荐值 |
| `src/Baihe.Host/MainWindow.xaml.cs` | 修改 | 注册 system.memory IPC 命令 |
| `src/Baihe.UI/src/pages/Settings.svelte` | 修改 | 动态加载系统内存，滑块选项动态化，提示文字动态化 |

## 6. 验收标准

- [ ] 首次安装时内存分配自动设置为推荐值
- [ ] 设置页显示"检测到 XGB 内存 · 推荐 YGB"
- [ ] 滑块选项不超过系统内存的 70%
- [ ] 用户手动调整后重启保持用户选择
- [ ] 系统内存检测失败时回退到默认 4GB
- [ ] 无 TypeScript 编译错误
- [ ] CI 编译通过

---

> **触发下一阶段**: 本 spec 获批后，自动进入 tasks.md 任务拆解和 checklist.md 验证清单编写。P1 为最低优先级，全部通过后整个项目完成。
