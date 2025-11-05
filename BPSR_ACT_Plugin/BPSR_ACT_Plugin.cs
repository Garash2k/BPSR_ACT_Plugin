using System;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using BPSR_ACT_Plugin.src;

namespace BPSR_ACT_Plugin
{
    public class BPSR_ACT_Plugin : IActPluginV1
    {
        private Label pluginStatusLabel;
        private TextBox statusBox;

        static BPSR_ACT_Plugin()
        {
            AppDomain.CurrentDomain.AssemblyResolve += AssemblyHelper.CurrentDomain_AssemblyResolve;
        }

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            pluginStatusLabel = pluginStatusText;
            pluginStatusLabel.Text = "Initializing BPSR_ACT_Plugin...";

            InitStatusBox(pluginScreenSpace);

            SharpPcapHandler.OnLogStatus += LogStatus;
            PacketCaptureHelper.OnLogStatus += LogStatus;
            BPSRPacketHandler.OnLogStatus += LogStatus;

            SharpPcapHandler.OnPacketArrival += PacketCaptureHelper.Device_OnPacketArrival;
            PacketCaptureHelper.OnPayloadReady += BPSRPacketHandler.OnPayloadReady;
            BPSRPacketHandler.OnLogMasterSwing += ACTLogHelper.LogMasterSwing;

            SharpPcapHandler.StartListening();

            pluginStatusLabel.Text = "BPSR_ACT_Plugin initialized.";
            LogStatus("Plugin initialized.");
        }

        private void InitStatusBox(TabPage pluginScreenSpace)
        {
            // Minimal UI controls
            var panel = new Panel { Dock = DockStyle.Fill };
            var infoLabel = new Label
            {
                Text = "Blue Protocol: Star Resonance Parser Plugin",
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new System.Drawing.Font("Segoe UI", 12, System.Drawing.FontStyle.Bold),
                Padding = new Padding(10)
            };
            var statusBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new System.Drawing.Font("Consolas", 10),
                BackColor = System.Drawing.Color.Black,
                ForeColor = System.Drawing.Color.Lime
            };
            panel.Controls.Add(statusBox);
            panel.Controls.Add(infoLabel);
            pluginScreenSpace.Controls.Add(panel);

            // Save reference for status updates
            this.statusBox = statusBox;
        }

        public void LogStatus(string message)
        {
            statusBox?.AppendText(message + "\r\n");
        }

        public void DeInitPlugin()
        {
            SharpPcapHandler.StopListening();
            pluginStatusLabel.Text = "BPSR_ACT_Plugin stopped.";
        }
    }
}
