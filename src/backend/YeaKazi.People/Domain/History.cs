namespace YeaKazi.People.Domain;

/// <summary>Reads effective-dated employment history as of a business date.</summary>
public static class History
{
    public static EmploymentEpisode? EpisodeOn(IEnumerable<EmploymentEpisode> episodes, DateOnly date)
        => episodes.Where(e => e.HireDate <= date && (e.TerminationDate is null || e.TerminationDate >= date))
            .OrderByDescending(e => e.Sequence).FirstOrDefault();

    /// <summary>The non-cancelled version in force on the date within the episode.</summary>
    public static AssignmentVersion? AssignmentOn(IEnumerable<AssignmentVersion> versions, Guid episodeId, DateOnly date)
        => versions.Where(v => v.EpisodeId == episodeId && !v.IsCancelled && v.EffectiveFrom <= date)
            .OrderByDescending(v => v.EffectiveFrom).FirstOrDefault();

    /// <summary>Day before the next version starts (null while open-ended).</summary>
    public static DateOnly? EffectiveTo(IEnumerable<AssignmentVersion> versions, AssignmentVersion version, EmploymentEpisode episode)
    {
        if (version.IsCancelled)
        {
            return null;
        }

        var next = versions.Where(v => v.EpisodeId == version.EpisodeId && !v.IsCancelled && v.EffectiveFrom > version.EffectiveFrom)
            .OrderBy(v => v.EffectiveFrom).FirstOrDefault();
        return next is not null ? next.EffectiveFrom.AddDays(-1) : episode.TerminationDate;
    }

    public static string StatusOn(IReadOnlyCollection<EmploymentEpisode> episodes, IReadOnlyCollection<AssignmentVersion> versions, DateOnly date)
    {
        var current = EpisodeOn(episodes, date);
        if (current is not null)
        {
            return AssignmentOn(versions, current.Id, date)?.Status == AssignmentStatuses.Suspended
                ? EmployeeStatuses.Suspended
                : EmployeeStatuses.Active;
        }

        return episodes.Any(e => e.HireDate > date) ? EmployeeStatuses.PendingStart : EmployeeStatuses.Terminated;
    }

    /// <summary>The episode that is current, else the next future one, else the latest one.</summary>
    public static EmploymentEpisode? RelevantEpisode(IReadOnlyCollection<EmploymentEpisode> episodes, DateOnly date)
        => EpisodeOn(episodes, date)
           ?? episodes.Where(e => e.HireDate > date).OrderBy(e => e.HireDate).FirstOrDefault()
           ?? episodes.OrderByDescending(e => e.Sequence).FirstOrDefault();

    /// <summary>True if following manager links from <paramref name="managerId"/> reaches <paramref name="employeeId"/>.</summary>
    public static bool WouldCreateManagerCycle(Guid employeeId, Guid? managerId, IReadOnlyDictionary<Guid, Guid?> managers)
    {
        var seen = new HashSet<Guid>();
        var current = managerId;
        while (current is { } c)
        {
            if (c == employeeId || !seen.Add(c))
            {
                return true;
            }

            current = managers.TryGetValue(c, out var next) ? next : null;
        }

        return false;
    }
}
