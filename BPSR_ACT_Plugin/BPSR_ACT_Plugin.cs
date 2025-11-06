using System;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using BPSR_ACT_Plugin.src;

namespace BPSR_ACT_Plugin
{
    /// <summary>
    /// Entry point for our plugin.
    /// </summary>
    public class BPSR_ACT_Plugin : IActPluginV1
    {
        static BPSR_ACT_Plugin()
        {
            AppDomain.CurrentDomain.AssemblyResolve += AssemblyHelper.CurrentDomain_AssemblyResolve;
        }

        private Label _pluginStatusLabel;
        private TextBox _statusLogBox;

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            _pluginStatusLabel = pluginStatusText;
            _pluginStatusLabel.Text = "Initializing BPSR_ACT_Plugin...";

            InitStatusBox(pluginScreenSpace);

            SharpPcapHandler.OnLogStatus += LogStatus;
            PacketCaptureHandler.OnLogStatus += LogStatus;
            BPSRPacketHandler.OnLogStatus += LogStatus;
            TcpReassembler.OnLogStatus += LogStatus;

            SharpPcapHandler.OnPacketArrival += PacketCaptureHandler.PacketArrival;
            PacketCaptureHandler.OnPayloadReady += BPSRPacketHandler.PayloadReady;
            BPSRPacketHandler.OnLogMasterSwing += ACTLogHandler.LogMasterSwing;

            //TODO: Set correct zone when possible
            ActGlobals.oFormActMain.ChangeZone("Blue Protocol: Star Resonnance");

            SharpPcapHandler.StartListening();

            _pluginStatusLabel.Text = "BPSR_ACT_Plugin initialized.";
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
            this._statusLogBox = statusBox;
        }

        public void LogStatus(string message)
        {
            _statusLogBox?.AppendText(message + "\r\n");
        }

        public void DeInitPlugin()
        {
            SharpPcapHandler.StopListening();
            _pluginStatusLabel.Text = "BPSR_ACT_Plugin stopped.";
        }
    }
}
