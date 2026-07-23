// 存档管理服务 — 列出存档、备份存档为 zip、导入存档 zip

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace Baihe.Host.Services;

public static class SaveService
{
    /// <summary>获取 saves 目录路径</summary>
    private static string GetSavesDir()
    {
        var mcDir = InstanceService.GetMcDirectory();
        var savesDir = Path.Combine(mcDir, "saves");
        Directory.CreateDirectory(savesDir);
        return savesDir;
    }

    /// <summary>获取备份目录路径</summary>
    private static string GetBackupDir()
    {
        var mcDir = InstanceService.GetMcDirectory();
        var backupDir = Path.Combine(mcDir, "backups");
        Directory.CreateDirectory(backupDir);
        return backupDir;
    }

    /// <summary>列出所有存档</summary>
    public static Task<List<SaveInfo>> ListSaves()
    {
        var savesDir = GetSavesDir();
        var saves = new List<SaveInfo>();

        if (!Directory.Exists(savesDir))
            return Task.FromResult(saves);

        foreach (var dir in Directory.GetDirectories(savesDir))
        {
            var info = new DirectoryInfo(dir);
            var save = new SaveInfo
            {
                Name = info.Name,
                LastModified = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                FolderSize = GetDirectorySize(dir),
                SizeText = FormatSize(GetDirectorySize(dir)),
            };

            // 读取 level.dat 中的游戏模式等信息 (简化: 只检查文件是否存在)
            var levelDat = Path.Combine(dir, "level.dat");
            save.HasLevelData = File.Exists(levelDat);

            saves.Add(save);
        }

        // 按最后修改时间降序排列
        saves.Sort((a, b) => string.Compare(b.LastModified, a.LastModified, StringComparison.Ordinal));
        return Task.FromResult(saves);
    }

    /// <summary>备份存档为 zip</summary>
    public static Task<object> BackupSave(string saveName)
    {
        var savesDir = GetSavesDir();
        var backupDir = GetBackupDir();
        var savePath = Path.Combine(savesDir, saveName);

        if (!Directory.Exists(savePath))
            throw new DirectoryNotFoundException($"未找到存档: {saveName}");

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupFileName = $"{saveName}_{timestamp}.zip";
        var backupPath = Path.Combine(backupDir, backupFileName);

        ZipFile.CreateFromDirectory(savePath, backupPath, CompressionLevel.Optimal, false);

        var info = new FileInfo(backupPath);
        return Task.FromResult<object>(new
        {
            success = true,
            backupPath = backupPath,
            backupName = backupFileName,
            size = info.Length,
            sizeText = FormatSize(info.Length),
        });
    }

    /// <summary>导入存档 zip</summary>
    public static Task<object> ImportSave(string zipPath)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException($"未找到 zip 文件: {zipPath}");

        var savesDir = GetSavesDir();
        var tempDir = Path.Combine(Path.GetTempPath(), $"baihe_import_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, tempDir);

            // 查找包含 level.dat 的目录
            string? saveDir = null;
            var levelDat = Path.Combine(tempDir, "level.dat");
            if (File.Exists(levelDat))
            {
                // zip 根目录就是存档目录
                saveDir = tempDir;
            }
            else
            {
                // 在子目录中查找
                foreach (var dir in Directory.GetDirectories(tempDir))
                {
                    if (File.Exists(Path.Combine(dir, "level.dat")))
                    {
                        saveDir = dir;
                        break;
                    }
                }
            }

            if (saveDir == null)
                throw new InvalidOperationException("zip 文件中未找到有效的存档 (缺少 level.dat)");

            var saveName = new DirectoryInfo(saveDir).Name;
            var targetPath = Path.Combine(savesDir, saveName);

            // 如果目标目录已存在，添加后缀
            if (Directory.Exists(targetPath))
                targetPath = Path.Combine(savesDir, $"{saveName}_{DateTime.Now:yyyyMMdd_HHmmss}");

            // 移动存档目录
            CopyDirectory(saveDir, targetPath);

            return Task.FromResult<object>(new
            {
                success = true,
                saveName = Path.GetFileName(targetPath),
                message = $"存档 {Path.GetFileName(targetPath)} 导入成功",
            });
        }
        finally
        {
            // 清理临时目录
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    /// <summary>列出所有备份</summary>
    public static Task<List<BackupInfo>> ListBackups()
    {
        var backupDir = GetBackupDir();
        var backups = new List<BackupInfo>();

        if (!Directory.Exists(backupDir))
            return Task.FromResult(backups);

        foreach (var file in Directory.GetFiles(backupDir, "*.zip"))
        {
            var info = new FileInfo(file);
            backups.Add(new BackupInfo
            {
                FileName = info.Name,
                SizeText = FormatSize(info.Length),
                CreatedTime = info.CreationTime.ToString("yyyy-MM-dd HH:mm"),
            });
        }

        backups.Sort((a, b) => string.Compare(b.CreatedTime, a.CreatedTime, StringComparison.Ordinal));
        return Task.FromResult(backups);
    }

    /// <summary>删除备份</summary>
    public static Task DeleteBackup(string fileName)
    {
        var backupDir = GetBackupDir();
        var path = Path.Combine(backupDir, fileName);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    /// <summary>恢复备份</summary>
    public static Task<object> RestoreBackup(string backupFileName, string? saveName = null)
    {
        var backupDir = GetBackupDir();
        var backupPath = Path.Combine(backupDir, backupFileName);

        if (!File.Exists(backupPath))
            throw new FileNotFoundException($"未找到备份: {backupFileName}");

        var savesDir = GetSavesDir();
        var tempDir = Path.Combine(Path.GetTempPath(), $"baihe_restore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            ZipFile.ExtractToDirectory(backupPath, tempDir);

            // 推导存档名: 去掉 _yyyyMMdd_HHmmss 后缀
            var dirName = new DirectoryInfo(tempDir).GetDirectories().FirstOrDefault()?.Name
                          ?? new DirectoryInfo(tempDir).Name;
            if (string.IsNullOrEmpty(saveName))
            {
                saveName = dirName;
                // 去掉时间戳后缀
                var lastUnder = saveName.LastIndexOf('_');
                if (lastUnder > 0)
                    saveName = saveName.Substring(0, lastUnder);
            }

            var sourceDir = Directory.GetDirectories(tempDir).FirstOrDefault() ?? tempDir;
            var targetPath = Path.Combine(savesDir, saveName);

            // 如果目标已存在，先重命名
            if (Directory.Exists(targetPath))
            {
                var oldBackup = targetPath + "_old_" + DateTime.Now.ToString("yyyyMMddHHmmss");
                Directory.Move(targetPath, oldBackup);
            }

            CopyDirectory(sourceDir, targetPath);

            return Task.FromResult<object>(new
            {
                success = true,
                saveName = saveName,
                message = $"存档 {saveName} 恢复成功",
            });
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    /// <summary>计算目录大小</summary>
    private static long GetDirectorySize(string path)
    {
        return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
    }

    /// <summary>递归复制目录</summary>
    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    /// <summary>格式化文件大小</summary>
    private static string FormatSize(long bytes) => FormatHelper.FormatSize(bytes);
}

/// <summary>存档信息</summary>
public class SaveInfo
{
    public string Name { get; set; } = "";
    public string LastModified { get; set; } = "";
    public long FolderSize { get; set; }
    public string SizeText { get; set; } = "";
    public bool HasLevelData { get; set; }
}

/// <summary>备份信息</summary>
public class BackupInfo
{
    public string FileName { get; set; } = "";
    public string SizeText { get; set; } = "";
    public string CreatedTime { get; set; } = "";
}
