using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Infrastructure.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Testy helpera ponawiającego przy konflikcie optymistycznej współbieżności.
/// Czyste — bez bazy; symulują DbUpdateConcurrencyException.
/// </summary>
public class ConcurrencyRetryTests
{
    [Fact]
    public async Task Succeeds_On_First_Attempt_Without_Retry()
    {
        var attempts = 0;
        await ConcurrencyRetry.ExecuteAsync(() => { attempts++; return Task.CompletedTask; });
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task Retries_Then_Succeeds()
    {
        var attempts = 0;
        await ConcurrencyRetry.ExecuteAsync(() =>
        {
            attempts++;
            if (attempts < 3) throw new DbUpdateConcurrencyException();
            return Task.CompletedTask;
        });
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task Gives_Up_After_MaxAttempts_And_Rethrows()
    {
        var attempts = 0;
        var act = () => ConcurrencyRetry.ExecuteAsync(() =>
        {
            attempts++;
            throw new DbUpdateConcurrencyException();
        }, maxAttempts: 3);

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        attempts.Should().Be(3); // ostatnia próba przepuszcza wyjątek
    }

    [Fact]
    public async Task Does_Not_Swallow_Other_Exceptions()
    {
        var attempts = 0;
        var act = () => ConcurrencyRetry.ExecuteAsync(() =>
        {
            attempts++;
            throw new InvalidOperationException("inny błąd");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        attempts.Should().Be(1); // nie ponawiamy błędów niezwiązanych ze współbieżnością
    }
}
