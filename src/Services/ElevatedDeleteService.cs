using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Text;
using System.Text.Json;
using RightMgr.Models;

namespace RightMgr.Services;

public static class ElevatedDeleteService
{
    private const string DeleteItemArgument = "--delete-item";
    private const string RestoreItemArgument = "--restore-item";

    public static bool TryRunPendingDeletes(string[] args, out string? error)
    {
        error = null;
        var payloads = EnumerateDeletePayloads(args).ToList();
        if (payloads.Count == 0)
            return true;

        foreach (var payload in payloads)
        {
            var item = DecodeItem(payload);
            if (item == null)
            {
                error = "删除参数无效，无法恢复待删除项。";
                return false;
            }

            try
            {
                RegistryRecycleBinService.BackupAndDelete(item);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        return true;
    }

    public static bool TryRunPendingRestores(string[] args, out string? error)
    {
        error = null;
        foreach (var recordId in EnumerateArguments(args, RestoreItemArgument))
        {
            try
            {
                RegistryRecycleBinService.Restore(recordId);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
        return true;
    }

    public static void RestartElevatedForDelete(ContextMenuItemInfo item)
    {
        var exePath = Environment.ProcessPath
                      ?? Process.GetCurrentProcess().MainModule?.FileName
                      ?? throw new InvalidOperationException("无法定位当前程序路径");

        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"{DeleteItemArgument} {QuoteArgument(EncodeItem(item))}",
            Verb = "runas",
            UseShellExecute = true
        });
    }

    public static void RestartElevatedForRestore(string recordId)
    {
        var exePath = Environment.ProcessPath
                      ?? Process.GetCurrentProcess().MainModule?.FileName
                      ?? throw new InvalidOperationException("无法定位当前程序路径");
        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"{RestoreItemArgument} {QuoteArgument(recordId)}",
            Verb = "runas",
            UseShellExecute = true
        });
    }

    public static bool IsPermissionFailure(Exception ex)
    {
        return ex is UnauthorizedAccessException
               || ex is SecurityException
               || ex is Win32Exception { NativeErrorCode: 5 }
               || ex.HResult == unchecked((int)0x80070005);
    }

    public static string EncodeItem(ContextMenuItemInfo item)
    {
        var json = JsonSerializer.Serialize(item);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static ContextMenuItemInfo? DecodeItem(string payload)
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            return JsonSerializer.Deserialize<ContextMenuItemInfo>(json);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateDeletePayloads(string[] args)
    {
        return EnumerateArguments(args, DeleteItemArgument);
    }

    private static IEnumerable<string> EnumerateArguments(string[] args, string argumentName)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith($"{argumentName}=", StringComparison.OrdinalIgnoreCase))
            {
                yield return arg[(argumentName.Length + 1)..];
                continue;
            }

            if (arg.Equals(argumentName, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                yield return args[++i];
        }
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}
