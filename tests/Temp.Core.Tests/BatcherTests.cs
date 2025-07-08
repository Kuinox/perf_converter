using FluentAssertions;
using Temp.Core;
using Xunit;

namespace Temp.Core.Tests;

public class BatcherTests
{
    private class TestItem
    {
        public int Id { get; set; }
        public string? Value { get; set; }
    }

    private class TestBatchPersistence : IBatchPersistence<TestItem>
    {
        public List<TestItem> PersistedItems { get; } = new();
        public int BatchCount { get; private set; }

        public Task PersistAsync(IReadOnlyCollection<TestItem> batch)
        {
            PersistedItems.AddRange(batch);
            BatchCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Batcher_Should_Persist_Single_Item()
    {
        // Arrange
        var persistence = new TestBatchPersistence();
        var batcher = Batcher<TestItem>.Create(persistence, batchSize: 10, BatchingMode.OnFull);
        var testItem = new TestItem { Id = 1, Value = "Test" };

        // Act
        batcher.Persist(testItem);
        await batcher.DisposeAsync();

        // Assert
        persistence.PersistedItems.Should().HaveCount(1);
        persistence.PersistedItems[0].Id.Should().Be(1);
        persistence.PersistedItems[0].Value.Should().Be("Test");
    }

    [Fact]
    public async Task Batcher_Should_Batch_Multiple_Items()
    {
        // Arrange
        var persistence = new TestBatchPersistence();
        var batcher = Batcher<TestItem>.Create(persistence, batchSize: 3, BatchingMode.OnFull);

        // Act
        batcher.Persist(new TestItem { Id = 1, Value = "Test1" });
        batcher.Persist(new TestItem { Id = 2, Value = "Test2" });
        batcher.Persist(new TestItem { Id = 3, Value = "Test3" });
        
        // Give some time for async processing
        await Task.Delay(100);
        
        await batcher.DisposeAsync();

        // Assert
        persistence.PersistedItems.Should().HaveCount(3);
        persistence.BatchCount.Should().Be(1);
    }

    [Fact]
    public async Task Batcher_Should_Handle_Partial_Batch_On_Dispose()
    {
        // Arrange
        var persistence = new TestBatchPersistence();
        var batcher = Batcher<TestItem>.Create(persistence, batchSize: 5, BatchingMode.OnFull);

        // Act
        batcher.Persist(new TestItem { Id = 1, Value = "Test1" });
        batcher.Persist(new TestItem { Id = 2, Value = "Test2" });
        await batcher.DisposeAsync();

        // Assert
        persistence.PersistedItems.Should().HaveCount(2);
        persistence.BatchCount.Should().Be(1);
    }

    [Fact]
    public async Task Batcher_Should_Handle_Empty_Disposal()
    {
        // Arrange
        var persistence = new TestBatchPersistence();
        var batcher = Batcher<TestItem>.Create(persistence, batchSize: 5, BatchingMode.OnFull);

        // Act
        await batcher.DisposeAsync();

        // Assert
        persistence.PersistedItems.Should().BeEmpty();
        persistence.BatchCount.Should().Be(0);
    }
}