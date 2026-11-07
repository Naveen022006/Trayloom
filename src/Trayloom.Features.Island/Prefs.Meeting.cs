// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using Trayloom.Core;

namespace Trayloom.Features.Island;

/// <summary>Settings keys of the meeting heads-up (spec 10d).</summary>
internal static partial class K
{
    /// <summary>Show the next meeting in the closed island a few minutes before it starts.</summary>
    public const string MeetingPrep = "notchMeetingPrep";

    /// <summary>How many minutes before the start the heads-up appears: 2, 5, 10 or 15.</summary>
    public const string MeetingLead = "notchMeetingLead";

    /// <summary>Only meetings that carry a video-call link get a heads-up.</summary>
    public const string MeetingVideoOnly = "notchMeetingVideoOnly";

    /// <summary>Registers the defaults of the meeting heads-up.</summary>
    private static void RegisterMeetingDefaults(IAppSettings app)
    {
        app.RegisterDefaults(new Dictionary<string, object?>
        {
            [MeetingPrep] = true,
            [MeetingLead] = 5,
            [MeetingVideoOnly] = true,
        });
    }
}
