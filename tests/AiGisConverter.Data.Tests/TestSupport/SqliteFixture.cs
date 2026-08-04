using AiGisConverter.Data.Abstractions;
using AiGisConverter.Data.Context;
using AiGisConverter.Data.UnitOfWork;
using AiGisConverter.Domain.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Data.Tests.TestSupport;

/// <summary>
/// A real SQLite database, held in memory for the life of one test.
/// </summary>
/// <remarks>
/// SQLite rather than the in-memory provider. The in-memory provider is not relational: it has no
/// transactions, no foreign keys and no SQL translation, so a test passing against it says nothing
/// about whether the mapping works. An in-memory SQLite connection is the real engine and is
/// discarded when the connection closes.
/// </remarks>
internal sealed class SqliteFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteFixture()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        DbContextOptions<AiGisConverterDbContext> options =
            new DbContextOptionsBuilder<AiGisConverterDbContext>()
                .UseSqlite(_connection)
                .Options;

        Context = new AiGisConverterDbContext(options);
        Context.Database.EnsureCreated();

        Dispatcher = new RecordingDispatcher();
        UnitOfWork = new UnitOfWork.UnitOfWork(Context, Dispatcher, NullLogger<UnitOfWork.UnitOfWork>.Instance);
    }

    public AiGisConverterDbContext Context { get; }

    public RecordingDispatcher Dispatcher { get; }

    public UnitOfWork.UnitOfWork UnitOfWork { get; }

    /// <summary>Opens a second context over the same database, to prove a round trip.</summary>
    /// <remarks>
    /// Reading back through the context that wrote would only prove the change tracker remembers.
    /// </remarks>
    public AiGisConverterDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AiGisConverterDbContext>().UseSqlite(_connection).Options);

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }

    internal sealed class RecordingDispatcher : IDomainEventDispatcher
    {
        public List<IDomainEvent> Dispatched { get; } = [];

        public Task DispatchAsync(
            IReadOnlyList<IDomainEvent> domainEvents,
            CancellationToken cancellationToken = default)
        {
            Dispatched.AddRange(domainEvents);

            return Task.CompletedTask;
        }
    }
}
