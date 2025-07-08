using FluentAssertions;
using PerfConverter.Entry;
using Xunit;

namespace PerfConverter.Tests;

public class TraceEntryTests
{
    [Fact]
    public void TraceEntry_Should_Initialize_With_Default_Values()
    {
        // Arrange & Act
        var entry = new TraceEntry();

        // Assert
        entry.Id.Should().Be(0);
        entry.Pid.Should().Be(0);
        entry.Tid.Should().Be(0);
        entry.HaveAddress.Should().BeFalse();
        entry.IpSym.Should().BeNull();
        entry.AddressSym.Should().BeNull();
    }

    [Fact]
    public void TraceEntry_Should_Store_Basic_Properties()
    {
        // Arrange & Act
        var entry = new TraceEntry
        {
            Id = 12345,
            PerfId = 67890,
            Pid = 1001,
            Tid = 2002,
            Time = 1234567890,
            Cpu = 4,
            Period = 5000,
            InsnCnt = 100,
            CycCnt = 200,
            Weight = 300,
            Cpumode = 1,
            AddrCorrelatesSym = 1,
            Event = "sample_event",
            MachinePid = 3003,
            Vcpu = 1
        };

        // Assert
        entry.Id.Should().Be(12345);
        entry.PerfId.Should().Be(67890);
        entry.Pid.Should().Be(1001);
        entry.Tid.Should().Be(2002);
        entry.Time.Should().Be(1234567890);
        entry.Cpu.Should().Be(4);
        entry.Period.Should().Be(5000);
        entry.InsnCnt.Should().Be(100);
        entry.CycCnt.Should().Be(200);
        entry.Weight.Should().Be(300);
        entry.Cpumode.Should().Be(1);
        entry.AddrCorrelatesSym.Should().Be(1);
        entry.Event.Should().Be("sample_event");
        entry.MachinePid.Should().Be(3003);
        entry.Vcpu.Should().Be(1);
    }

    [Fact]
    public void TraceEntry_Should_Store_IP_Information()
    {
        // Arrange & Act
        var buildId = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var entry = new TraceEntry
        {
            IpAddress = 0x7F123456789ABCUL,
            IpSymoff = 100,
            IpSym = "test_function",
            IpSymStart = 0x7F123456789A00UL,
            IpSymEnd = 0x7F123456789AFFUL,
            IpDso = "libc.so.6",
            IpSymBinding = 1,
            IpIs64Bit = 1,
            IpIsKernelIp = 0,
            IpBuildId = buildId,
            IpFiltered = 0,
            IpComm = "test_process"
        };

        // Assert
        entry.IpAddress.Should().Be(0x7F123456789ABCUL);
        entry.IpSymoff.Should().Be(100);
        entry.IpSym.Should().Be("test_function");
        entry.IpSymStart.Should().Be(0x7F123456789A00UL);
        entry.IpSymEnd.Should().Be(0x7F123456789AFFUL);
        entry.IpDso.Should().Be("libc.so.6");
        entry.IpSymBinding.Should().Be(1);
        entry.IpIs64Bit.Should().Be(1);
        entry.IpIsKernelIp.Should().Be(0);
        entry.IpBuildId.Should().BeEquivalentTo(buildId);
        entry.IpFiltered.Should().Be(0);
        entry.IpComm.Should().Be("test_process");
    }

    [Fact]
    public void TraceEntry_Should_Store_Address_Information()
    {
        // Arrange & Act
        var buildId = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var entry = new TraceEntry
        {
            HaveAddress = true,
            AddressAddress = 0x7F987654321000UL,
            AddressSymoff = 200,
            AddressSym = "target_function",
            AddressSymStart = 0x7F987654321000UL,
            AddressSymEnd = 0x7F9876543210FFUL,
            AddressDso = "libtest.so",
            AddressSymBinding = 2,
            AddressIs64Bit = 1,
            AddressIsKernelIp = 1,
            AddressBuildId = buildId,
            AddressFiltered = 0,
            AddressComm = "target_process"
        };

        // Assert
        entry.HaveAddress.Should().BeTrue();
        entry.AddressAddress.Should().Be(0x7F987654321000UL);
        entry.AddressSymoff.Should().Be(200);
        entry.AddressSym.Should().Be("target_function");
        entry.AddressSymStart.Should().Be(0x7F987654321000UL);
        entry.AddressSymEnd.Should().Be(0x7F9876543210FFUL);
        entry.AddressDso.Should().Be("libtest.so");
        entry.AddressSymBinding.Should().Be(2);
        entry.AddressIs64Bit.Should().Be(1);
        entry.AddressIsKernelIp.Should().Be(1);
        entry.AddressBuildId.Should().BeEquivalentTo(buildId);
        entry.AddressFiltered.Should().Be(0);
        entry.AddressComm.Should().Be("target_process");
    }
}