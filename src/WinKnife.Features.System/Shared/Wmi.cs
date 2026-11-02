// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Runtime.InteropServices;

namespace WinKnife.Features.SystemTools;

/// <summary>
/// Minimal late-bound WMI access through the scripting COM API (WbemScripting.SWbemLocator).
/// Avoids the System.Management NuGet package. All methods return empty results instead of throwing.
/// </summary>
internal static class Wmi
{
    private const int FlagsReturnImmediately = 0x10;

    public static List<Dictionary<string, object?>> Query(string ns, string query, params string[] properties)
    {
        var rows = new List<Dictionary<string, object?>>();
        object? locatorObj = null;
        object? serviceObj = null;
        object? setObj = null;
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (type is null)
            {
                return rows;
            }

            locatorObj = Activator.CreateInstance(type);
            dynamic locator = locatorObj!;
            serviceObj = locator.ConnectServer(".", ns);
            dynamic service = serviceObj!;
            setObj = service.ExecQuery(query, "WQL", FlagsReturnImmediately);
            dynamic set = setObj!;
            foreach (dynamic item in set)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in properties)
                {
                    try
                    {
                        row[name] = item.Properties_.Item(name).Value;
                    }
                    catch (Exception)
                    {
                        row[name] = null;
                    }
                }

                rows.Add(row);
                TryRelease(item);
            }
        }
        catch (Exception)
        {
            // Class missing, access denied, WMI service unavailable...
        }
        finally
        {
            TryRelease(setObj);
            TryRelease(serviceObj);
            TryRelease(locatorObj);
        }

        return rows;
    }

    /// <summary>Runs <paramref name="action"/> against each object returned by the query (for method calls).</summary>
    public static bool ForEach(string ns, string query, Action<object> action)
    {
        object? locatorObj = null;
        object? serviceObj = null;
        object? setObj = null;
        var any = false;
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (type is null)
            {
                return false;
            }

            locatorObj = Activator.CreateInstance(type);
            dynamic locator = locatorObj!;
            serviceObj = locator.ConnectServer(".", ns);
            dynamic service = serviceObj!;
            setObj = service.ExecQuery(query, "WQL", FlagsReturnImmediately);
            dynamic set = setObj!;
            foreach (object item in set)
            {
                action(item);
                any = true;
                TryRelease(item);
            }
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            TryRelease(setObj);
            TryRelease(serviceObj);
            TryRelease(locatorObj);
        }

        return any;
    }

    /// <summary>
    /// Calls a method of a WMI scripting object. Uses IDispatch directly instead of the C# dynamic binder, which caches
    /// DISPIDs of the first object and fails on later objects (WMI assigns DISPIDs per object instance).
    /// </summary>
    public static object? Invoke(object item, string method, params object[] args) =>
        item.GetType().InvokeMember(method, System.Reflection.BindingFlags.InvokeMethod, null, item, args);

    public static int ToInt(object? value, int fallback = 0)
    {
        try
        {
            return value is null ? fallback : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static void TryRelease(object? o)
    {
        try
        {
            if (o is not null && Marshal.IsComObject(o))
            {
                Marshal.ReleaseComObject(o);
            }
        }
        catch (Exception)
        {
        }
    }
}
