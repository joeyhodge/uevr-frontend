using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace UEVR {
    internal static class EarlyShaderCapture {
        private const uint CreateSuspended = 0x00000004;
        private const uint CreateUnicodeEnvironment = 0x00000400;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo {
            public uint cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public uint dwX;
            public uint dwY;
            public uint dwXSize;
            public uint dwYSize;
            public uint dwXCountChars;
            public uint dwYCountChars;
            public uint dwFillAttribute;
            public uint dwFlags;
            public ushort wShowWindow;
            public ushort cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RegistryStatus {
            public uint Magic;
            public uint Size;
            public uint AbiVersion;
            public uint State;
            public uint ProcessId;
            public uint HooksActive;
            public ulong Records;
            public ulong GraphicsPipelines;
            public ulong ComputePipelines;
            public ulong PipelineStreams;
            public ulong UniqueShaders;
            public ulong RetainedBytes;
            public ulong DroppedRecords;
            public ulong DroppedShaderBytes;
            public ulong LastSequence;
            public uint LastError;
            public uint Reserved;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcessW(
            string? applicationName,
            string commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string? currentDirectory,
            ref StartupInfo startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        internal static bool ArmAndLaunch(string executablePath, out Process? process, out string status) {
            process = null;
            status = "";

            var fullPath = Path.GetFullPath(executablePath);
            if (!File.Exists(fullPath) || !fullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
                status = "Select an exact 64-bit game executable first.";
                return false;
            }

            var startupInfo = new StartupInfo {
                cb = (uint)Marshal.SizeOf<StartupInfo>()
            };

            if (!CreateProcessW(
                    fullPath,
                    $"\"{fullPath}\"",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CreateSuspended | CreateUnicodeEnvironment,
                    IntPtr.Zero,
                    Path.GetDirectoryName(fullPath),
                    ref startupInfo,
                    out var processInformation)) {
                status = $"CreateProcess failed ({Marshal.GetLastWin32Error()}).";
                return false;
            }

            var resume = false;
            try {
                if (!Injector.InjectDll((int)processInformation.dwProcessId, "UEVRShaderRegistryBootstrap.dll", out _)) {
                    status = "The startup shader helper could not be injected; the suspended game was stopped.";
                    return false;
                }

                // DllMain starts a tiny worker which publishes this mapping before the game is resumed.
                RegistryStatus helperStatus = default;
                var mappingSeen = false;
                var helperArmed = false;
                for (var i = 0; i < 500; ++i) {
                    if (TryReadStatus((int)processInformation.dwProcessId, out helperStatus)) {
                        mappingSeen = true;
                        if (helperStatus.AbiVersion != 1 || helperStatus.State == 5) {
                            break;
                        }
                        if (helperStatus.State == 2 && helperStatus.HooksActive != 0) {
                            helperArmed = true;
                            break;
                        }
                    }
                    Thread.Sleep(10);
                }

                if (!helperArmed) {
                    status = mappingSeen
                        ? $"Shader helper failed to arm (state {helperStatus.State}, error {helperStatus.LastError})."
                        : "Shader helper did not publish readiness before timeout.";
                    return false;
                }

                if (ResumeThread(processInformation.hThread) == uint.MaxValue) {
                    status = $"ResumeThread failed ({Marshal.GetLastWin32Error()}).";
                    return false;
                }

                resume = true;
                process = Process.GetProcessById((int)processInformation.dwProcessId);
                status = "Armed before launch; inject UEVR normally when the game is ready.";
                return true;
            } finally {
                if (!resume) {
                    TerminateProcess(processInformation.hProcess, 1);
                }
                CloseHandle(processInformation.hThread);
                CloseHandle(processInformation.hProcess);
            }
        }

        internal static bool TryReadStatus(int processId, out RegistryStatus status) {
            status = default;
            try {
                using var mapping = MemoryMappedFile.OpenExisting(
                    $"Local\\UEVRShaderRegistry-{processId}", MemoryMappedFileRights.Read);
                using var accessor = mapping.CreateViewAccessor(0, Marshal.SizeOf<RegistryStatus>(), MemoryMappedFileAccess.Read);
                accessor.Read(0, out status);
                return status.Magic == 0x52534855 && status.ProcessId == (uint)processId;
            } catch (FileNotFoundException) {
                return false;
            } catch (UnauthorizedAccessException) {
                return false;
            }
        }

        internal static bool PrepareForBackendInjection(int processId, out string statusText) {
            statusText = "";
            if (!TryReadStatus(processId, out var status)) {
                statusText = "The startup shader helper is no longer available.";
                return false;
            }

            if (status.HooksActive == 0) {
                statusText = $"Startup capture ready: {status.Records} PSOs imported; helper hooks are inactive.";
                return true;
            }

            IntPtr helperBase = IntPtr.Zero;
            try {
                using var process = Process.GetProcessById(processId);
                foreach (ProcessModule module in process.Modules) {
                    if (string.Equals(
                            module.ModuleName,
                            "UEVRShaderRegistryBootstrap.dll",
                            StringComparison.OrdinalIgnoreCase)) {
                        helperBase = module.BaseAddress;
                        break;
                    }
                }
            } catch {
                statusText = "Could not locate the startup shader helper in the target process.";
                return false;
            }

            if (helperBase == IntPtr.Zero ||
                !Injector.CallFunctionNoArgsWithoutDllMain(
                    processId,
                    "UEVRShaderRegistryBootstrap.dll",
                    helperBase,
                    "UEVRShaderRegistry_Disable")) {
                statusText = "Could not hand D3D12 hook ownership back before UEVR injection.";
                return false;
            }

            // The helper removes its creation hooks and drains in-flight calls
            // before publishing HooksActive=0.
            for (var i = 0; i < 250; ++i) {
                if (TryReadStatus(processId, out status) && status.HooksActive == 0) {
                    statusText = $"Startup capture ready: {status.Records} PSOs captured; injecting UEVR with clean D3D12 hooks.";
                    return true;
                }
                Thread.Sleep(10);
            }

            statusText = "Timed out waiting for the startup shader helper to release D3D12 hooks.";
            return false;
        }
    }
}
