using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace nuone_tools
{
    internal enum ShareType : uint
    {
        DiskTree = 0x00000000,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHARE_INFO_1
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? shi1_netname;

        public ShareType shi1_type;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? shi1_remark;
    }

    internal enum ShellInjectedMenuItemKind
    {
        Command,
        Separator,
        SubmenuHeader,
    }

    internal enum ShellInjectedCommand : uint
    {
        OpenInOtherPane = 0x8000,
        CopyToOtherPane,
        MoveToOtherPane,
        CreateAutomation,
        DeployNodeDocker,
        Rename,
        CreateFolder,
        CopyPath,
    }

    internal sealed class ShellInjectedMenuItem
    {
        public ShellInjectedMenuItem(string text, ShellInjectedCommand command)
        {
            Text = text;
            Command = command;
            Kind = ShellInjectedMenuItemKind.Command;
        }

        public ShellInjectedMenuItem(string text, ShellInjectedMenuItemKind kind)
        {
            Text = text;
            Kind = kind;
        }

        public string Text { get; }

        public ShellInjectedMenuItemKind Kind { get; }

        public ShellInjectedCommand Command { get; }
    }

    internal static partial class ShellContextMenuHost
    {
        private const uint ShellMenuFirstId = 1;
        private const uint ShellMenuLastId = 0x7FFF;
        private const uint MfString = 0x00000000;
        private const uint MfPopup = 0x00000010;
        private const uint MfByPosition = 0x00000400;
        private const uint MfSeparator = 0x00000800;
        private const uint TpmReturnCommand = 0x0100;
        private const uint TpmRightButton = 0x0002;
        private const int WmNull = 0x0000;
        private const int CmIcMaskUnicode = 0x00004000;
        private const int CmIcMaskPtInvoke = 0x20000000;
        private const int SwShownormal = 1;
        private static readonly Guid IidShellFolder = typeof(IShellFolder).GUID;
        private static readonly Guid IidContextMenu = typeof(IContextMenu).GUID;
        private static readonly SUBCLASSPROC MenuSubclassProc = MenuWindowSubclassProc;
        private static ShellContextMenuSession? _activeSession;

        public static void ShowForPaths(
            nint windowHandle,
            double rasterizationScale,
            double x,
            double y,
            string parentFolderPath,
            IReadOnlyList<string> selectedPaths,
            IReadOnlyList<ShellInjectedMenuItem>? injectedMenuItems = null,
            Action<ShellInjectedCommand>? injectedCommandHandler = null)
        {
            if (windowHandle == nint.Zero || string.IsNullOrWhiteSpace(parentFolderPath) || selectedPaths.Count == 0)
            {
                return;
            }

            var screenPoint = new POINT(
                (int)Math.Round(x * rasterizationScale, MidpointRounding.AwayFromZero),
                (int)Math.Round(y * rasterizationScale, MidpointRounding.AwayFromZero));
            ClientToScreen(windowHandle, ref screenPoint);

            using var session = ShellContextMenuSession.Create(
                windowHandle,
                parentFolderPath,
                selectedPaths,
                injectedMenuItems,
                injectedCommandHandler);
            _activeSession = session;

            SetForegroundWindow(windowHandle);
            if (!SetWindowSubclass(windowHandle, MenuSubclassProc, 1, nint.Zero))
            {
                _activeSession = null;
                return;
            }

            try
            {
                var selectedCommand = TrackPopupMenuEx(
                    session.MenuHandle,
                    TpmReturnCommand | TpmRightButton,
                    screenPoint.X,
                    screenPoint.Y,
                    windowHandle,
                    nint.Zero);

                if (selectedCommand >= ShellMenuFirstId && selectedCommand <= ShellMenuLastId)
                {
                    session.InvokeCommand(selectedCommand, screenPoint);
                }
                else if (Enum.IsDefined(typeof(ShellInjectedCommand), selectedCommand))
                {
                    session.InvokeInjectedCommand((ShellInjectedCommand)selectedCommand);
                }
            }
            finally
            {
                RemoveWindowSubclass(windowHandle, MenuSubclassProc, 1);
                PostMessage(windowHandle, WmNull, nint.Zero, nint.Zero);
                _activeSession = null;
            }
        }

        private static nint MenuWindowSubclassProc(
            nint hWnd,
            uint msg,
            nint wParam,
            nint lParam,
            nuint uIdSubclass,
            nint dwRefData)
        {
            var session = _activeSession;
            if (session is not null)
            {
                var result = session.HandleMenuMessage(msg, wParam, lParam);
                if (result.Handled)
                {
                    return result.Result;
                }
            }

            return DefSubclassProc(hWnd, msg, wParam, lParam);
        }

        private sealed class ShellContextMenuSession : IDisposable
        {
            private readonly nint _windowHandle;
            private readonly IShellFolder _parentFolder;
            private readonly IntPtr[] _childPidls;
            private readonly nint _parentPidl;
            private readonly IContextMenu _contextMenu;
            private readonly IContextMenu2? _contextMenu2;
            private readonly IContextMenu3? _contextMenu3;
            private readonly Action<ShellInjectedCommand>? _injectedCommandHandler;
            private bool _disposed;

            private ShellContextMenuSession(
                nint windowHandle,
                IShellFolder parentFolder,
                nint parentPidl,
                IntPtr[] childPidls,
                IContextMenu contextMenu,
                IContextMenu2? contextMenu2,
                IContextMenu3? contextMenu3,
                nint menuHandle,
                Action<ShellInjectedCommand>? injectedCommandHandler)
            {
                _windowHandle = windowHandle;
                _parentFolder = parentFolder;
                _parentPidl = parentPidl;
                _childPidls = childPidls;
                _contextMenu = contextMenu;
                _contextMenu2 = contextMenu2;
                _contextMenu3 = contextMenu3;
                MenuHandle = menuHandle;
                _injectedCommandHandler = injectedCommandHandler;
            }

            public nint MenuHandle { get; }

            public static ShellContextMenuSession Create(
                nint windowHandle,
                string parentFolderPath,
                IReadOnlyList<string> selectedPaths,
                IReadOnlyList<ShellInjectedMenuItem>? injectedMenuItems,
                Action<ShellInjectedCommand>? injectedCommandHandler)
            {
                var desktopFolder = default(IShellFolder);
                var parentFolder = default(IShellFolder);
                var parentPidl = nint.Zero;
                var childPidls = new IntPtr[selectedPaths.Count];
                var menuHandle = nint.Zero;
                var injectedSubmenuHandle = nint.Zero;
                nint contextMenuPtr = nint.Zero;

                try
                {
                    ThrowIfFailed(SHGetDesktopFolder(out desktopFolder));

                    uint attributes = 0;
                    var shellFolderGuid = IidShellFolder;
                    var contextMenuGuid = IidContextMenu;
                    ThrowIfFailed(SHParseDisplayName(parentFolderPath, nint.Zero, out parentPidl, 0, out attributes));

                    ThrowIfFailed(desktopFolder.BindToObject(parentPidl, nint.Zero, ref shellFolderGuid, out var parentFolderPtr));
                    parentFolder = (IShellFolder)Marshal.GetObjectForIUnknown(parentFolderPtr);
                    Marshal.Release(parentFolderPtr);

                    for (var index = 0; index < selectedPaths.Count; index++)
                    {
                        var displayName = Path.GetFileName(selectedPaths[index].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        attributes = 0;
                        ThrowIfFailed(parentFolder.ParseDisplayName(
                            windowHandle,
                            nint.Zero,
                            displayName,
                            out _,
                            out childPidls[index],
                            ref attributes));
                    }

                    ThrowIfFailed(parentFolder.GetUIObjectOf(
                        windowHandle,
                        (uint)childPidls.Length,
                        childPidls,
                        ref contextMenuGuid,
                        nint.Zero,
                        out contextMenuPtr));

                    var contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);
                    var contextMenu2 = contextMenu as IContextMenu2;
                    var contextMenu3 = contextMenu as IContextMenu3;
                    Marshal.Release(contextMenuPtr);
                    contextMenuPtr = nint.Zero;

                    menuHandle = CreatePopupMenu();
                    if (menuHandle == nint.Zero)
                    {
                        throw new InvalidOperationException("無法建立 Shell 選單。");
                    }

                    ThrowIfFailed(contextMenu.QueryContextMenu(menuHandle, 0, ShellMenuFirstId, ShellMenuLastId, 0));
                    injectedSubmenuHandle = InjectCustomMenu(menuHandle, injectedMenuItems);

                    return new ShellContextMenuSession(
                        windowHandle,
                        parentFolder,
                        parentPidl,
                        childPidls,
                        contextMenu,
                        contextMenu2,
                        contextMenu3,
                        menuHandle,
                        injectedCommandHandler);
                }
                catch
                {
                    if (menuHandle != nint.Zero)
                    {
                        DestroyMenu(menuHandle);
                    }

                    foreach (var pidl in childPidls.Where(pidl => pidl != nint.Zero))
                    {
                        CoTaskMemFree(pidl);
                    }

                if (contextMenuPtr != nint.Zero)
                {
                    Marshal.Release(contextMenuPtr);
                }

                    if (parentFolder is not null)
                    {
                        Marshal.ReleaseComObject(parentFolder);
                    }

                    if (parentPidl != nint.Zero)
                    {
                        CoTaskMemFree(parentPidl);
                    }

                    throw;
                }
                finally
                {
                    if (desktopFolder is not null)
                    {
                        Marshal.ReleaseComObject(desktopFolder);
                    }
                }
            }

            public ShellMenuMessageResult HandleMenuMessage(uint msg, nint wParam, nint lParam)
            {
                if (_contextMenu3 is not null)
                {
                    var hr = _contextMenu3.HandleMenuMsg2(msg, wParam, lParam, out var result);
                    if (hr == 0)
                    {
                        return new ShellMenuMessageResult(true, result);
                    }
                }

                if (_contextMenu2 is not null)
                {
                    var hr = _contextMenu2.HandleMenuMsg(msg, wParam, lParam);
                    if (hr == 0)
                    {
                        return new ShellMenuMessageResult(true, nint.Zero);
                    }
                }

                return default;
            }

            public void InvokeCommand(uint selectedCommand, POINT screenPoint)
            {
                var commandOffset = unchecked((nint)(selectedCommand - ShellMenuFirstId));
                var invokeInfo = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CmIcMaskUnicode | CmIcMaskPtInvoke,
                    hwnd = _windowHandle,
                    lpVerb = commandOffset,
                    lpVerbW = commandOffset,
                    nShow = SwShownormal,
                    ptInvoke = screenPoint,
                };

                ThrowIfFailed(_contextMenu.InvokeCommand(ref invokeInfo));
            }

            public void InvokeInjectedCommand(ShellInjectedCommand command)
            {
                _injectedCommandHandler?.Invoke(command);
            }

            private static nint InjectCustomMenu(nint menuHandle, IReadOnlyList<ShellInjectedMenuItem>? injectedMenuItems)
            {
                if (menuHandle == nint.Zero || injectedMenuItems is null || injectedMenuItems.Count == 0)
                {
                    return nint.Zero;
                }

                var header = injectedMenuItems.FirstOrDefault(item => item.Kind == ShellInjectedMenuItemKind.SubmenuHeader);
                var submenuTitle = string.IsNullOrWhiteSpace(header?.Text) ? "Nuone Tools" : header!.Text;
                var submenuHandle = CreatePopupMenu();
                if (submenuHandle == nint.Zero)
                {
                    return nint.Zero;
                }

                foreach (var item in injectedMenuItems)
                {
                    switch (item.Kind)
                    {
                        case ShellInjectedMenuItemKind.SubmenuHeader:
                            continue;
                        case ShellInjectedMenuItemKind.Separator:
                            AppendMenu(submenuHandle, MfSeparator, 0, null);
                            break;
                        case ShellInjectedMenuItemKind.Command:
                            AppendMenu(submenuHandle, MfString, (nuint)item.Command, item.Text);
                            break;
                    }
                }

                InsertMenu(menuHandle, 0, MfByPosition | MfPopup, (nuint)submenuHandle, submenuTitle);
                InsertMenu(menuHandle, 1, MfByPosition | MfSeparator, 0, null);
                return submenuHandle;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                if (MenuHandle != nint.Zero)
                {
                    DestroyMenu(MenuHandle);
                }

                foreach (var pidl in _childPidls.Where(pidl => pidl != nint.Zero))
                {
                    CoTaskMemFree(pidl);
                }

                if (_parentPidl != nint.Zero)
                {
                    CoTaskMemFree(_parentPidl);
                }

                Marshal.ReleaseComObject(_contextMenu);
                Marshal.ReleaseComObject(_parentFolder);
            }
        }

        private readonly record struct ShellMenuMessageResult(bool Handled, nint Result);

        [ComImport]
        [Guid("000214E6-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellFolder
        {
            [PreserveSig]
            int ParseDisplayName(
                nint hwnd,
                nint pbc,
                [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
                out uint pchEaten,
                out nint ppidl,
                ref uint pdwAttributes);

            [PreserveSig]
            int EnumObjects(nint hwnd, int grfFlags, out nint ppenumIDList);

            [PreserveSig]
            int BindToObject(nint pidl, nint pbc, ref Guid riid, out nint ppv);

            [PreserveSig]
            int BindToStorage(nint pidl, nint pbc, ref Guid riid, out nint ppv);

            [PreserveSig]
            int CompareIDs(nint lParam, nint pidl1, nint pidl2);

            [PreserveSig]
            int CreateViewObject(nint hwndOwner, ref Guid riid, out nint ppv);

            [PreserveSig]
            int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] nint[] apidl, ref uint rgfInOut);

            [PreserveSig]
            int GetUIObjectOf(
                nint hwndOwner,
                uint cidl,
                [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] nint[] apidl,
                ref Guid riid,
                nint rgfReserved,
                out nint ppv);

            [PreserveSig]
            int GetDisplayNameOf(nint pidl, uint uFlags, out STRRET pName);

            [PreserveSig]
            int SetNameOf(nint hwnd, nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out nint ppidlOut);
        }

        [ComImport]
        [Guid("000214E4-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu
        {
            [PreserveSig]
            int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

            [PreserveSig]
            int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

            [PreserveSig]
            int GetCommandString(nint idCmd, uint uType, nint pReserved, nint pszName, uint cchMax);
        }

        [ComImport]
        [Guid("000214F4-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu2
        {
            [PreserveSig]
            int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

            [PreserveSig]
            int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

            [PreserveSig]
            int GetCommandString(nint idCmd, uint uType, nint pReserved, nint pszName, uint cchMax);

            [PreserveSig]
            int HandleMenuMsg(uint uMsg, nint wParam, nint lParam);
        }

        [ComImport]
        [Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu3
        {
            [PreserveSig]
            int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

            [PreserveSig]
            int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

            [PreserveSig]
            int GetCommandString(nint idCmd, uint uType, nint pReserved, nint pszName, uint cchMax);

            [PreserveSig]
            int HandleMenuMsg(uint uMsg, nint wParam, nint lParam);

            [PreserveSig]
            int HandleMenuMsg2(uint uMsg, nint wParam, nint lParam, out nint plResult);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CMINVOKECOMMANDINFOEX
        {
            public int cbSize;
            public int fMask;
            public nint hwnd;
            public nint lpVerb;
            public nint lpParameters;
            public nint lpDirectory;
            public int nShow;
            public int dwHotKey;
            public nint hIcon;
            public nint lpTitle;
            public nint lpVerbW;
            public nint lpParametersW;
            public nint lpDirectoryW;
            public nint lpTitleW;
            public POINT ptInvoke;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STRRET
        {
            public uint uType;
            public nint pOleStr;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        private delegate nint SUBCLASSPROC(
            nint hWnd,
            uint uMsg,
            nint wParam,
            nint lParam,
            nuint uIdSubclass,
            nint dwRefData);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(
            string pszName,
            nint pbc,
            out nint ppidl,
            uint sfgaoIn,
            out uint psfgaoOut);

        [DllImport("shell32.dll")]
        private static extern int SHGetDesktopFolder(out IShellFolder ppshf);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(nint pv);

        [DllImport("user32.dll")]
        private static extern nint CreatePopupMenu();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyMenu(nint hMenu);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "InsertMenuW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InsertMenu(nint hMenu, uint uPosition, uint uFlags, nuint uIDNewItem, string? lpNewItem);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AppendMenu(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

        [DllImport("user32.dll")]
        private static extern uint TrackPopupMenuEx(nint hmenu, uint uFlags, int x, int y, nint hwnd, nint lptpm);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(nint hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowSubclass(
            nint hWnd,
            SUBCLASSPROC pfnSubclass,
            nuint uIdSubclass,
            nint dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveWindowSubclass(
            nint hWnd,
            SUBCLASSPROC pfnSubclass,
            nuint uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern nint DefSubclassProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

        private static void ThrowIfFailed(int hr)
        {
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }
        }
    }
}
