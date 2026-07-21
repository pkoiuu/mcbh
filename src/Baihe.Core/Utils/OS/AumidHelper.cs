using Microsoft.Win32;

namespace Baihe.Core.Utils.OS;

public static class AumidHelper
{
    public const string Aumid = "SimpFun.Qihang";
    
    public static bool HasAumid()
    {
        using var key = Registry.CurrentUser.OpenSubKey(string.Concat(@"Software\Classes\AppUserModelId", Aumid));
        return key is not null;
    }
    
    public static void RegisterAumid()
    {
        // .NET 8 在正常情况下不可能返回 null，如果炸了不应该包住而是让他炸下去
        using var key = Registry.CurrentUser.CreateSubKey(string.Concat(@"Software\Classes\AppUserModelId", Aumid));
        key.SetValue("DisplayName", "启航");
        key.SetValue("IconUri", IconHelper.GetIconPath());
        key.SetValue("IconBackgroundColor", "FFDDDD");
    }

    public static void UnregisterAumid()
    {
        Registry.CurrentUser.DeleteSubKey(string.Concat(@"Software\Classes\AppUserModelId\", Aumid), false);
    }
}