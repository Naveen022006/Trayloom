// SPDX-License-Identifier: GPL-3.0-or-later
// Trayloom — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Collections.Concurrent;
using System.Reflection;

namespace Trayloom.Core;

/// <summary>When a recovery hook runs.</summary>
public enum RecoveryStage
{
    /// <summary>Normal startup: undo anything a previous run left behind.</summary>
    Startup,

    /// <summary>After a crash or kill was detected.</summary>
    Crash,

    /// <summary>During uninstall: give back every Windows change Trayloom made.</summary>
    Uninstall,
}

/// <summary>
/// Undoes system changes Trayloom made (lid action, mic mute, pending Bluetooth, ...). Discovered by reflection in the loaded
/// Trayloom assemblies (public, non-abstract, public parameterless constructor).
/// </summary>
public interface IStartupRecovery
{
    /// <summary>Short stable id used in the log.</summary>
    string Id { get; }

    /// <summary>
    /// Acts only when a journal written by Trayloom says Trayloom changed something; a missing journal means do nothing.
    /// Must never throw and must be idempotent.
    /// </summary>
    void Recover(RecoveryStage stage, ILog log);
}

/// <summary>Runs every <see cref="IStartupRecovery"/> hook, each on its own task, within a total time budget.</summary>
public static class StartupRecovery
{
    /// <summary>
    /// Discovers the hooks and runs them with <see cref="Run"/>. Assemblies whose name starts with <c>Trayloom</c> are scanned (the
    /// features and the App).
    /// </summary>
    public static void RunAll(RecoveryStage stage, ILog log, TimeSpan totalBudget)
    {
        var hooks = new List<IStartupRecovery>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name;
            if (name is null || !name.StartsWith("Trayloom", StringComparison.Ordinal))
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface || !typeof(IStartupRecovery).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null)
                {
                    continue;
                }

                try
                {
                    hooks.Add((IStartupRecovery)Activator.CreateInstance(type)!);
                }
                catch (Exception ex)
                {
                    log.Error($"recovery {type.Name}: could not be created", ex);
                }
            }
        }

        Run(hooks, stage, log, totalBudget);
    }

    /// <summary>
    /// Runs the hooks in parallel and waits at most <paramref name="totalBudget"/> for all of them. Logs and returns one line per hook:
    /// <c>recovery &lt;id&gt;: done | failed | still running</c>. A hook still running is left alone (it is not aborted).
    /// </summary>
    public static IReadOnlyList<string> Run(IEnumerable<IStartupRecovery> hooks, RecoveryStage stage, ILog log, TimeSpan totalBudget)
    {
        var list = hooks.ToList();
        var states = new ConcurrentDictionary<int, string>();
        var ids = new string[list.Count];
        var tasks = new Task[list.Count];
        for (var n = 0; n < list.Count; n++)
        {
            var index = n;
            var hook = list[n];
            ids[n] = SafeId(hook);
            tasks[n] = Task.Run(() =>
            {
                try
                {
                    hook.Recover(stage, log);
                    states[index] = "done";
                }
                catch (Exception ex)
                {
                    states[index] = "failed";
                    try
                    {
                        log.Error($"recovery {ids[index]} threw", ex);
                    }
                    catch
                    {
                        // logging must not break recovery
                    }
                }
            });
        }

        try
        {
            Task.WaitAll(tasks, totalBudget < TimeSpan.Zero ? TimeSpan.Zero : totalBudget);
        }
        catch
        {
            // tasks never throw: they catch everything
        }

        var lines = new List<string>(list.Count);
        for (var n = 0; n < list.Count; n++)
        {
            var state = states.TryGetValue(n, out var s) ? s : "still running";
            var line = $"recovery {ids[n]}: {state}";
            lines.Add(line);
            try
            {
                if (state == "done")
                {
                    log.Info(line);
                }
                else
                {
                    log.Warn(line);
                }
            }
            catch
            {
                // ignore
            }
        }

        return lines;
    }

    private static string SafeId(IStartupRecovery hook)
    {
        try
        {
            return hook.Id;
        }
        catch
        {
            return hook.GetType().Name;
        }
    }
}
