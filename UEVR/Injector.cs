using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Diagnostics;
using System.Reflection;

namespace UEVR {
    class Injector {
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        public static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        // Inject the DLL into the target process
        // dllPath is local filename, relative to EXE.
        public static bool InjectDll(int processId, string dllPath, out IntPtr dllBase) {
            string originalPath = dllPath;

            try {
                var exeDirectory = AppContext.BaseDirectory;

                if (exeDirectory != null) {
                    var newPath = Path.Combine(exeDirectory, dllPath);

                    if (System.IO.File.Exists(newPath)) {
                        dllPath = Path.Combine(exeDirectory, dllPath);
                    }
                }
            } catch (Exception) {
            }

            if (!System.IO.File.Exists(dllPath)) {
                MessageBox.Show($"{originalPath} does not appear to exist! Check if any anti-virus software has deleted the file. Reinstall UEVR if necessary.\n\nBaseDirectory: {AppContext.BaseDirectory}");
            }

            dllBase = IntPtr.Zero;

            string fullPath = Path.GetFullPath(dllPath);

            // Open the target process with the necessary access
            IntPtr processHandle = OpenProcess(0x1F0FFF, false, processId);

            if (processHandle == IntPtr.Zero) {
                MessageBox.Show("Could not open a handle to the target process.\nYou may need to start this program as an administrator, or the process may be protected.");
                return false;
            }

            // Get the address of the LoadLibrary function
            IntPtr loadLibraryAddress = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");

            if (loadLibraryAddress == IntPtr.Zero) {
                CloseHandle(processHandle);
                MessageBox.Show("Could not obtain LoadLibraryW address in the target process.");
                return false;
            }

            // Allocate memory in the target process for the DLL path
            var bytes = Encoding.Unicode.GetBytes(fullPath + "\0");
            IntPtr dllPathAddress = VirtualAllocEx(processHandle, IntPtr.Zero, (uint)bytes.Length, 0x3000, 0x04);

            if (dllPathAddress == IntPtr.Zero) {
                CloseHandle(processHandle);
                MessageBox.Show("Failed to allocate memory in the target process.");
                return false;
            }

            // Write the DLL path in UTF-16
            int bytesWritten = 0;
            if (!WriteProcessMemory(processHandle, dllPathAddress, bytes, (uint)bytes.Length, out bytesWritten) ||
                bytesWritten != bytes.Length) {
                VirtualFreeEx(processHandle, dllPathAddress, UIntPtr.Zero, 0x8000);
                CloseHandle(processHandle);
                MessageBox.Show("Failed to write the DLL path into the target process.");
                return false;
            }

            // Create a remote thread in the target process that calls LoadLibrary with the DLL path
            IntPtr threadHandle = CreateRemoteThread(processHandle, IntPtr.Zero, 0, loadLibraryAddress, dllPathAddress, 0, IntPtr.Zero);

            if (threadHandle == IntPtr.Zero) {
                VirtualFreeEx(processHandle, dllPathAddress, UIntPtr.Zero, 0x8000);
                CloseHandle(processHandle);
                MessageBox.Show("Failed to create remote thread in the target processs.");
                return false;
            }

            var waitResult = WaitForSingleObject(threadHandle, 10000);
            var remoteLoadSucceeded = waitResult == 0 && GetExitCodeThread(threadHandle, out var remoteExitCode) &&
                remoteExitCode != 0;

            // Get base of DLL that was just injected
            if (waitResult == 0) try {
                using var p = Process.GetProcessById(processId);
                foreach (ProcessModule module in p.Modules) {
                    if (module.FileName != null &&
                        string.Equals(Path.GetFullPath(module.FileName), fullPath, StringComparison.OrdinalIgnoreCase)) {
                        dllBase = module.BaseAddress;
                        break;
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine($"Exception caught: {ex}");
                MessageBox.Show($"Exception while injecting: {ex}");
            }

            CloseHandle(threadHandle);
            // A timed-out remote thread may still be reading this path.
            // Leak the small allocation rather than introducing a target-process UAF.
            if (waitResult == 0) {
                VirtualFreeEx(processHandle, dllPathAddress, UIntPtr.Zero, 0x8000);
            }
            CloseHandle(processHandle);
            return remoteLoadSucceeded;
        }

        public static bool InjectDll(int processId, string dllPath) {
            IntPtr dummy;
            return InjectDll(processId, dllPath, out dummy);
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr LoadLibrary(string lpFileName);

        // FreeLibrary
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool FreeLibrary(IntPtr hModule);

        public static bool CallFunctionNoArgs(int processId, string dllPath, IntPtr dllBase, string functionName, bool wait = false) {
            IntPtr processHandle = OpenProcess(0x1F0FFF, false, processId);

            if (processHandle == IntPtr.Zero) {
                MessageBox.Show("Could not open a handle to the target process.\nYou may need to start this program as an administrator, or the process may be protected.");
                return false;
            }

            // We need to load the DLL into our own process temporarily as a workaround for GetProcAddress not working with remote DLLs
            IntPtr localDllHandle = LoadLibrary(dllPath);

            if (localDllHandle == IntPtr.Zero) {
                MessageBox.Show("Could not load the target DLL into our own process.");
                return false;
            }

            IntPtr localVa = GetProcAddress(localDllHandle, functionName);

            if (localVa == IntPtr.Zero) {
                MessageBox.Show("Could not obtain " + functionName + " address in our own process.");
                return false;
            }

            IntPtr rva = (IntPtr)(localVa.ToInt64() - localDllHandle.ToInt64());
            IntPtr functionAddress = (IntPtr)(dllBase.ToInt64() + rva.ToInt64());

            // Create a remote thread in the target process that calls the function
            IntPtr threadHandle = CreateRemoteThread(processHandle, IntPtr.Zero, 0, functionAddress, IntPtr.Zero, 0, IntPtr.Zero);

            if (threadHandle == IntPtr.Zero) {
                MessageBox.Show("Failed to create remote thread in the target processs.");
                return false;
            }

            if (wait) {
                WaitForSingleObject(threadHandle, 2000);
            }

            return true;
        }
    }
}
