namespace Ingestor.Parliament;

/// <summary>
/// A register fetch did not cover every member. The run must abort: a partial dataset written
/// anywhere is worse than no run (CLAUDE.md, never-do #5).
/// </summary>
public sealed class RegisterCoverageException : Exception
{
    public RegisterCoverageException(string house, int expectedMembers, int distinctMembers)
        : base($"{house} register coverage check failed: {distinctMembers} distinct members fetched, " +
               $"totalResults is {expectedMembers}. Aborting rather than proceeding with a partial set.")
    {
        House = house;
        ExpectedMembers = expectedMembers;
        DistinctMembers = distinctMembers;
    }

    /// <summary><c>commons</c> or <c>lords</c>.</summary>
    public string House { get; }

    /// <summary>The <c>totalResults</c> the API reported.</summary>
    public int ExpectedMembers { get; }

    /// <summary>Distinct member IDs actually seen across all pages.</summary>
    public int DistinctMembers { get; }
}
