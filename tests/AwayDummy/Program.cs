// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AwayDummy;

/// <summary>
/// Stand-in application windows for the Away mode tests. <c>AwayDummy.exe --windows 3</c> creates top-level windows titled
/// "Away dummy 1..N" (window 2 maximized, window 1 in front, distinct positions and sizes) and prints one line per window:
/// <c>hwnd=&lt;hex&gt; z=&lt;n&gt; state=&lt;normal|max|min&gt; rect=l,t,r,b fg=&lt;0|1&gt;</c>. With <c>--report &lt;file&gt;</c> the same lines are
/// rewritten every 500 ms until the process is killed. It reads nothing from Trayloom.
/// </summary>
public static class Program
{
    private delegate bool EnumProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc proc, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [STAThread]
    public static int Main(string[] args)
    {
        var count = 3;
        string? report = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--windows" && i + 1 < args.Length)
            {
                count = Math.Clamp(int.Parse(args[++i], CultureInfo.InvariantCulture), 1, 12);
            }
            else if (args[i] == "--report" && i + 1 < args.Length)
            {
                report = args[++i];
            }
        }

        Application.EnableVisualStyles();
        var forms = new List<Form>();

        // Created back to front so window 1 ends up on top.
        for (var n = count; n >= 1; n--)
        {
            var form = new Form
            {
                Text = "Away dummy " + n,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = true,
            };
            if (n == 2)
            {
                form.Bounds = new System.Drawing.Rectangle(160, 160, 700, 460);
                form.WindowState = FormWindowState.Maximized;
            }
            else
            {
                form.Bounds = new System.Drawing.Rectangle(120 + (n * 70), 110 + (n * 45), 520 + (n * 30), 360 + (n * 15));
            }

            form.Show();
            forms.Add(form);
        }

        var first = forms.First(f => f.Text == "Away dummy 1");
        Application.Idle += OnFirstIdle;

        void OnFirstIdle(object? sender, EventArgs e)
        {
            Application.Idle -= OnFirstIdle;
            foreach (var f in forms.Where(f => f.Text != "Away dummy 1"))
            {
                f.Visible = false;
                f.Visible = true;
            }

            first.Activate();
        }

        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) => Report(forms, report);
        timer.Start();
        Report(forms, report);
        Application.Run(new ApplicationContext(first));
        return 0;
    }

    private static void Report(List<Form> forms, string? file)
    {
        var mine = forms.Where(f => f.IsHandleCreated).ToDictionary(f => f.Handle, f => f.Text);
        var order = new List<nint>();
        EnumWindows((hwnd, _) =>
        {
            if (mine.ContainsKey(hwnd))
            {
                order.Add(hwnd);
            }

            return true;
        }, 0);

        var foreground = GetForegroundWindow();
        var text = new StringBuilder();
        foreach (var hwnd in order)
        {
            GetWindowRect(hwnd, out var r);
            var state = IsIconic(hwnd) ? "min" : IsZoomed(hwnd) ? "max" : "normal";
            text.Append(CultureInfo.InvariantCulture, $"hwnd={hwnd:x} z={order.IndexOf(hwnd)} state={state} rect={r.Left},{r.Top},{r.Right},{r.Bottom} fg={(hwnd == foreground ? 1 : 0)} vis={(IsWindowVisible(hwnd) ? 1 : 0)} title={mine[hwnd]}\n");
        }

        if (file is null)
        {
            Console.Write(text.ToString());
            return;
        }

        try
        {
            var temp = file + ".tmp";
            File.WriteAllText(temp, text.ToString());
            File.Move(temp, file, overwrite: true);
        }
        catch (IOException)
        {
            // the reader had the file open; the next tick writes it again
        }
    }
}
