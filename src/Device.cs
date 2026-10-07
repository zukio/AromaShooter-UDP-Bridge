using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AromaShooterWindowsSDK;

namespace AromaShooterUdpBridge
{
    public interface IDevice
    {
        Task Connect(string transport);
        string[] Devices();
        int StopToShootDelayMs { get; }
        void Shoot(string serial, int duration, int[] chambers, int[] intensities, int internalBooster, int externalBooster);
        void Stop(string serial);
        void Disconnect();
    }

    // Accessed exclusively by ControlEngine's worker, including discovery and disconnection.
    public sealed class OfficialDevice : IDevice
    {
        private string transport;
        // 実機検証: USBは停止直後の噴射が無視され、100ms空けると動作する。
        public int StopToShootDelayMs { get { return transport == "USB" ? 100 : 0; } }
        public async Task Connect(string value)
        {
            transport = value;
            if (transport == "BLE") await AromaShooterControllerBLE.SharedInstance.ScanAndConnect().ConfigureAwait(false);
            else AromaShooterControllerUSB.SharedInstance.ScanAndConnect();
        }
        public string[] Devices()
        {
            if (transport == null) return new string[0];
            return (transport == "BLE" ? AromaShooterControllerBLE.SharedInstance.GetConnectedDevices()
                : AromaShooterControllerUSB.SharedInstance.GetConnectedDevices()).ToArray();
        }
        public void Shoot(string serial, int duration, int[] numbers, int[] intensities, int internalBooster, int externalBooster)
        {
            var chambers = new List<AromaChamber>();
            for (int i = 0; i < numbers.Length; i++) chambers.Add(new AromaChamber { number = numbers[i], concentration = intensities[i] });
            if (transport == "BLE") AromaShooterControllerBLE.SharedInstance.ShootWithIntensity(duration, chambers, internalBooster, externalBooster, serial);
            else AromaShooterControllerUSB.SharedInstance.ShootWithIntensity(duration, chambers, internalBooster, externalBooster, serial);
        }
        public void Stop(string serial)
        {
            var chambers = new[] { 1, 2, 3, 4, 5, 6 };
            if (transport == "BLE") AromaShooterControllerBLE.SharedInstance.StopWithIntensity(serial, chambers, true, true);
            else AromaShooterControllerUSB.SharedInstance.StopWithIntensity(serial, chambers, true, true);
        }
        public void Disconnect()
        {
            if (transport == "BLE") AromaShooterControllerBLE.SharedInstance.DisconnectAll();
            else if (transport == "USB") AromaShooterControllerUSB.SharedInstance.DisconnectAll();
            transport = null;
        }
    }
}
