﻿using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using log4net;
using ScpControl;
using ScpControl.Bluetooth;
using ScpControl.Exceptions;
using ScpControl.Shared.Core;
using ScpControl.Usb.Ds3;
using ScpControl.Usb.Gamepads;
using ScpControl.Utilities;
using ScpServer.Properties;

namespace ScpServer
{
    public partial class ScpForm : Form
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private readonly RadioButton[] Pad = new RadioButton[4];
        private IntPtr m_BthNotify = IntPtr.Zero;
        private IntPtr m_Ds3Notify = IntPtr.Zero;
        private IntPtr m_Ds4Notify = IntPtr.Zero;
        private IntPtr _genericNotify = IntPtr.Zero;

        public ScpForm()
        {
            InitializeComponent();

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                Log.FatalFormat("An unhandled exception occured: {0}", args.ExceptionObject);
            };

            ThemeUtil.SetTheme(lvDebug);

            Pad[0] = rbPad_1;
            Pad[1] = rbPad_2;
            Pad[2] = rbPad_3;
            Pad[3] = rbPad_4;
        }

        private void Form_Load(object sender, EventArgs e)
        {
            Icon = Resources.Scp_All;

            ScpDevice.RegisterNotify(Handle, UsbDs3.DeviceClassGuid, ref m_Ds3Notify);
            ScpDevice.RegisterNotify(Handle, BthDongle.DeviceClassGuid, ref m_BthNotify);
            ScpDevice.RegisterNotify(Handle, UsbGenericGamepad.DeviceClassGuid, ref _genericNotify);

            Log.DebugFormat("++ {0} [{1}]", Assembly.GetExecutingAssembly().Location,
                Assembly.GetExecutingAssembly().GetName().Version);

            tmrUpdate.Enabled = true;
            btnStart_Click(sender, e);
        }

        private void Form_Close(object sender, FormClosingEventArgs e)
        {
            rootHub.Close();

            if (m_Ds3Notify != IntPtr.Zero) ScpDevice.UnregisterNotify(m_Ds3Notify);
            if (m_Ds4Notify != IntPtr.Zero) ScpDevice.UnregisterNotify(m_Ds4Notify);
            if (m_BthNotify != IntPtr.Zero) ScpDevice.UnregisterNotify(m_BthNotify);
            if (_genericNotify != IntPtr.Zero) ScpDevice.UnregisterNotify(_genericNotify);
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            try
            {
                if (rootHub.Open() && rootHub.Start())
                {
                    btnStart.Enabled = false;
                    btnStop.Enabled = true;
                }
            }
            catch (RootHubAlreadyStartedException rhex)
            {
                Log.Fatal(rhex.Message);
                MessageBox.Show(rhex.Message, "Error starting server", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            if (rootHub.Stop())
            {
                btnStart.Enabled = true;
                btnStop.Enabled = false;
            }
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            lvDebug.Items.Clear();
        }

        private void btnMotor_Click(object sender, EventArgs e)
        {
            var Target = (Button) sender;
            byte Left = 0x00, Right = 0x00;

            if (Target == btnBoth)
            {
                Left = 0xFF;
                Right = 0xFF;
            }
            else if (Target == btnLeft) Left = 0xFF;
            else if (Target == btnRight) Right = 0xFF;

            for (var Index = 0; Index < 4; Index++)
            {
                if (Pad[Index].Enabled && Pad[Index].Checked)
                {
                    rootHub.Pads[Index].Rumble(Left, Right);
                }
            }
        }

        private void btnPair_Click(object sender, EventArgs e)
        {
            for (var Index = 0; Index < Pad.Length; Index++)
            {
                if (Pad[Index].Checked)
                {
                    rootHub.Pads[Index].Pair(rootHub.BluetoothHostAddress);
                    break;
                }
            }
        }

        protected void btnDisconnect_Click(object sender, EventArgs e)
        {
            for (var index = 0; index < Pad.Length; index++)
            {
                if (Pad[index].Checked)
                {
                    rootHub.Pads[index].Disconnect();
                    break;
                }
            }
        }

        protected void btnSuspend_Click(object sender, EventArgs e)
        {
            rootHub.Suspend();
        }

        protected void btnResume_Click(object sender, EventArgs e)
        {
            rootHub.Resume();
        }

        protected override void WndProc(ref Message m)
        {
            try
            {
                if (m.Msg == ScpDevice.WM_DEVICECHANGE)
                {
                    var type = m.WParam.ToInt32();

                    switch (type)
                    {
                        case ScpDevice.DBT_DEVICEARRIVAL:
                        case ScpDevice.DBT_DEVICEQUERYREMOVE:
                        case ScpDevice.DBT_DEVICEREMOVECOMPLETE:

                            ScpDevice.DEV_BROADCAST_HDR hdr;

                            hdr =
                                (ScpDevice.DEV_BROADCAST_HDR)
                                    Marshal.PtrToStructure(m.LParam, typeof (ScpDevice.DEV_BROADCAST_HDR));

                            if (hdr.dbch_devicetype == ScpDevice.DBT_DEVTYP_DEVICEINTERFACE)
                            {
                                ScpDevice.DEV_BROADCAST_DEVICEINTERFACE_M deviceInterface;

                                deviceInterface =
                                    (ScpDevice.DEV_BROADCAST_DEVICEINTERFACE_M)
                                        Marshal.PtrToStructure(m.LParam,
                                            typeof (ScpDevice.DEV_BROADCAST_DEVICEINTERFACE_M));

                                var Class = "{" + new Guid(deviceInterface.dbcc_classguid).ToString().ToUpper() + "}";

                                var path = new string(deviceInterface.dbcc_name);
                                path = path.Substring(0, path.IndexOf('\0')).ToUpper();

                                rootHub.Notify((ScpDevice.Notified) type, Class, path);
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorFormat("Unexpected error while processing window messages: {0}", ex);
            }

            base.WndProc(ref m);
        }

        private void tmrUpdate_Tick(object sender, EventArgs e)
        {
            bool bSelected = false, bDisconnect = false, bPair = false;

            lblHost.Text = rootHub.Dongle;
            lblHost.Enabled = btnStop.Enabled;

            for (var index = 0; index < Pad.Length; index++)
            {
                Pad[index].Text = rootHub.Pads[index].ToString();
                Pad[index].Enabled = rootHub.Pads[index].State == DsState.Connected;
                Pad[index].Checked = Pad[index].Enabled && Pad[index].Checked;

                bSelected = bSelected || Pad[index].Checked;
                bDisconnect = bDisconnect || rootHub.Pads[index].Connection == DsConnection.Bluetooth;

                bPair = bPair ||
                        (Pad[index].Checked && rootHub.Pads[index].Connection == DsConnection.Usb &&
                         rootHub.BluetoothHostAddress != null
                         && !rootHub.BluetoothHostAddress.Equals(rootHub.Pads[index].HostAddress));
            }

            btnBoth.Enabled = btnLeft.Enabled = btnRight.Enabled = btnOff.Enabled = bSelected && btnStop.Enabled;

            btnPair.Enabled = bPair && bSelected && btnStop.Enabled && rootHub.Pairable;

            btnClear.Enabled = lvDebug.Items.Count > 0;
        }

        private void lvDebug_Enter(object sender, EventArgs e)
        {
            ThemeUtil.UpdateFocus(lvDebug.Handle);
        }

        private void Button_Enter(object sender, EventArgs e)
        {
            ThemeUtil.UpdateFocus(((Button) sender).Handle);
        }
    }
}