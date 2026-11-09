// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;

namespace Trayloom.Setup;

/// <summary>Unpacks the bundled release and opens Trayloom's per-user installer.</summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            MessageBox.Show("Run Trayloom Setup normally, without administrator rights. Trayloom installs for your Windows account.",
                "Trayloom Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // A second Trayloom instance only activates the first one, so it cannot present the installer while the app is running.
        if (IsTrayloomRunning())
        {
            MessageBox.Show("Quit Trayloom from its tray icon, then open Trayloom Setup again. Your settings will be kept.",
                "Trayloom Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.Run(new SetupWindow());
    }

    private static bool IsTrayloomRunning()
    {
        // Match the application's isolated-instance names when both development overrides are supplied.
        var name = Environment.GetEnvironmentVariable("TRAYLOOM_INSTANCE");
        var suffix = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRAYLOOM_DATA")) && !string.IsNullOrEmpty(name)
            ? "." + string.Concat(name.Where(char.IsLetterOrDigit))
            : string.Empty;
        try
        {
            using var mutex = Mutex.OpenExisting(@"Local\Trayloom.SingleInstance" + suffix);
            try
            {
                if (!mutex.WaitOne(0))
                {
                    return true;
                }
                mutex.ReleaseMutex();
                return false;
            }
            catch (AbandonedMutexException)
            {
                mutex.ReleaseMutex();
                return false;
            }
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}

/// <summary>Shows extraction progress, then waits for the temporary copy to exit before removing it.</summary>
internal sealed class SetupWindow : Form
{
    private readonly Label _status = new() { AutoSize = true, Text = "Preparing Trayloom. This may take a moment…", Left = 22, Top = 22 };

    public SetupWindow()
    {
        Text = "Trayloom Setup";
        ClientSize = new System.Drawing.Size(420, 112);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        Controls.Add(_status);
        Controls.Add(new ProgressBar { Left = 22, Top = 58, Width = 376, Height = 16, Style = ProgressBarStyle.Marquee });
        Shown += async (_, _) => await RunSetupAsync();
    }

    private async Task RunSetupAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Trayloom-Setup-" + Guid.NewGuid().ToString("N"));
        try
        {
            await Task.Run(() => Extract(folder));
            var exe = Path.Combine(folder, "Trayloom.exe");
            var start = new ProcessStartInfo(exe, "--install") { UseShellExecute = false, WorkingDirectory = folder };
            start.Environment["TRAYLOOM_SETUP_LAUNCH"] = "1";
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Trayloom could not be started.");
            Hide();
            await process.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Trayloom Setup could not start the installer. " + ex.Message, "Trayloom Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Close();
        }
    }

    private static void Extract(string folder)
    {
        Directory.CreateDirectory(folder);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Trayloom.Setup.Payload.zip")
            ?? throw new InvalidDataException("The installation files are missing.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        archive.ExtractToDirectory(folder);
        if (!File.Exists(Path.Combine(folder, "Trayloom.exe")) || !File.Exists(Path.Combine(folder, "Trayloom.Core.dll")))
        {
            throw new InvalidDataException("The installation files are incomplete.");
        }
    }
}
