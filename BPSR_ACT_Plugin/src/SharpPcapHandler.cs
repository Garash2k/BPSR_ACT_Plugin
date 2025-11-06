using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpPcap;
using SharpPcap.LibPcap;

namespace BPSR_ACT_Plugin.src
{
    /// <summary>
    /// Using SharpPcap, provides functionality to begin packet capture (StartListening) and forward them (OnPacketArrival).
    /// </summary>
    internal static class SharpPcapHandler
    {
        public static Action<string> OnLogStatus;
        public static event PacketArrivalEventHandler OnPacketArrival;

        private static LibPcapLiveDevice _device;

        public static void StartListening()
        {
            foreach (var devices in LibPcapLiveDeviceList.Instance)
            {
                OnLogStatus($"Found device: {devices.Name} - {devices.Description}");

                if (devices.Description.ToLower().Contains("miniport")) continue;
                if (devices.Description.ToLower().Contains("loopback")) continue;
                _device = devices;
                break;
            }

            OnLogStatus($"Using device: {_device.Name} - {_device.Description}");

            _device.Open();

            // Forward device packet events to subscribers of this class event.
            _device.OnPacketArrival += (sender, e) => OnPacketArrival?.Invoke(sender, e);

            _device.StartCapture();
        }

        public static void StopListening()
        {
            if (_device == null) return;

            try
            {
                _device.StopCapture();
                _device.Close();
            }
            catch (Exception)
            {
                // swallow or log as appropriate
            }
            finally
            {
                _device = null;
            }
        }
    }
}
