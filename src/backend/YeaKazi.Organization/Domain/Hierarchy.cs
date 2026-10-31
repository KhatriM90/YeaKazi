namespace YeaKazi.Organization.Domain;

/// <summary>Detects cycles in parent links (department parents, position reporting lines).</summary>
public static class Hierarchy
{
    /// <summary>
    /// True if setting <paramref name="nodeId"/>'s parent to <paramref name="newParentId"/> creates a cycle,
    /// given the current parent links of all nodes.
    /// </summary>
    public static bool WouldCreateCycle(Guid nodeId, Guid? newParentId, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        if (newParentId is null)
        {
            return false;
        }

        var visited = new HashSet<Guid>();
        Guid? current = newParentId;
        while (current is { } c)
        {
            if (c == nodeId)
            {
                return true;
            }

            if (!visited.Add(c))
            {
                // Pre-existing cycle in data: treat as unsafe.
                return true;
            }

            current = parents.TryGetValue(c, out var p) ? p : null;
        }

        return false;
    }
}
