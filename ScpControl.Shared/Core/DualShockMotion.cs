namespace ScpControl.Shared.Core
{
    public class DsAccelerometer
    {
        public ushort X { get; set; }
        public ushort Y { get; set; }
        public ushort Z { get; set; }
    }

    public class DsGyroscope
    {
        public ushort Roll { get; set; }
        public ushort Yaw { get; set; }
        public ushort Pitch { get; set; }
    }
}
