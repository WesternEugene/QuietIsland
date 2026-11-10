using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;

public class BluetoothBatteryTests
{
    [Fact]
    public void Cached_windows_battery_never_makes_an_offline_device_visible()
    {
        BluetoothBatteryEndpoint[] endpoints = [new("mouse", "HUAWEI Mouse CD23", "container", false, null, BluetoothDeviceKind.Mouse)];
        BluetoothBatteryNode[] nodes = [new("container", 76, BluetoothDeviceKind.Mouse)];
        Assert.Empty(BluetoothBatteryPolicy.Select(endpoints, nodes));
        Assert.Equal(76, Assert.Single(BluetoothBatteryPolicy.Select([endpoints[0] with { Connected = true }], nodes)).Percent);
        Assert.Empty(BluetoothBatteryPolicy.Select([], nodes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(255)]
    public void Unsupported_or_unknown_batteries_are_hidden(int? percent)
    {
        var endpoint = new BluetoothBatteryEndpoint("mouse", "Mouse", "container", true, percent, BluetoothDeviceKind.Mouse);
        Assert.Empty(BluetoothBatteryPolicy.Select([endpoint], [new("container", percent, BluetoothDeviceKind.Mouse)]));
    }

    [Theory]
    [InlineData(0, BatteryChargeBand.Low)]
    [InlineData(1, BatteryChargeBand.Low)]
    [InlineData(30, BatteryChargeBand.Low)]
    [InlineData(31, BatteryChargeBand.Medium)]
    [InlineData(50, BatteryChargeBand.Medium)]
    [InlineData(51, BatteryChargeBand.High)]
    [InlineData(100, BatteryChargeBand.High)]
    public void Charge_color_obeys_the_requested_boundaries(int percent, BatteryChargeBand expected)
    {
        var device = Assert.Single(BluetoothBatteryPolicy.Select([new("id", "Mouse", null, true, percent, BluetoothDeviceKind.Mouse)], []));
        Assert.Equal(expected, device.Band);
        Assert.Equal(percent / 100d, device.Progress);
    }

    [Fact]
    public void One_physical_device_has_one_row_across_classic_and_le()
    {
        BluetoothBatteryEndpoint[] endpoints = [
            new("classic", "Headphones", "SAME", false, 99, BluetoothDeviceKind.Headphones),
            new("le", "Headphones", "same", true, null, BluetoothDeviceKind.Unknown),
            new("mouse", "Mouse", "other", true, 64, BluetoothDeviceKind.Mouse)];
        var devices = BluetoothBatteryPolicy.Select(endpoints, [new("same", 42, BluetoothDeviceKind.Headphones)]);
        Assert.Equal(2, devices.Count);
        var headphones = Assert.Single(devices, d => d.Name == "Headphones");
        Assert.Equal(42, headphones.Percent);
        Assert.Equal(BluetoothDeviceKind.Headphones, headphones.Kind);
        Assert.Equal("same", headphones.Id, ignoreCase: true);
    }

    [Theory]
    [InlineData("Input.Mouse", null, null, null, null, BluetoothDeviceKind.Mouse)]
    [InlineData(null, 5, 32, null, null, BluetoothDeviceKind.Mouse)]
    [InlineData(null, 5, 16, null, null, BluetoothDeviceKind.Keyboard)]
    [InlineData(null, 4, 6, null, null, BluetoothDeviceKind.Headphones)]
    [InlineData(null, 4, 1, null, null, BluetoothDeviceKind.Headphones)]
    [InlineData(null, 4, 5, null, null, BluetoothDeviceKind.Speaker)]
    [InlineData(null, null, null, 15, 2, BluetoothDeviceKind.Mouse)]
    [InlineData(null, null, null, 15, 1, BluetoothDeviceKind.Keyboard)]
    [InlineData(null, null, null, 15, 4, BluetoothDeviceKind.Gamepad)]
    [InlineData(null, null, null, 1, 0, BluetoothDeviceKind.Phone)]
    [InlineData(null, null, null, 3, 0, BluetoothDeviceKind.Watch)]
    public void Windows_metadata_selects_the_corresponding_device_icon(string? category, int? major, int? minor, int? appearance, int? subcategory, BluetoothDeviceKind expected)
        => Assert.Equal(expected, BluetoothBatteryPolicy.Classify(category == null ? [] : [category], major, minor, appearance, subcategory));

    [Theory]
    [InlineData("Input.Mouse", BluetoothDeviceKind.Mouse)]
    [InlineData("Input.Keyboard", BluetoothDeviceKind.Keyboard)]
    [InlineData("Audio.Headphone", BluetoothDeviceKind.Headphones)]
    [InlineData("Audio.Speakers", BluetoothDeviceKind.Speaker)]
    [InlineData("Input.Gaming", BluetoothDeviceKind.Gamepad)]
    public void Connected_supported_types_are_visible_and_offline_or_unknown_battery_types_are_hidden(string category, BluetoothDeviceKind kind)
    {
        var classified = BluetoothBatteryPolicy.Classify([category]);
        Assert.Equal(kind, classified);
        var endpoint = new BluetoothBatteryEndpoint("peer", "Device", "container", true, null, BluetoothDeviceKind.Unknown);
        var battery = new BluetoothBatteryNode("container", 67, classified);
        var device = Assert.Single(BluetoothBatteryPolicy.Select([endpoint], [battery]));
        Assert.Equal(kind, device.Kind); Assert.Equal(67, device.Percent);
        Assert.Empty(BluetoothBatteryPolicy.Select([endpoint with { Connected = false }], [battery]));
        Assert.Empty(BluetoothBatteryPolicy.Select([endpoint], [battery with { Percent = null }]));
    }

    [Theory]
    [InlineData("4d36e96b-e325-11ce-bfc1-08002be10318", BluetoothDeviceKind.Keyboard)]
    [InlineData("4d36e96f-e325-11ce-bfc1-08002be10318", BluetoothDeviceKind.Mouse)]
    public void Hid_class_identifies_input_devices_when_the_bluetooth_appearance_is_unknown(string classId, BluetoothDeviceKind expected)
        => Assert.Equal(expected, BluetoothBatteryPolicy.Classify(["Unknown"], appearance: 0, subcategory: 0, deviceClass: Guid.Parse(classId)));

    [Fact]
    public void A_headset_with_an_auxiliary_hid_keyboard_interface_keeps_its_headphone_icon()
    {
        var endpoint = new BluetoothBatteryEndpoint("headset", "Headset", "container", true, null, BluetoothDeviceKind.Headphones);
        var device = Assert.Single(BluetoothBatteryPolicy.Select([endpoint], [new("container", 60, BluetoothDeviceKind.Keyboard)]));
        Assert.Equal(BluetoothDeviceKind.Headphones, device.Kind);
    }
}
