using System;
using System.ComponentModel;

namespace ScpControl.Shared.Core
{
    public enum DsOffset
    {
        Pad = 0,
        State = 1,
        Battery = 2,
        Connection = 3,
        Model = 89,
        Address = 90
    };

    public enum DsState
    {
        [Description("Disconnected")]
        Disconnected = 0x00,
        [Description("Reserved")]
        Reserved = 0x01,
        [Description("Connected")]
        Connected = 0x02
    };

    /// <summary>
    ///     DualShock connection types.
    /// </summary>
    public enum DsConnection
    {
        [Description("None")]
        None = 0x00,
        [Description("Usb")]
        Usb = 0x01,
        [Description("Bluetooth")]
        Bluetooth = 0x02
    };

    /// <summary>
    ///     DualShock rechargeable battery status.
    /// </summary>
    public enum DsBattery : byte
    {
        None = 0x00,
        Dying = 0x01,
        Low = 0x02,
        Medium = 0x03,
        High = 0x04,
        Full = 0x05,
        Charging = 0xEE,
        Charged = 0xEF
    };

    public enum DsPadId : byte
    {
        None = 0xFF,
        One = 0x00,
        Two = 0x01,
        Three = 0x02,
        Four = 0x03,
        All = 0x04
    };

    /// <summary>
    ///     DualShock models.
    /// </summary>
    public enum DsModel : byte
    {
        [Description("None")]
        None = 0,
        [Description("DualShock 3")]
        DS3 = 1,
        [Description("Generic Gamepad")]
        Generic = 3
    }

    public enum DsMatch
    {
        None = 0,
        Global = 1,
        Pad = 2,
        Mac = 3
    }

}
