using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace nuone_tools
{
    public sealed class ConPtyRuntimeContext : IDisposable
    {
        internal ConPtyRuntimeContext(IntPtr pseudoConsoleHandle, FileStream inputWriter, FileStream outputReader)
        {
            PseudoConsoleHandle = pseudoConsoleHandle;
            InputWriter = inputWriter;
            OutputReader = outputReader;
        }

        internal IntPtr PseudoConsoleHandle { get; private set; }

        internal FileStream InputWriter { get; }

        internal FileStream OutputReader { get; }

        internal void Resize(short columns, short rows)
        {
            if (PseudoConsoleHandle == IntPtr.Zero)
            {
                return;
            }

            var result = TerminalConPtyNative.ResizePseudoConsole(PseudoConsoleHandle, new TerminalConPtyNative.COORD
            {
                X = columns,
                Y = rows,
            });
            if (result != 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }
        }

        public void Dispose()
        {
            try
            {
                InputWriter.Dispose();
            }
            catch
            {
            }

            try
            {
                OutputReader.Dispose();
            }
            catch
            {
            }

            if (PseudoConsoleHandle != IntPtr.Zero)
            {
                TerminalConPtyNative.ClosePseudoConsole(PseudoConsoleHandle);
                PseudoConsoleHandle = IntPtr.Zero;
            }
        }
    }

    internal static class TerminalConPtyNative
    {
        private const int EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        private const int HANDLE_FLAG_INHERIT = 0x00000001;
        private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

        internal static (ConPtyRuntimeContext Context, int ProcessId) CreatePseudoConsoleProcess(
            string fileName,
            string arguments,
            string workingDirectory,
            short columns,
            short rows)
        {
            var securityAttributes = new SECURITY_ATTRIBUTES
            {
                nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                bInheritHandle = true,
            };

            IntPtr pseudoConsoleInputRead = IntPtr.Zero;
            IntPtr appInputWrite = IntPtr.Zero;
            IntPtr appOutputRead = IntPtr.Zero;
            IntPtr pseudoConsoleOutputWrite = IntPtr.Zero;
            IntPtr pseudoConsoleHandle = IntPtr.Zero;
            IntPtr attributeListBuffer = IntPtr.Zero;
            IntPtr attributeList = IntPtr.Zero;
            PROCESS_INFORMATION processInformation = default;

            try
            {
                CreatePipeOrThrow(out pseudoConsoleInputRead, out appInputWrite, securityAttributes);
                CreatePipeOrThrow(out appOutputRead, out pseudoConsoleOutputWrite, securityAttributes);
                SetHandleInformationOrThrow(appInputWrite, HANDLE_FLAG_INHERIT, 0);
                SetHandleInformationOrThrow(appOutputRead, HANDLE_FLAG_INHERIT, 0);

                var createResult = CreatePseudoConsole(
                    new COORD { X = columns, Y = rows },
                    pseudoConsoleInputRead,
                    pseudoConsoleOutputWrite,
                    0,
                    out pseudoConsoleHandle);
                if (createResult != 0)
                {
                    Marshal.ThrowExceptionForHR(createResult);
                }

                CloseHandleSafe(ref pseudoConsoleInputRead);
                CloseHandleSafe(ref pseudoConsoleOutputWrite);

                var attributeListSize = IntPtr.Zero;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);
                attributeListBuffer = Marshal.AllocHGlobal(attributeListSize);
                attributeList = attributeListBuffer;
                if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                if (!UpdateProcThreadAttribute(
                        attributeList,
                        0,
                        (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                        pseudoConsoleHandle,
                        (IntPtr)IntPtr.Size,
                        IntPtr.Zero,
                        IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                var startupInfo = new STARTUPINFOEX();
                startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
                startupInfo.lpAttributeList = attributeList;

                var commandLine = new StringBuilder();
                commandLine.Append('"').Append(fileName).Append('"');
                if (!string.IsNullOrWhiteSpace(arguments))
                {
                    commandLine.Append(' ').Append(arguments);
                }

                if (!CreateProcessW(
                        null,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        EXTENDED_STARTUPINFO_PRESENT,
                        IntPtr.Zero,
                        workingDirectory,
                        ref startupInfo,
                        out processInformation))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                var inputWriter = new FileStream(new SafeFileHandle(appInputWrite, ownsHandle: true), FileAccess.Write, 0x1000, isAsync: false);
                var outputReader = new FileStream(new SafeFileHandle(appOutputRead, ownsHandle: true), FileAccess.Read, 0x1000, isAsync: false);
                appInputWrite = IntPtr.Zero;
                appOutputRead = IntPtr.Zero;

                return (new ConPtyRuntimeContext(pseudoConsoleHandle, inputWriter, outputReader), processInformation.dwProcessId);
            }
            catch
            {
                if (pseudoConsoleHandle != IntPtr.Zero)
                {
                    ClosePseudoConsole(pseudoConsoleHandle);
                }

                throw;
            }
            finally
            {
                if (attributeList != IntPtr.Zero)
                {
                    DeleteProcThreadAttributeList(attributeList);
                }

                if (attributeListBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(attributeListBuffer);
                }

                CloseHandleSafe(ref pseudoConsoleInputRead);
                CloseHandleSafe(ref pseudoConsoleOutputWrite);
                CloseHandleSafe(ref appInputWrite);
                CloseHandleSafe(ref appOutputRead);

                if (processInformation.hThread != IntPtr.Zero)
                {
                    CloseHandle(processInformation.hThread);
                }

                if (processInformation.hProcess != IntPtr.Zero)
                {
                    CloseHandle(processInformation.hProcess);
                }
            }
        }

        private static void CreatePipeOrThrow(out IntPtr readPipe, out IntPtr writePipe, SECURITY_ATTRIBUTES securityAttributes)
        {
            if (!CreatePipe(out readPipe, out writePipe, ref securityAttributes, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        private static void SetHandleInformationOrThrow(IntPtr handle, int mask, int flags)
        {
            if (!SetHandleInformation(handle, mask, flags))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        private static void CloseHandleSafe(ref IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            CloseHandle(handle);
            handle = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct COORD
        {
            internal short X;
            internal short Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            internal int nLength;
            internal IntPtr lpSecurityDescriptor;
            [MarshalAs(UnmanagedType.Bool)]
            internal bool bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFO
        {
            internal int cb;
            internal IntPtr lpReserved;
            internal IntPtr lpDesktop;
            internal IntPtr lpTitle;
            internal int dwX;
            internal int dwY;
            internal int dwXSize;
            internal int dwYSize;
            internal int dwXCountChars;
            internal int dwYCountChars;
            internal int dwFillAttribute;
            internal int dwFlags;
            internal short wShowWindow;
            internal short cbReserved2;
            internal IntPtr lpReserved2;
            internal IntPtr hStdInput;
            internal IntPtr hStdOutput;
            internal IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFOEX
        {
            internal STARTUPINFO StartupInfo;
            internal IntPtr lpAttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            internal IntPtr hProcess;
            internal IntPtr hThread;
            internal int dwProcessId;
            internal int dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetHandleInformation(IntPtr hObject, int dwMask, int dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern void ClosePseudoConsole(IntPtr hPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int CreatePseudoConsole(COORD size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateProcThreadAttribute(
            IntPtr lpAttributeList,
            uint dwFlags,
            IntPtr attribute,
            IntPtr lpValue,
            IntPtr cbSize,
            IntPtr lpPreviousValue,
            IntPtr lpReturnSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcessW(
            string? lpApplicationName,
            StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
            int dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref STARTUPINFOEX lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
