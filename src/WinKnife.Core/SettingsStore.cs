// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WinKnife.Core;

/// <summary>
/// JSON-backed settings database. Layout: <c>{ "scopes": { "&lt;scope&gt;": { "&lt;key&gt;": value } } }</c>.
/// Writes are debounced and atomic (temp file + move).
/// </summary>
public sealed class SettingsStore : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>Serializer options used for all stored values (enums are written as strings).</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly object _writeGate = new();
    private readonly string _path;
    private readonly ILog _log;
    private readonly Dictionary<string, ScopedSettings> _scopes = new(StringComparer.Ordinal);
    private readonly Timer _saveTimer;
    private JsonObject _scopesNode;
    private bool _dirty;
    private bool _disposed;
    private volatile bool _suspended;

    /// <summary>Opens (or creates) the store at <paramref name="path"/>.</summary>
    public SettingsStore(string path, ILog log)
    {
        _path = path;
        _log = log;
        _scopesNode = Load();
        _saveTimer = new Timer(_ => SaveIfDirty(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Returns the settings for one scope (a feature id, or an internal scope starting with an underscore).</summary>
    public ScopedSettings GetScope(string scope)
    {
        lock (_gate)
        {
            if (!_scopes.TryGetValue(scope, out var settings))
            {
                settings = new ScopedSettings(this, scope);
                _scopes[scope] = settings;
            }

            return settings;
        }
    }

    /// <summary>Writes pending changes immediately.</summary>
    public void Flush() => SaveIfDirty();

    /// <summary>Stops every further write (used right before the settings file is replaced by an import / deleted).</summary>
    public void SuspendSaving() => _suspended = true;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _saveTimer.Dispose();
        SaveIfDirty();
    }

    internal bool Contains(string scope, string key)
    {
        lock (_gate)
        {
            return _scopesNode[scope] is JsonObject obj && obj.ContainsKey(key);
        }
    }

    internal T Get<T>(string scope, string key, T fallback)
    {
        lock (_gate)
        {
            if (_scopesNode[scope] is not JsonObject obj || !obj.TryGetPropertyValue(key, out var node))
            {
                return fallback;
            }

            if (node is null)
            {
                return default!;
            }

            try
            {
                return node.Deserialize<T>(JsonOptions) is { } value ? value : fallback;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
            {
                _log.Warn($"Setting {scope}/{key} could not be read as {typeof(T).Name}: {ex.Message}");
                return fallback;
            }
        }
    }

    internal bool Set<T>(string scope, string key, T value)
    {
        var node = JsonSerializer.SerializeToNode(value, JsonOptions);
        lock (_gate)
        {
            if (_scopesNode[scope] is not JsonObject obj)
            {
                obj = new JsonObject();
                _scopesNode[scope] = obj;
            }

            if (obj.TryGetPropertyValue(key, out var existing) && JsonNode.DeepEquals(existing, node))
            {
                return false;
            }

            obj[key] = node;
            _dirty = true;
        }

        if (!_disposed)
        {
            _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }

        return true;
    }

    internal void Remove(string scope, string key)
    {
        lock (_gate)
        {
            if (_scopesNode[scope] is JsonObject obj && obj.Remove(key))
            {
                _dirty = true;
            }
        }

        if (!_disposed)
        {
            _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private JsonObject Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject;
                if (root?["scopes"] is JsonObject scopes)
                {
                    root.Remove("scopes");
                    return scopes;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("settings.json is unreadable; starting with defaults", ex);
            try
            {
                File.Copy(_path, _path + ".bad", overwrite: true);
            }
            catch
            {
                // best effort backup
            }
        }

        return new JsonObject();
    }

    private void SaveIfDirty()
    {
        if (_suspended)
        {
            return;
        }

        string json;
        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            _dirty = false;
            var root = new JsonObject { ["scopes"] = _scopesNode.DeepClone() };
            json = root.ToJsonString(JsonOptions);
        }

        lock (_writeGate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                _log.Error("Failed to save settings", ex);
                lock (_gate)
                {
                    _dirty = true;
                }
            }
        }
    }
}

/// <summary>One scope (usually a feature id) of a <see cref="SettingsStore"/>.</summary>
public sealed class ScopedSettings : IFeatureSettings
{
    private readonly SettingsStore _store;
    private readonly string _scope;

    internal ScopedSettings(SettingsStore store, string scope)
    {
        _store = store;
        _scope = scope;
    }

    /// <inheritdoc />
    public event Action<string>? Changed;

    /// <inheritdoc />
    public T Get<T>(string key, T fallback) => _store.Get(_scope, key, fallback);

    /// <inheritdoc />
    public void Set<T>(string key, T value)
    {
        if (_store.Set(_scope, key, value))
        {
            Changed?.Invoke(key);
        }
    }

    /// <summary>True when a value (even an explicit null) is stored for <paramref name="key"/>.</summary>
    public bool Contains(string key) => _store.Contains(_scope, key);

    /// <summary>Removes a stored value so <see cref="Get{T}"/> returns the fallback again.</summary>
    public void Remove(string key)
    {
        _store.Remove(_scope, key);
        Changed?.Invoke(key);
    }
}
