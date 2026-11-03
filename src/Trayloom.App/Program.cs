// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.CompilerServices;
using Trayloom.Core.Native;

namespace Trayloom.App;

/// <summary>
/// The entry point. <c>--window-guardian*</c> runs the small window guardian and returns before WPF is touched: the two paths are
/// separate non-inlined methods so the guardian process never loads PresentationFramework (it must stay light).
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        args.Length > 0 && args[0].StartsWith("--window-guardian", StringComparison.Ordinal) ? Guardian(args) : RunApp();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Guardian(string[] args) => WindowGuardian.Run(args);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunApp()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
