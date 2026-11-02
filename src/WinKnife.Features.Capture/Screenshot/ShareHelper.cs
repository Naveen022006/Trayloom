// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace WinKnife.Features.Capture;

/// <summary>Opens the Windows share sheet for a file through IDataTransferManagerInterop.</summary>
internal static class ShareHelper
{
    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        nint GetForWindow(nint appWindow, ref Guid riid);

        void ShowShareUIForWindow(nint appWindow);
    }

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(nint classId, ref Guid iid, out nint factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out nint hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint hstring);

    public static bool Share(nint hwnd, string file, string title)
    {
        try
        {
            var classId = "Windows.ApplicationModel.DataTransfer.DataTransferManager";
            WindowsCreateString(classId, classId.Length, out var hstring);
            var iid = typeof(IDataTransferManagerInterop).GUID;
            var hr = RoGetActivationFactory(hstring, ref iid, out var factory);
            WindowsDeleteString(hstring);
            if (hr != 0)
            {
                throw new COMException("share unavailable", hr);
            }

            var interop = (IDataTransferManagerInterop)Marshal.GetObjectForIUnknown(factory);
            var managerIid = new Guid("A5CAEE9B-8708-49D1-8D36-67D25A8DA00C");
            var ptr = interop.GetForWindow(hwnd, ref managerIid);
            var manager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(ptr);
            Windows.Foundation.TypedEventHandler<DataTransferManager, DataRequestedEventArgs> handler = null!;
            handler = async (sender, args) =>
            {
                manager.DataRequested -= handler;
                var deferral = args.Request.GetDeferral();
                try
                {
                    args.Request.Data.Properties.Title = title;
                    var storage = await StorageFile.GetFileFromPathAsync(file);
                    args.Request.Data.SetStorageItems([storage]);
                }
                finally
                {
                    deferral.Complete();
                }
            };
            manager.DataRequested += handler;
            interop.ShowShareUIForWindow(hwnd);
            return true;
        }
        catch
        {
            // Fallback: reveal the file so it can be shared by hand.
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
            }
            catch
            {
                // ignore
            }

            return false;
        }
    }

}
