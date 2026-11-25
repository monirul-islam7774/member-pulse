using MemberPulse.Domain.Members;

namespace MemberPulse.Domain.Segmentation;

/// <summary>A named group of members defined by criteria. A member belongs when ALL criteria hold.</summary>
public sealed class Segment
{
    private readonly List<SegmentCriterion> _criteria = [];

    private Segment() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<SegmentCriterion> Criteria => _criteria.AsReadOnly();

    public static Segment Create(string name, string? description, IEnumerable<SegmentCriterion> criteria, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(criteria);

        var segment = new Segment
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CreatedAt = createdAt,
        };
        segment._criteria.AddRange(criteria);

        if (segment._criteria.Count == 0)
        {
            throw new ArgumentException("A segment needs at least one criterion.", nameof(criteria));
        }

        return segment;
    }

    /// <summary>
    /// Member must be loaded with accounts and (at least the last 30 days of) transactions.
    /// </summary>
    public bool Matches(Member member, DateTimeOffset now) =>
        _criteria.All(c => c.IsSatisfiedBy(member, now));
}
