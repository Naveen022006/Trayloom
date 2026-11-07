// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>Settings keys of the Focus session (spec 10c).</summary>
internal static partial class K
{
    /// <summary>Id of the mode a Focus session enters; empty means none.</summary>
    public const string FocusModeId = "notchFocusModeId";

    /// <summary>Hold back the island's own optional notices while a Focus phase runs.</summary>
    public const string FocusQuiet = "notchFocusQuiet";

    /// <summary>Leave the mode the session entered when the session ends.</summary>
    public const string FocusLeaveMode = "notchFocusLeaveMode";

    /// <summary>Show the companion beside the time left while a session runs.</summary>
    public const string FocusMascot = "notchFocusMascot";

    /// <summary>Registers the defaults of the Focus session.</summary>
    private static void RegisterFocusDefaults(IAppSettings app)
    {
        app.RegisterDefaults(new Dictionary<string, object?>
        {
            [FocusModeId] = "",
            [FocusQuiet] = true,
            [FocusLeaveMode] = true,
            [FocusMascot] = true,
        });
    }
}
