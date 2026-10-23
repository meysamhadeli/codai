namespace Codai.Core.Chat;

/// <summary>
/// Remembers which files and skills a session has touched, most recent first.
/// </summary>
/// <remarks>
/// <para>
/// A compaction replaces the turns that carried the reading with a summary, and a summary
/// cannot carry a file. What is left afterwards is a conversation that knows <em>that</em>
/// the parser was read and no longer what is in it, so the model re-reads the same file to
/// recover what the summary could not keep — spending the tokens it just saved.
/// </para>
/// <para>
/// This is the smallest thing that prevents that: a bounded, de-duplicated list of what was
/// touched, kept as it happens rather than derived from the history, because the history is
/// exactly what a compaction is allowed to throw away.
/// </para>
/// </remarks>
public sealed class SessionFootprint
{
    /// <summary>
    /// Paths kept. Larger than any restore would use, because the restore also has a token
    /// budget and a file that does not fit is worth skipping rather than forgetting: the next
    /// compaction may have room for it.
    /// </summary>
    public const int DefaultCapacity = 32;

    private readonly object _gate = new();
    private readonly List<string> _files = [];
    private readonly List<string> _skills = [];
    private readonly int _capacity;

    /// <summary>Creates a footprint.</summary>
    /// <param name="capacity">Paths to remember before the oldest is forgotten.</param>
    public SessionFootprint(int capacity = DefaultCapacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    /// <summary>Paths of the files this session read or wrote, most recently touched first.</summary>
    public IReadOnlyList<string> Files
    {
        get
        {
            lock (_gate)
            {
                return [.. _files];
            }
        }
    }

    /// <summary>Names of the skills this session loaded, most recently loaded first.</summary>
    public IReadOnlyList<string> Skills
    {
        get
        {
            lock (_gate)
            {
                return [.. _skills];
            }
        }
    }

    /// <summary>Records that a file was read or written.</summary>
    /// <param name="path">Path as the tool received it, or null when there was none.</param>
    public void RecordFile(string? path) => Record(_files, path);

    /// <summary>Records that a skill was loaded.</summary>
    /// <param name="name">Skill name, or null when there was none.</param>
    public void RecordSkill(string? name) => Record(_skills, name);

    /// <summary>Forgets everything, which is what starting the conversation over means.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _files.Clear();
            _skills.Clear();
        }
    }

    private void Record(List<string> entries, string? value)
    {
        var entry = value?.Trim();

        if (string.IsNullOrEmpty(entry))
        {
            return;
        }

        lock (_gate)
        {
            // Touching a file again makes it the most recent one rather than adding a
            // second copy: the list is what the restore reads, and reading it twice would
            // spend the budget on one file.
            _ = entries.RemoveAll(existing => string.Equals(existing, entry, StringComparison.OrdinalIgnoreCase));
            entries.Insert(0, entry);

            if (entries.Count > _capacity)
            {
                entries.RemoveRange(_capacity, entries.Count - _capacity);
            }
        }
    }
}
