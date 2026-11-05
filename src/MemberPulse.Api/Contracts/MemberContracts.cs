using System.ComponentModel.DataAnnotations;
using MemberPulse.Domain.Accounts;

namespace MemberPulse.Api.Contracts;

public sealed class CreateMemberRequest
{
    [Required, MaxLength(100)]
    public string FirstName { get; init; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; init; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required]
    public DateOnly? DateOfBirth { get; init; }
}

public sealed class OpenAccountRequest
{
    [Required]
    public AccountType? Type { get; init; }

    [Range(0d, 1_000_000_000d)]
    public decimal InitialDeposit { get; init; }
}

public sealed record AccountDto(Guid Id, AccountType Type, decimal Balance, DateTimeOffset OpenedAt);

public sealed record MemberSummaryDto(Guid Id, string FullName, string Email, int AccountCount);

public sealed record MemberDetailDto(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    DateOnly DateOfBirth,
    int Age,
    DateTimeOffset JoinedAt,
    IReadOnlyList<AccountDto> Accounts);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);