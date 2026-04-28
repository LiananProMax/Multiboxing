using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace KeyMouseSyncReplica;

public sealed class DmPlugin : IDisposable
{
    private const string ProgId = "dm.dmsoft";
    private static readonly Guid DmClsid = new("26037A0E-7CBD-4FFF-9C63-56F2D0770214");
    private static readonly Guid IidClassFactory = new("00000001-0000-0000-C000-000000000046");
    private static readonly Guid IidDispatch = new("00020400-0000-0000-C000-000000000046");
    private readonly object _instance;
    private readonly IntPtr _libraryHandle;

    private DmPlugin(object instance, string creationMode, IntPtr libraryHandle = default)
    {
        _instance = instance;
        CreationMode = creationMode;
        _libraryHandle = libraryHandle;
    }

    public string CreationMode { get; }

    public static bool TryCreate(string dllPath, out DmPlugin? plugin, out string error)
    {
        plugin = null;
        error = string.Empty;

        if (TryCreateFromDll(dllPath, out plugin, out var dllError))
        {
            return true;
        }

        if (TryCreateFromRegistry(out plugin, out var registryError))
        {
            return true;
        }

        error = $"初始化 dm 插件失败。\r\nDLL 直加载: {dllError}\r\n注册表 COM: {registryError}";
        return false;
    }

    public static bool TryCreate(out DmPlugin? plugin, out string error)
    {
        return TryCreateFromRegistry(out plugin, out error);
    }

    private static bool TryCreateFromRegistry(out DmPlugin? plugin, out string error)
    {
        plugin = null;
        error = string.Empty;

        try
        {
            var type = Type.GetTypeFromProgID(ProgId);
            if (type == null)
            {
                error = "未找到 dm.dmsoft COM 组件。请先注册所选 dm DLL，且程序必须以 x86 运行。";
                return false;
            }

            var instance = Activator.CreateInstance(type);
            if (instance == null)
            {
                error = "创建 dm.dmsoft 对象失败。";
                return false;
            }

            plugin = new DmPlugin(instance, "RegistryCOM");
            return true;
        }
        catch (COMException ex)
        {
            error = $"创建 dm COM 对象失败：{ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            error = $"初始化 dm 插件失败：{ex.Message}";
            return false;
        }
    }

    private static bool TryCreateFromDll(string dllPath, out DmPlugin? plugin, out string error)
    {
        plugin = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
        {
            error = $"dm DLL 不存在：{dllPath}";
            return false;
        }

        var module = IntPtr.Zero;
        try
        {
            module = LoadLibrary(dllPath);
            if (module == IntPtr.Zero)
            {
                error = $"LoadLibrary 失败，Win32={Marshal.GetLastWin32Error()}";
                return false;
            }

            var proc = GetProcAddress(module, "DllGetClassObject");
            if (proc == IntPtr.Zero)
            {
                error = $"未导出 DllGetClassObject，Win32={Marshal.GetLastWin32Error()}";
                FreeLibrary(module);
                return false;
            }

            var getClassObject = Marshal.GetDelegateForFunctionPointer<DllGetClassObjectDelegate>(proc);
            var clsid = DmClsid;
            var iidClassFactory = IidClassFactory;
            var hr = getClassObject(ref clsid, ref iidClassFactory, out var factoryPtr);
            if (hr < 0 || factoryPtr == IntPtr.Zero)
            {
                error = $"DllGetClassObject 失败，HRESULT=0x{hr:X8}";
                FreeLibrary(module);
                return false;
            }

            var factory = (IClassFactory)Marshal.GetObjectForIUnknown(factoryPtr);
            try
            {
                var iidDispatch = IidDispatch;
                hr = factory.CreateInstance(IntPtr.Zero, ref iidDispatch, out var instance);
                if (hr < 0 || instance == null)
                {
                    error = $"IClassFactory.CreateInstance 失败，HRESULT=0x{hr:X8}";
                    FreeLibrary(module);
                    return false;
                }

                plugin = new DmPlugin(instance, $"DirectDll:{Path.GetFileName(dllPath)}", module);
                module = IntPtr.Zero;
                return true;
            }
            finally
            {
                Marshal.FinalReleaseComObject(factory);
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            if (module != IntPtr.Zero)
            {
                FreeLibrary(module);
            }

            return false;
        }
    }

    public static bool RegisterDll(string dllPath, bool elevated, out string message)
    {
        if (!File.Exists(dllPath))
        {
            message = $"dm DLL 不存在：{dllPath}";
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "regsvr32.exe",
                Arguments = $"/s \"{dllPath}\"",
                UseShellExecute = elevated,
                CreateNoWindow = true
            };

            if (elevated)
            {
                startInfo.Verb = "runas";
            }

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                message = "无法启动 regsvr32。";
                return false;
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                message = $"regsvr32 返回错误码 {process.ExitCode}。请确认 DLL 位数与管理员权限。";
                return false;
            }

            message = "dm DLL 注册完成。";
            return true;
        }
        catch (Exception ex)
        {
            message = $"注册 dm DLL 失败：{ex.Message}";
            return false;
        }
    }

    public static void TryRegisterSelectedDll(string dllPath)
    {
        // The original program creates dm through YunDm.fne with a selected DLL path.
        // In C# COM mode the closest equivalent is ensuring that the selected DLL is the registered dm.dmsoft.
        RegisterDll(dllPath, elevated: false, out _);
    }

    public string Version()
    {
        try
        {
            dynamic dm = _instance;
            return Convert.ToString(dm.Ver(), CultureInfo.InvariantCulture) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public int LastError()
    {
        try
        {
            dynamic dm = _instance;
            return ToInt32(dm.GetLastError());
        }
        catch
        {
            return int.MinValue;
        }
    }

    public string Initialize(string basePath)
    {
        var steps = new List<string>();

        try
        {
            steps.Add($"SetShowErrorMsg={SetShowErrorMsg(false)}");
        }
        catch (Exception ex)
        {
            steps.Add($"SetShowErrorMsg=失败({ex.Message})");
        }

        try
        {
            steps.Add($"SetPath={SetPath(basePath)}");
        }
        catch (Exception ex)
        {
            steps.Add($"SetPath=失败({ex.Message})");
        }

        try
        {
            steps.Add($"Path={GetPath()}");
        }
        catch (Exception ex)
        {
            steps.Add($"Path=失败({ex.Message})");
        }

        return string.Join(", ", steps);
    }

    public int BindWindow(IntPtr hwnd, string display, string mouse, string keypad, int mode)
    {
        dynamic dm = _instance;
        return ToInt32(dm.BindWindow(hwnd.ToInt32(), display, mouse, keypad, mode));
    }

    public int BindWindowEx(IntPtr hwnd, string display, string mouse, string keypad, string publicMode, int mode)
    {
        dynamic dm = _instance;
        return ToInt32(dm.BindWindowEx(hwnd.ToInt32(), display, mouse, keypad, publicMode, mode));
    }

    public int UnBindWindow()
    {
        dynamic dm = _instance;
        return ToInt32(dm.UnBindWindow());
    }

    public int SetWindowState(IntPtr hwnd, int state)
    {
        dynamic dm = _instance;
        return ToInt32(dm.SetWindowState(hwnd.ToInt32(), state));
    }

    public int SetPath(string path)
    {
        dynamic dm = _instance;
        return ToInt32(dm.SetPath(path));
    }

    public string GetPath()
    {
        dynamic dm = _instance;
        return Convert.ToString(dm.GetPath(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public int SetShowErrorMsg(bool enabled)
    {
        dynamic dm = _instance;
        return ToInt32(dm.SetShowErrorMsg(enabled ? 1 : 0));
    }

    public int ForceUnBindWindow(IntPtr hwnd)
    {
        return ToInt32(InvokeWithFallback("ForceUnBindWindow", new object[] { hwnd.ToInt32() }, Array.Empty<object>()));
    }

    public int SwitchBindWindow(IntPtr hwnd)
    {
        return ToInt32(InvokeWithFallback("SwitchBindWindow", new object[] { hwnd.ToInt32() }, Array.Empty<object>()));
    }

    public int EnableKeypadSync(bool enabled)
    {
        return ToInt32(InvokeWithFallback("EnableKeypadSync", new object[] { enabled ? 1 : 0, 0 }, new object[] { enabled ? 1 : 0 }));
    }

    public int EnableMouseSync(bool enabled)
    {
        return ToInt32(InvokeWithFallback("EnableMouseSync", new object[] { enabled ? 1 : 0, 0 }, new object[] { enabled ? 1 : 0 }));
    }

    public void Dispose()
    {
        if (Marshal.IsComObject(_instance))
        {
            Marshal.FinalReleaseComObject(_instance);
        }

        if (_libraryHandle != IntPtr.Zero)
        {
            FreeLibrary(_libraryHandle);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DllGetClassObjectDelegate(ref Guid clsid, ref Guid iid, out IntPtr classFactory);

    [ComImport]
    [Guid("00000001-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(IntPtr outer, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object? instance);

        [PreserveSig]
        int LockServer(bool lockServer);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string fileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    private object? InvokeWithFallback(string name, params object[][] argumentSets)
    {
        Exception? lastError = null;
        foreach (var args in argumentSets)
        {
            try
            {
                return Invoke(name, args);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException($"调用 dm.{name} 失败。", lastError);
    }

    private object? Invoke(string name, params object[] args)
    {
        return _instance.GetType().InvokeMember(
            name,
            BindingFlags.InvokeMethod,
            binder: null,
            target: _instance,
            args: args,
            culture: CultureInfo.InvariantCulture);
    }

    private static int ToInt32(object? value)
    {
        if (value == null)
        {
            return 0;
        }

        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }
}
