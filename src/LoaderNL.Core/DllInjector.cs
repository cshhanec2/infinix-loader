using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LoaderNL.Core.Services;

public sealed class DllInjector
{
    private readonly LogService _logService;

    public DllInjector(LogService logService)
    {
        _logService = logService;
    }

    /// <summary>
    /// Ожидает появление процесса игры в течение заданного времени.
    /// </summary>
    public async Task<bool> WaitForProcessAsync(string processName, int timeoutSeconds)
    {
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        while (DateTime.UtcNow - startTime < timeout)
        {
            var processes = Process.GetProcessesByName(processName);
            if (processes.Length > 0)
            {
                return true;
            }

            await Task.Delay(1000);
        }

        return false;
    }

    /// <summary>
    /// Ожидает загрузку конкретного игрового модуля (например, client.dll), чтобы тайминг инжекта был идеальным.
    /// </summary>
    public async Task<bool> WaitForGameModuleAsync(int processId, string moduleName, int timeoutSeconds)
    {
        return await Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutSeconds * 1000)
            {
                try
                {
                    var targetProcess = Process.GetProcessById(processId);
                    foreach (ProcessModule module in targetProcess.Modules)
                    {
                        if (module.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    // Процесс может быть еще на этапе инициализации, игнорируем исключения доступа
                }

                Thread.Sleep(500);
            }
            return false;
        });
    }

    /// <summary>
    /// Чистый стандартный инжект для Neverlose (nl_cache.dll)
    /// </summary>
    public async Task<bool> InjectEmbeddedDllAsync(string processName, string resourceName)
    {
        return await Task.Run(() =>
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    _logService.Error($"[INJECTOR] Ресурс '{resourceName}' НЕ найден в сборке!");
                    return false;
                }

                // Используем нейтральную папку в ProgramData, исключая проблемы с кириллицей в путях (%TEMP%)
                string workFolder = @"C:\ProgramData\LoaderNL";
                if (!Directory.Exists(workFolder))
                {
                    Directory.CreateDirectory(workFolder);
                }

                string tempDllPath = Path.Combine(workFolder, "nl_cache.dll");

                if (File.Exists(tempDllPath))
                {
                    try { File.Delete(tempDllPath); } catch { }
                }

                using (var fileStream = File.Create(tempDllPath))
                {
                    stream.CopyTo(fileStream);
                }

                _logService.Info($"[INJECTOR] Neverlose DLL распакована по пути: {tempDllPath}");
                return InjectStandardDll(processName, tempDllPath);
            }
            catch (Exception ex)
            {
                _logService.Error($"[INJECTOR] Ошибка распаковки DLL: {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// Специальный инжект для Skeet (skeet.dll) с предварительным резервом 0x43310000
    /// </summary>
    public async Task<bool> InjectSkeetDllAsync(string processName, string resourceName)
    {
        return await Task.Run(() =>
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    _logService.Error($"[INJECTOR] Ресурс '{resourceName}' НЕ найден в сборке!");
                    return false;
                }

                string workFolder = @"C:\ProgramData\LoaderNL";
                if (!Directory.Exists(workFolder))
                {
                    Directory.CreateDirectory(workFolder);
                }

                string tempDllPath = Path.Combine(workFolder, "skeet.dll");

                if (File.Exists(tempDllPath))
                {
                    try { File.Delete(tempDllPath); } catch { }
                }

                using (var fileStream = File.Create(tempDllPath))
                {
                    stream.CopyTo(fileStream);
                }

                _logService.Info($"[INJECTOR] Skeet DLL распакована по пути: {tempDllPath}");
                return InjectSkeetWithPreallocatedMemory(processName, tempDllPath);
            }
            catch (Exception ex)
            {
                _logService.Error($"[INJECTOR] Ошибка распаковки Skeet DLL: {ex.Message}");
                return false;
            }
        });
    }

    private bool InjectStandardDll(string processName, string dllPath)
    {
        var processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
        {
            _logService.Error($"[INJECTOR] Процесс {processName} не найден.");
            return false;
        }

        var process = processes[0];
        IntPtr hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, process.Id);

        if (hProcess == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            _logService.Error($"[INJECTOR] Не удалось открыть процесс. Win32 Error: {err}. Запусти от АДМИНА!");
            return false;
        }

        try
        {
            IntPtr loadLibraryAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
            if (loadLibraryAddr == IntPtr.Zero)
            {
                _logService.Error("[INJECTOR] Не найден адрес LoadLibraryW.");
                return false;
            }

            byte[] bytes = Encoding.Unicode.GetBytes(dllPath + "\0");
            uint size = (uint)bytes.Length;

            IntPtr allocMemAddress = VirtualAllocEx(hProcess, IntPtr.Zero, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

            if (allocMemAddress == IntPtr.Zero)
            {
                _logService.Error("[INJECTOR] Не удалось выделить память в процессе (VirtualAllocEx).");
                return false;
            }

            bool writeResult = WriteProcessMemory(hProcess, allocMemAddress, bytes, size, out _);
            if (!writeResult)
            {
                _logService.Error("[INJECTOR] Ошибка записи памяти (WriteProcessMemory).");
                return false;
            }

            IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0, IntPtr.Zero);
            if (hThread == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                _logService.Error($"[INJECTOR] Ошибка создания потока (CreateRemoteThread). Win32 Error: {err}");
                return false;
            }

            _logService.Info("[INJECTOR] Поток создан. Ожидание ответа от LoadLibraryW...");

            uint waitResult = WaitForSingleObject(hThread, 5000);

            if (waitResult == WAIT_TIMEOUT)
            {
                _logService.Error("[INJECTOR] Поток завис (таймаут 5 сек).");
                CloseHandle(hThread);
                return false;
            }

            if (GetExitCodeThread(hThread, out uint exitCode))
            {
                if (exitCode == 0)
                {
                    _logService.Error("[INJECTOR] LoadLibraryW вернула NULL (0x0)! Проверь параметры запуска и антивирус.");
                    CloseHandle(hThread);
                    return false;
                }

                _logService.Info($"[INJECTOR] DLL успешно загружена по адресу: 0x{exitCode:X8}");
                CloseHandle(hThread);
                return true;
            }

            CloseHandle(hThread);
            return true;
        }
        catch (Exception ex)
        {
            _logService.Error($"[INJECTOR] Исключение: {ex.Message}");
            return false;
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    private bool InjectSkeetWithPreallocatedMemory(string processName, string dllPath)
    {
        var processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
        {
            _logService.Error($"[INJECTOR] Процесс {processName} не найден.");
            return false;
        }

        var process = processes[0];
        IntPtr hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, process.Id);

        if (hProcess == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            _logService.Error($"[INJECTOR] Не удалось открыть процесс CS:GO. Win32 Error: {err}. Запусти от АДМИНА!");
            return false;
        }

        try
        {
            IntPtr fixedAddress = new IntPtr(0x43310000);
            uint payloadSize = 0x2FC000;

            VirtualFreeEx(hProcess, fixedAddress, 0, MEM_RELEASE);

            IntPtr reservedMem = VirtualAllocEx(hProcess, fixedAddress, payloadSize, MEM_RESERVE, PAGE_EXECUTE_READWRITE);
            if (reservedMem != IntPtr.Zero)
            {
                VirtualAllocEx(hProcess, fixedAddress, payloadSize, MEM_COMMIT, PAGE_EXECUTE_READWRITE);
            }

            IntPtr loadLibraryAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
            if (loadLibraryAddr == IntPtr.Zero)
            {
                _logService.Error("[INJECTOR] Не найден адрес LoadLibraryW.");
                return false;
            }

            byte[] bytes = Encoding.Unicode.GetBytes(dllPath + "\0");
            uint size = (uint)bytes.Length;

            IntPtr allocMemAddress = VirtualAllocEx(hProcess, IntPtr.Zero, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (allocMemAddress == IntPtr.Zero)
            {
                _logService.Error("[INJECTOR] Не удалось выделить память под путь DLL.");
                return false;
            }

            WriteProcessMemory(hProcess, allocMemAddress, bytes, size, out _);

            IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0, IntPtr.Zero);
            if (hThread == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                _logService.Error($"[INJECTOR] Ошибка CreateRemoteThread для Skeet. Win32 Error: {err}");
                return false;
            }

            WaitForSingleObject(hThread, 5000);

            if (GetExitCodeThread(hThread, out uint exitCode))
            {
                if (exitCode == 0)
                {
                    _logService.Error("[INJECTOR] Skeet LoadLibraryW вернул NULL (0x0)!");
                    CloseHandle(hThread);
                    return false;
                }

                _logService.Info($"[INJECTOR] Skeet успешно инжектирован! Адрес: 0x{exitCode:X8}");
                CloseHandle(hThread);
                return true;
            }

            CloseHandle(hThread);
            return true;
        }
        catch (Exception ex)
        {
            _logService.Error($"[INJECTOR] Ошибка инжекта Skeet: {ex.Message}");
            return false;
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    #region WinAPI Imports
    private const uint PROCESS_ALL_ACCESS = 0x001F0FFF;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;
    private const uint WAIT_TIMEOUT = 0x00000102;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpTargetHandle, uint dwCreationFlags, IntPtr lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
    #endregion
}
