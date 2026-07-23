# P1 Tasks: 内存智能推荐

## 任务概览

共 4 个任务，后端 2 个 + 前端 1 个 + 集成验证 1 个。

## T1: 后端 — 推荐算法 + 首次安装自动设置 [后端层]

**文件**: `src/Baihe.Host/Services/SettingsService.cs` (修改)

**内容**:
1. 新增 `GetTotalPhysicalMemoryMB()` 静态方法:
   - 使用 `Microsoft.VisualBasic.Devices.ComputerInfo.TotalPhysicalMemory`
   - 转换为 MB（整数）
   - 异常时返回 8192 (8GB 默认值)

2. 新增 `CalculateRecommendedMemory(int totalMB)` 公共静态方法:
   - `half = totalMB * 0.5`
   - `reserved = totalMB - 4096`
   - `recommended = min(half, reserved, 16384)`
   - `return max(recommended, 2048)`

3. 修改 `GetAsync()` 中 settings.json 不存在时的逻辑:
   - 当前: `_cached = new LauncherSettings();`
   - 改为: `_cached = new LauncherSettings { MemoryMB = CalculateRecommendedMemory(GetTotalPhysicalMemoryMB()) };`

**依赖**: 无
**验证**: 方法逻辑正确，异常处理完善

## T2: 后端 — IPC 命令注册 [后端层]

**文件**: `src/Baihe.Host/MainWindow.xaml.cs` (修改)

**内容**:
在 IPC 注册区域添加 `system.memory` 命令:
```csharp
_ipcRouter.Register("system.memory", async _ =>
{
    var totalMB = SettingsService.GetTotalPhysicalMemoryMB();
    var recommendedMB = SettingsService.CalculateRecommendedMemory(totalMB);
    return new
    {
        totalMB,
        totalGB = totalMB / 1024,
        recommendedMB,
        recommendedGB = recommendedMB / 1024
    };
});
```

**依赖**: T1
**验证**: IPC 返回正确格式的 JSON

## T3: 前端 — 设置页内存区域改造 [前端层]

**文件**: `src/Baihe.UI/src/pages/Settings.svelte` (修改)

**内容**:
1. 新增 `SystemMemory` 接口和状态变量:
   ```typescript
   interface SystemMemory {
     totalMB: number
     totalGB: number
     recommendedMB: number
     recommendedGB: number
   }
   let systemMemory = $state<SystemMemory | null>(null)
   ```

2. 新增 `loadSystemMemory()` 函数，调用 `ipc('system.memory')`

3. 将 `memoryOptions` 从常量改为 `$derived`:
   ```typescript
   const memoryOptions = $derived(
     systemMemory
       ? generateMemoryOptions(systemMemory.totalGB)
       : [2, 3, 4, 6, 8, 12, 16]
   )
   ```

4. 新增 `generateMemoryOptions(totalGB)` 函数:
   - 基础选项: [2, 3, 4, 6, 8, 12, 16]
   - 过滤掉超过 `totalGB * 0.7` 的选项
   - 确保推荐值在选项中（不在则添加）
   - 排序去重

5. 修改提示文字:
   - 当前: `建议分配 2-4 GB`
   - 改为动态: `systemMemory ? '检测到 {totalGB}GB 内存 · 推荐 {recommendedGB}GB' : '建议分配 2-4 GB'`
   - 保留 `{settingsSaving ? ' · 保存中...' : ''}` 后缀

6. 组件挂载时调用 `loadSystemMemory()`（与 `loadSettings()` 并行）

**依赖**: T2
**验证**: 设置页显示动态内存信息

## T4: 集成验证 [集成层]

**内容**:
- 前端 `pnpm build` 无错误
- CI `dotnet build` 编译通过
- 检查代码一致性

**依赖**: T1-T3 全部完成
**验证**: 编译通过

## 并行执行策略

```
T1 ─ T2 ─ T3 (串行依赖)
         └── T4 (集成)
```

T1 是后端基础，T2 依赖 T1，T3 依赖 T2。无法并行，需串行执行。

---

> **触发下一阶段**: 所有任务完成后，对照 checklist.md 逐项验证，然后整体编译验证。全部通过后 P1 完成。
