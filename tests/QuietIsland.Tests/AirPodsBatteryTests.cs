using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;

public class AirPodsBatteryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 19, 0, 0, TimeSpan.Zero);
    private static readonly AirPodsBatteryPeer Peer = new("own-container", "My AirPods Pro", 0x2014, 0x123456789ABC, true);

    [Theory]
    [InlineData(0xA8, 90)]
    [InlineData(0x58, 65)]
    [InlineData(0xAF, 100)]
    [InlineData(0xF3, 30)]
    [InlineData(0x00, 0)]
    public void One_percentage_is_the_mean_of_the_available_earbud_readings(int batteries, int expected)
    {
        Assert.True(AirPodsBatteryProtocol.TryRead(Packet((byte)batteries), out var reading));
        Assert.Equal((ushort)0x2014, reading!.ModelId); Assert.Equal(expected, reading.Percent);
    }

    [Fact]
    public void Case_battery_and_left_right_orientation_never_change_the_pair_percentage()
    {
        var first = Packet(0x58); var second = Packet(0x58);
        first[7] = 0x00; second[7] = 0xFA; second[5] ^= 0x40;
        Assert.True(AirPodsBatteryProtocol.TryRead(first, out var a));
        Assert.True(AirPodsBatteryProtocol.TryRead(second, out var b));
        Assert.Equal(65, a!.Percent); Assert.Equal(65, b!.Percent);
    }

    [Theory]
    [InlineData(0xFF)]
    [InlineData(0xB7)]
    [InlineData(0xCD)]
    public void Unknown_or_invalid_battery_packets_do_not_create_a_percentage(int battery)
        => Assert.False(AirPodsBatteryProtocol.TryRead(Packet((byte)battery), out _));

    [Fact]
    public void Other_apple_messages_truncated_packets_and_non_airpods_models_are_rejected()
    {
        var bytes = Packet(0x88);
        Assert.False(AirPodsBatteryProtocol.TryRead(bytes.AsSpan(0, 12), out _));
        bytes[0] = 0x10; Assert.False(AirPodsBatteryProtocol.TryRead(bytes, out _));
        bytes[0] = 7; bytes[3] = 0x06; Assert.False(AirPodsBatteryProtocol.TryRead(bytes, out _)); // Beats, outside requested scope.
    }

    [Fact]
    public void Exact_address_can_select_the_connected_pair_among_multiple_nearby_pairs()
    {
        var cache = new AirPodsBatteryCache();
        cache.Observe(Peer.Address, -85, Packet(0xA8), Now);
        cache.Observe(0xFFEEDDCCBBAA, -25, Packet(0x22), Now);
        var device = Assert.Single(cache.Resolve([Peer], Now));
        Assert.Equal(Peer.Id, device.Id); Assert.Equal(90, device.Percent); Assert.Equal(BluetoothDeviceKind.Headphones, device.Kind);
        cache.Observe(0xFFEEDDCCBBAA, -25, Packet(0x22), Now.AddSeconds(121));
        Assert.Empty(cache.Resolve([Peer], Now.AddSeconds(121)));
        Assert.Empty(cache.Resolve([Peer with { Connected = false }], Now.AddSeconds(122)));
    }

    [Fact]
    public void A_unique_nearby_matching_model_can_be_bound_despite_its_random_ble_address()
    {
        var cache = new AirPodsBatteryCache();
        cache.Observe(0xAABBCCDDEEFF, -40, Packet(0x58), Now);
        Assert.Equal(65, Assert.Single(cache.Resolve([Peer], Now)).Percent);
        Assert.Empty(cache.Resolve([Peer], Now.AddMinutes(3)));
    }

    [Fact]
    public void Ambiguous_nearby_models_or_multiple_connected_pairs_are_not_guessed()
    {
        var cache = new AirPodsBatteryCache();
        cache.Observe(1, -40, Packet(0x88), Now); cache.Observe(2, -41, Packet(0x66), Now);
        Assert.Empty(cache.Resolve([Peer], Now));
        cache.Clear(); cache.Observe(1, -40, Packet(0x88), Now);
        Assert.Empty(cache.Resolve([Peer, Peer with { Id = "other", Address = 3 }], Now));
        Assert.Empty(cache.Resolve([Peer with { ModelId = 0x200E }], Now));
    }

    [Fact]
    public void A_new_nearby_pair_cannot_take_over_an_existing_private_address_binding()
    {
        var cache = new AirPodsBatteryCache();
        cache.Observe(1, -40, Packet(0x88), Now);
        Assert.Equal(80, Assert.Single(cache.Resolve([Peer], Now)).Percent);
        cache.Observe(2, -41, Packet(0x22), Now.AddSeconds(15));
        Assert.Empty(cache.Resolve([Peer], Now.AddSeconds(15)));
        cache.Observe(2, -41, Packet(0x22), Now.AddSeconds(121));
        Assert.Empty(cache.Resolve([Peer], Now.AddSeconds(121)));
        Assert.Empty(cache.Resolve([Peer with { Connected = false }], Now.AddSeconds(122)));
    }

    [Fact]
    public void A_bound_reading_expires_and_neither_weak_nor_old_samples_create_a_new_binding()
    {
        var cache = new AirPodsBatteryCache();
        cache.Observe(1, -80, Packet(0x88), Now);
        Assert.Empty(cache.Resolve([Peer], Now));
        cache.Observe(1, -40, Packet(0x88), Now);
        Assert.Empty(cache.Resolve([Peer], Now.AddSeconds(11)));
        Assert.Single(cache.Resolve([Peer], Now.AddSeconds(5)));
        Assert.Single(cache.Resolve([Peer], Now.AddSeconds(110)));
        Assert.Empty(cache.Resolve([Peer], Now.AddSeconds(121)));
    }

    [Fact]
    public void Apple_message_sequences_are_parsed_without_reading_case_battery_as_earbuds()
    {
        byte[] otherMessage = [0x10, 0x02, 0x00, 0x00];
        var combined = otherMessage.Concat(Packet(0x58)).ToArray();
        Assert.True(AirPodsBatteryProtocol.TryRead(combined, out var reading));
        Assert.Equal(65, reading!.Percent);
        var caseOnly = Packet(0xFF); caseOnly[7] = 0x0A;
        Assert.False(AirPodsBatteryProtocol.TryRead(caseOnly, out _));
        Assert.Equal("≈65%", new BluetoothBatteryDevice("id", "AirPods", reading.Percent, BluetoothDeviceKind.Headphones) { IsApproximate = true }.ChargeLabel);
    }

    [Theory]
    [InlineData("BTHENUM\\{SERVICE}_VID&0001004C_PID&2014", 0x2014)]
    [InlineData("BTHENUM\\{SERVICE}_VID&000205AC_PID&2024", 0x2024)]
    [InlineData("BTHENUM\\{SERVICE}_VID&00010057_PID&2014", null)]
    public void The_windows_vendor_and_product_identify_airpods_even_after_a_rename(string id, int? expected)
        => Assert.Equal(expected, (int?)AirPodsBatteryProtocol.ModelFromWindowsId(id));

    private static byte[] Packet(byte batteries)
    {
        // Apple manufacturer data, proximity tag 07, 25-byte payload, Pro 2 model 2014.
        var bytes = new byte[27]; bytes[0] = 7; bytes[1] = 25; bytes[2] = 1;
        bytes[3] = 0x14; bytes[4] = 0x20; bytes[5] = 0x55; bytes[6] = batteries; bytes[7] = 0xF7;
        return bytes;
    }
}
