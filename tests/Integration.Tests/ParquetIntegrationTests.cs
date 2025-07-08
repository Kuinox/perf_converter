using FluentAssertions;
using PerfConverter.Entry;
using PerfConverter.PerfStructs;
using Temp.Core;
using Temp.Schema;
using Xunit;

namespace Integration.Tests;

public class ParquetIntegrationTests
{
    [Fact]
    public void Should_Create_Valid_TraceEntry_From_Sample_Data()
    {
        // Arrange & Act
        var entry = CreateTestTraceEntry(1);

        // Assert
        entry.Id.Should().Be(1);
        entry.Pid.Should().Be(1001);
        entry.Tid.Should().Be(1002);
        entry.IpSym.Should().Be("test_function_1");
        entry.IpDso.Should().Be("test.so");
        entry.HaveAddress.Should().BeTrue();
        entry.AddressSym.Should().Be("target_function_1");
    }

    [Fact]
    public void Should_Handle_TraceEntry_Without_Address()
    {
        // Arrange & Act
        var entry = new TraceEntry
        {
            Id = 99,
            PerfId = 9999,
            Pid = 500,
            Tid = 501,
            Time = 1234567890,
            IpSym = "main",
            IpDso = "program",
            HaveAddress = false // No address information
        };

        // Assert
        entry.HaveAddress.Should().BeFalse();
        entry.AddressSym.Should().BeNull();
        entry.AddressAddress.Should().Be(0);
    }

    [Fact]
    public void Should_Create_Multiple_TraceEntries_With_Unique_Ids()
    {
        // Arrange & Act
        var entries = CreateTestTraceEntries(5);

        // Assert
        entries.Should().HaveCount(5);
        entries.Select(e => e.Id).Should().OnlyHaveUniqueItems();
        entries.Select(e => e.PerfId).Should().OnlyHaveUniqueItems();
        
        // Verify the entries have different properties
        entries[0].IpSym.Should().Be("test_function_0");
        entries[1].IpSym.Should().Be("test_function_1");
        entries[4].IpSym.Should().Be("test_function_4");
    }

    [Fact]
    public void Should_Handle_TraceEntry_Edge_Cases()
    {
        // Arrange & Act
        var entry = new TraceEntry
        {
            Id = ulong.MaxValue,
            PerfId = ulong.MaxValue,
            Pid = uint.MaxValue,
            Tid = uint.MaxValue,
            Time = ulong.MaxValue,
            IpAddress = ulong.MaxValue,
            AddressAddress = ulong.MaxValue,
            IpBuildId = new byte[16], // Empty build ID
            AddressBuildId = new byte[0], // Zero-length build ID
            HaveAddress = true
        };

        // Assert
        entry.Id.Should().Be(ulong.MaxValue);
        entry.PerfId.Should().Be(ulong.MaxValue);
        entry.Pid.Should().Be(uint.MaxValue);
        entry.Tid.Should().Be(uint.MaxValue);
        entry.IpBuildId.Should().HaveCount(16);
        entry.AddressBuildId.Should().BeEmpty();
    }

    [Fact]
    public void Should_Handle_Null_String_Fields()
    {
        // Arrange & Act
        var entry = new TraceEntry
        {
            Id = 1,
            IpSym = null,
            IpDso = null,
            IpComm = null,
            AddressSym = null,
            AddressDso = null,
            AddressComm = null,
            Event = null
        };

        // Assert
        entry.IpSym.Should().BeNull();
        entry.IpDso.Should().BeNull();
        entry.IpComm.Should().BeNull();
        entry.AddressSym.Should().BeNull();
        entry.AddressDso.Should().BeNull();
        entry.AddressComm.Should().BeNull();
        entry.Event.Should().BeNull();
    }

    [Fact]
    public async Task Should_Create_And_Dispose_MockBatchPersistence()
    {
        // Arrange
        var persistence = new MockBatchPersistence();
        var testEntries = CreateTestTraceEntries(3);

        // Act
        await persistence.PersistAsync(testEntries);
        await persistence.DisposeAsync();

        // Assert
        persistence.PersistedItems.Should().HaveCount(3);
        persistence.BatchCount.Should().Be(1);
        persistence.IsDisposed.Should().BeTrue();
    }

    private static TraceEntry[] CreateTestTraceEntries(int count)
    {
        var entries = new TraceEntry[count];
        for (int i = 0; i < count; i++)
        {
            entries[i] = CreateTestTraceEntry((ulong)i);
        }
        return entries;
    }

    private static TraceEntry CreateTestTraceEntry(ulong id)
    {
        return new TraceEntry
        {
            Id = id,
            PerfId = id * 100,
            InstructionLatency = (ushort)(id % 1000),
            Pid = (uint)(1000 + id),
            Tid = (uint)(1001 + id),
            Time = 1234567890 + id * 1000,
            Cpu = (uint)(id % 8),
            Flags = (DLFilterFlag)(id % 4),
            Period = 5000 + id,
            InsnCnt = id * 10,
            CycCnt = id * 20,
            Weight = id * 5,
            Cpumode = (byte)(id % 3),
            AddrCorrelatesSym = (byte)(id % 2),
            Event = $"sample_event_{id}",
            MachinePid = (uint)(2000 + id),
            Vcpu = (uint)(id % 4),

            // IP information
            IpAddress = 0x7F000000UL + id * 0x1000,
            IpSymoff = (uint)(id % 1000),
            IpSym = $"test_function_{id}",
            IpSymStart = 0x7F000000UL + id * 0x1000,
            IpSymEnd = 0x7F000000UL + id * 0x1000 + 0xFF,
            IpDso = "test.so",
            IpSymBinding = (byte)(id % 3),
            IpIs64Bit = 1,
            IpIsKernelIp = (byte)(id % 2),
            IpBuildId = [(byte)(id % 256), 0x01, 0x02, 0x03],
            IpFiltered = 0,
            IpComm = $"process_{id}",

            // Address information
            HaveAddress = true,
            AddressAddress = 0x8F000000UL + id * 0x2000,
            AddressSymoff = (uint)(id % 500),
            AddressSym = $"target_function_{id}",
            AddressSymStart = 0x8F000000UL + id * 0x2000,
            AddressSymEnd = 0x8F000000UL + id * 0x2000 + 0x1FF,
            AddressDso = "target.so",
            AddressSymBinding = (byte)(id % 3),
            AddressIs64Bit = 1,
            AddressIsKernelIp = (byte)(id % 2),
            AddressBuildId = [0xAA, (byte)(id % 256), 0xCC, 0xDD],
            AddressFiltered = 0,
            AddressComm = $"target_process_{id}"
        };
    }

    private class MockBatchPersistence : IBatchPersistence<TraceEntry>
    {
        public List<TraceEntry> PersistedItems { get; } = new();
        public int BatchCount { get; private set; }
        public bool IsDisposed { get; private set; }

        public Task PersistAsync(IReadOnlyCollection<TraceEntry> batch)
        {
            PersistedItems.AddRange(batch);
            BatchCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}