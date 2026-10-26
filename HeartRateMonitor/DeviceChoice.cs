using Windows.Devices.Bluetooth;

namespace HeartRateMonitor
{
    public sealed class DeviceChoice
    {
        public ulong Address { get; set; }
        public string Name { get; set; }
        public bool HasHeartRateService { get; set; }
        public string DeviceId { get; set; }
        public string ServiceId { get; set; }
        public BluetoothAddressType? AddressType { get; set; }
        internal bool Matches(DeviceChoice other)
        {
            return other != null && ((Address != 0 && Address == other.Address) ||
                (!string.IsNullOrEmpty(DeviceId) && DeviceId == other.DeviceId) ||
                (!string.IsNullOrEmpty(ServiceId) && ServiceId == other.ServiceId));
        }
        public override string ToString() => Address == 0 ? Name + Words.T(" · Windows登録済み", " · Registered in Windows") : $"{Name}  ·  {Address:X12}";
    }
}
