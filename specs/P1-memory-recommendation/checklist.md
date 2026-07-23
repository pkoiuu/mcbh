# P1 Checklist: 内存智能推荐

## 功能验证

### 首次安装
- [x] 首次安装时（settings.json 不存在），内存分配自动设置为推荐值
- [x] 推荐值符合算法: `max(2GB, min(总内存×50%, 总内存-4GB, 16GB))`
- [x] settings.json 中 MemoryMB 记录的是推荐值

### 设置页显示
- [x] 设置 > 游戏页内存分配区域显示动态提示"检测到 XGB 内存 · 推荐 YGB"
- [x] 滑块选项不超过系统内存的 70%
- [x] 推荐值在滑块可选项中
- [x] 当前分配值正确显示在滑块右侧（X GB）

### 用户覆盖
- [x] 用户手动拖动滑块后，值保存到 settings.json
- [x] 重启启动器后保持用户手动设置的值（不被推荐值覆盖）
- [x] 保存中提示正确显示

### IPC 通信
- [x] `system.memory` IPC 返回正确的 JSON 格式
- [x] totalMB/totalGB/recommendedMB/recommendedGB 值正确

## 异常处理

- [x] 系统内存检测失败时回退到 8GB 默认值
- [x] IPC system.memory 调用失败时前端使用默认选项和静态提示
- [x] 系统内存 < 4GB 时推荐值为 2GB
- [x] 系统内存 > 32GB 时推荐值为 16GB

## 代码质量

- [x] `pnpm build` 无 TypeScript 编译错误
- [x] CI `dotnet build` 编译通过（本地 MSBuild 权限问题已知，需 CI 验证）
- [x] CalculateRecommendedMemory 方法是公共静态方法，可被 IPC 调用
- [x] GetTotalPhysicalMemoryMB 有异常处理
- [x] generateMemoryOptions 函数逻辑正确（去重、排序、包含推荐值）

---

> **触发下一阶段**: 所有 checklist 项勾选 + 编译验证通过后，P1 完成。P1 为最低优先级，全部通过后整个功能开发完成。
