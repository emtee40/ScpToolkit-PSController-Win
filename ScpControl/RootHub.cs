﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.ServiceModel;
using Libarius.System;
using ReactiveSockets;
using ScpControl.Bluetooth;
using ScpControl.Exceptions;
using ScpControl.Profiler;
using ScpControl.Properties;
using ScpControl.Rx;
using ScpControl.ScpCore;
using ScpControl.Shared.Core;
using ScpControl.Sound;
using ScpControl.Usb;
using ScpControl.Usb.Ds3;
using ScpControl.Usb.Gamepads;
using ScpControl.Utilities;
using ScpControl.Wcf;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace ScpControl
{
    [ServiceBehavior(IncludeExceptionDetailInFaults = false, InstanceContextMode = InstanceContextMode.Single)]
    public sealed partial class RootHub : ScpHub, IScpCommandService
    {
        #region Private fields

        // Bluetooth hub
        private readonly BthHub _bthHub = new BthHub();

        private readonly byte[][] _mNative =
        {
            new byte[2] {0, 0}, new byte[2] {0, 0}, new byte[2] {0, 0},
            new byte[2] {0, 0}
        };

        private readonly PhysicalAddress[] _reservedPads =
        {
            PhysicalAddress.None, PhysicalAddress.None,
            PhysicalAddress.None, PhysicalAddress.None
        };

        private readonly byte[][] _vibration =
        {
            new byte[2] {0, 0}, new byte[2] {0, 0}, new byte[2] {0, 0},
            new byte[2] {0, 0}
        };

        // subscribed clients who receive the native stream
        private readonly IDictionary<int, ScpNativeFeedChannel> _nativeFeedSubscribers =
            new ConcurrentDictionary<int, ScpNativeFeedChannel>();

        // Usb hub
        private readonly UsbHub _usbHub = new UsbHub();
        private readonly ViGEmClient _client;
        // creates a system-wide mutex to check if the root hub has been instantiated already
        private LimitInstance _limitInstance;
        private volatile bool _mSuspended;
        // the WCF service host
        private ServiceHost _rootHubServiceHost;
        // server to broadcast native byte stream
        private ReactiveListener _rxFeedServer;
        private bool _serviceStarted;

        #endregion

        #region IScpCommandService methods

        /// <summary>
        ///     Checks if the native stream is available or disabled in configuration.
        /// </summary>
        /// <returns>True if feed is available, false otherwise.</returns>
        public bool IsNativeFeedAvailable()
        {
            return !GlobalConfiguration.Instance.DisableNative;
        }

        public DualShockPadMeta GetPadDetail(DsPadId pad)
        {
            var serial = (byte)pad;

            lock (Pads)
            {
                var current = Pads[serial];

                return new DualShockPadMeta
                {
                    BatteryStatus = (byte)current.Battery,
                    ConnectionType = current.Connection,
                    Model = current.Model,
                    PadId = current.PadId,
                    PadMacAddress = current.DeviceAddress,
                    PadState = current.State
                };
            }
        }

        public bool Rumble(DsPadId pad, byte large, byte small)
        {
            var serial = (byte)pad;

            if (Pads[serial].State != DsState.Connected) return false;

            if (large == _mNative[serial][0] && small == _mNative[serial][1]) return false;

            _mNative[serial][0] = large;
            _mNative[serial][1] = small;

            Pads[serial].Rumble(large, small);

            return true;
        }

        public IEnumerable<string> GetStatusData()
        {
            if (!_serviceStarted)
                return default(IEnumerable<string>);

            var list = new List<string>
            {
                Dongle,
                Pads[0].ToString(),
                Pads[1].ToString(),
                Pads[2].ToString(),
                Pads[3].ToString()
            };

            return list;
        }

        public void PromotePad(byte pad)
        {
            int target = pad;

            if (Pads[target].State == DsState.Disconnected) return;

            var swap = Pads[target];
            Pads[target] = Pads[target - 1];
            Pads[target - 1] = swap;

            Pads[target].PadId = (DsPadId)target;
            Pads[target - 1].PadId = (DsPadId)(target - 1);

            _reservedPads[target] = Pads[target].DeviceAddress;
            _reservedPads[target - 1] = Pads[target - 1].DeviceAddress;
        }

        /// <summary>
        ///     Requests the currently active configuration set from the root hub.
        /// </summary>
        /// <returns>Returns the global configuration object.</returns>
        public GlobalConfiguration RequestConfiguration()
        {
            return GlobalConfiguration.Request();
        }

        /// <summary>
        ///     Submits an altered copy of the global configuration to the root hub and saves it.
        /// </summary>
        /// <param name="configuration">The global configuration object.</param>
        public void SubmitConfiguration(GlobalConfiguration configuration)
        {
            GlobalConfiguration.Submit(configuration);
            GlobalConfiguration.Save();
        }

        public IEnumerable<DualShockProfile> GetProfiles()
        {
            return DualShockProfileManager.Instance.Profiles;
        }

        public void SubmitProfile(DualShockProfile profile)
        {
            DualShockProfileManager.Instance.SubmitProfile(profile);
        }

        public void RemoveProfile(DualShockProfile profile)
        {
            DualShockProfileManager.Instance.RemoveProfile(profile);
        }

        public void FeedbackReceivedEventHandler(object sender, DualShock4FeedbackReceivedEventArgs e) {
            int padId = DS4Pads.IndexOf((DualShock4Controller)sender);
            Rumble((DsPadId)padId, e.LargeMotor, e.SmallMotor);
        }

        public static void ConvertDS3(DualShock4Report report, ScpHidReport scpReport)
        {
            report.SetAxis(DualShock4Axes.LeftThumbX, scpReport[Ds3Axis.Lx].Value);
            report.SetAxis(DualShock4Axes.LeftThumbY, scpReport[Ds3Axis.Ly].Value);
            report.SetAxis(DualShock4Axes.RightThumbX, scpReport[Ds3Axis.Rx].Value);
            report.SetAxis(DualShock4Axes.RightThumbY, scpReport[Ds3Axis.Ry].Value);
            report.SetAxis(DualShock4Axes.LeftTrigger, scpReport[Ds3Axis.L2].Value);
            report.SetAxis(DualShock4Axes.RightTrigger, scpReport[Ds3Axis.R2].Value);

            report.SetSpecialButtonState(DualShock4SpecialButtons.Ps, scpReport[Ds3Button.Ps].IsPressed);
            report.SetButtonState(DualShock4Buttons.Circle, scpReport[Ds3Button.Circle].IsPressed);
            report.SetButtonState(DualShock4Buttons.Cross, scpReport[Ds3Button.Cross].IsPressed);
            report.SetButtonState(DualShock4Buttons.Square, scpReport[Ds3Button.Square].IsPressed);
            report.SetButtonState(DualShock4Buttons.Triangle, scpReport[Ds3Button.Triangle].IsPressed);
            report.SetSpecialButtonState(DualShock4SpecialButtons.Touchpad, scpReport[Ds3Button.Select].IsPressed);
            report.SetButtonState(DualShock4Buttons.Options, scpReport[Ds3Button.Start].IsPressed);
            report.SetButtonState(DualShock4Buttons.ShoulderLeft, scpReport[Ds3Button.L1].IsPressed);
            report.SetButtonState(DualShock4Buttons.ShoulderRight, scpReport[Ds3Button.R1].IsPressed);
            report.SetButtonState(DualShock4Buttons.ThumbLeft, scpReport[Ds3Button.L3].IsPressed);
            report.SetButtonState(DualShock4Buttons.ThumbRight, scpReport[Ds3Button.R3].IsPressed);

            if (scpReport[Ds3Button.Up].IsPressed)
                report.SetDPad(DualShock4DPadValues.North);
            if (scpReport[Ds3Button.Right].IsPressed)
                report.SetDPad(DualShock4DPadValues.East);
            if (scpReport[Ds3Button.Down].IsPressed)
                report.SetDPad(DualShock4DPadValues.South);
            if (scpReport[Ds3Button.Left].IsPressed)
                report.SetDPad(DualShock4DPadValues.West);
            if (scpReport[Ds3Button.Up].IsPressed
                && scpReport[Ds3Button.Right].IsPressed)
                report.SetDPad(DualShock4DPadValues.Northeast);
            if (scpReport[Ds3Button.Down].IsPressed
                && scpReport[Ds3Button.Right].IsPressed)
                report.SetDPad(DualShock4DPadValues.Southeast);
            if (scpReport[Ds3Button.Down].IsPressed
                && scpReport[Ds3Button.Left].IsPressed)
                report.SetDPad(DualShock4DPadValues.Southwest);
            if (scpReport[Ds3Button.Up].IsPressed
                && scpReport[Ds3Button.Left].IsPressed)
                report.SetDPad(DualShock4DPadValues.Northwest);

            short accelX = (short)(scpReport.Motion.X);
            report.SetMotion(DualShock4Motion.AccelX, accelX);
            short accelY = (short)(scpReport.Motion.Y);
            report.SetMotion(DualShock4Motion.AccelY, accelY);
            short accelZ = (short)(scpReport.Motion.Z);
            report.SetMotion(DualShock4Motion.AccelZ, accelZ);
            short gyroX = (short)(scpReport.Orientation.Pitch);
            report.SetMotion(DualShock4Motion.GyroX, gyroX);
            short gyroY = (short)(scpReport.Orientation.Yaw);
            report.SetMotion(DualShock4Motion.GyroY, gyroY);
            short gyroZ = (short)(scpReport.Orientation.Roll);
            report.SetMotion(DualShock4Motion.GyroZ, gyroZ);
        }

        #endregion

        #region Ctors

        public RootHub()
        {
            InitializeComponent();

            _client = new ViGEmClient();
            // prepare "empty" pad list
            Pads = new List<IDsDevice>
            {
                new DsNull(DsPadId.One),
                new DsNull(DsPadId.Two),
                new DsNull(DsPadId.Three),
                new DsNull(DsPadId.Four)
            };
            DS4Pads = new List<DualShock4Controller>
            {
                new DualShock4Controller(_client),
                new DualShock4Controller(_client),
                new DualShock4Controller(_client),
                new DualShock4Controller(_client),
            };

            // subscribe to device plug-in events
            _bthHub.Arrival += OnDeviceArrival;
            _usbHub.Arrival += OnDeviceArrival;

            // subscribe to incoming HID reports
            _bthHub.Report += OnHidReportReceived;
            _usbHub.Report += OnHidReportReceived;
        }

        public RootHub(IContainer container)
            : this()
        {
            container.Add(this);
        }

        #endregion

        #region Properties

        /// <summary>
        ///     A collection of currently connected game pads.
        /// </summary>
        public IList<IDsDevice> Pads { get; private set; }

        /// <summary>
        ///     A collection of currently emulated game pads.
        /// </summary>
        public IList<DualShock4Controller> DS4Pads { get; private set; }

        [Obsolete]
        public string Dongle
        {
            get { return _bthHub.Dongle; }
        }

        /// <summary>
        ///     The MAC address of the current Bluetooth host.
        /// </summary>
        public PhysicalAddress BluetoothHostAddress
        {
            get { return _bthHub.BluetoothHostAddress; }
        }

        public bool Pairable
        {
            get { return m_Started && _bthHub.Pairable; }
        }

        #endregion

        #region Actions

        /// <summary>
        ///     Opens and initializes devices and services listening and running on the local machine.
        /// </summary>
        /// <returns>True on success, false otherwise.</returns>
        public override bool Open()
        {
            var opened = false;

            Log.Debug("Initializing root hub");

            _limitInstance = new LimitInstance(@"Global\ScpDsxRootHub");

            try
            {
                if (!_limitInstance.IsOnlyInstance) // existing root hub running as desktop app
                    throw new RootHubAlreadyStartedException(
                        "The root hub is already running, please close the ScpServer first!");
            }
            catch (UnauthorizedAccessException) // existing root hub running as service
            {
                throw new RootHubAlreadyStartedException(
                    "The root hub is already running, please stop the ScpService first!");
            }

            Log.DebugFormat("++ {0} {1}", Assembly.GetExecutingAssembly().Location,
                Assembly.GetExecutingAssembly().GetName().Version);
            Log.DebugFormat("++ {0}", OsInfoHelper.OsInfo);

            #region Native feed server

            _rxFeedServer = new ReactiveListener(Settings.Default.RootHubNativeFeedPort);

            _rxFeedServer.Connections.Subscribe(socket =>
            {
                Log.DebugFormat("Client connected on native feed channel: {0}", socket.GetHashCode());
                var protocol = new ScpNativeFeedChannel(socket);

                _nativeFeedSubscribers.Add(socket.GetHashCode(), protocol);

                protocol.Receiver.Subscribe(packet => { Log.Warn("Uuuhh how did we end up here?!"); });

                socket.Disconnected += (sender, e) =>
                {
                    Log.DebugFormat(
                        "Client disconnected from native feed channel {0}",
                        sender.GetHashCode());

                    _nativeFeedSubscribers.Remove(socket.GetHashCode());
                };

                socket.Disposed += (sender, e) =>
                {
                    Log.DebugFormat("Client disposed from native feed channel {0}",
                        sender.GetHashCode());

                    _nativeFeedSubscribers.Remove(socket.GetHashCode());
                };
            });

            #endregion

            //Delopened |= _scpBus.Open(GlobalConfiguration.Instance.Bus);
            opened |= _usbHub.Open();
            opened |= _bthHub.Open();

            GlobalConfiguration.Load();
            return opened;
        }

        /// <summary>
        ///     Starts listening for incoming requests and starts all underlying hubs.
        /// </summary>
        /// <returns>True on success, false otherwise.</returns>
        public override bool Start()
        {
            if (m_Started) return m_Started;

            Log.Debug("Starting root hub");

            if (!_serviceStarted)
            {
                var baseAddress = new Uri("net.tcp://localhost:26760/ScpRootHubService");

                var binding = new NetTcpBinding
                {
                    TransferMode = TransferMode.Streamed,
                    Security = new NetTcpSecurity { Mode = SecurityMode.None }
                };

                _rootHubServiceHost = new ServiceHost(this, baseAddress);
                _rootHubServiceHost.AddServiceEndpoint(typeof(IScpCommandService), binding, baseAddress);

                _rootHubServiceHost.Open();

                _serviceStarted = true;
            }

            try
            {
                _rxFeedServer.Start();
            }
            catch (SocketException sex)
            {
                Log.FatalFormat("Couldn't start native feed server: {0}", sex);
                return false;
            }

            //Delm_Started |= _scpBus.Start();
            m_Started |= _usbHub.Start();
            m_Started |= _bthHub.Start();

            Log.Debug("Root hub started");

            // make some noise =)
            if (GlobalConfiguration.Instance.IsStartupSoundEnabled)
                AudioPlayer.Instance.PlayCustomFile(GlobalConfiguration.Instance.StartupSoundFile);

            return m_Started;
        }

        /// <summary>
        ///     Stops all underlying hubs and disposes acquired resources.
        /// </summary>
        /// <returns>True on success, false otherwise.</returns>
        public override bool Stop()
        {
            Log.Debug("Root hub stop requested");

            _serviceStarted = false;

            if (_rootHubServiceHost != null)
                _rootHubServiceHost.Close();

            if (_rxFeedServer != null)
                _rxFeedServer.Dispose();

            //Del_scpBus.Stop();
            _client.Dispose();
            _usbHub.Stop();
            _bthHub.Stop();

            m_Started = !m_Started;

            Log.Debug("Root hub stopped");

            _limitInstance.Dispose();

            return true;
        }

        /// <summary>
        ///     Stops all underlying hubs, disposes acquired resources and saves the global configuration.
        /// </summary>
        /// <returns>True on success, false otherwise.</returns>
        public override bool Close()
        {
            var retval = Stop();

            GlobalConfiguration.Save();

            return retval;
        }

        public override bool Suspend()
        {
            _mSuspended = true;

            lock (Pads)
            {
                foreach (var t in Pads)
                {
                    t.Disconnect();
                }

                foreach (var t in DS4Pads)
                {
                    if (!t.IsConnected())
                        continue;
                    t.Disconnect();
                    t.FeedbackReceived -= FeedbackReceivedEventHandler;
                }
            }

            //Del_scpBus.Suspend();
            _usbHub.Suspend();
            _bthHub.Suspend();

            Log.Debug("++ Suspended");
            return true;
        }

        public override bool Resume()
        {
            Log.Debug("++ Resumed");

            //Del_scpBus.Resume();

            for (var index = 0; index < Pads.Count; index++)
            {
                if (Pads[index].State != DsState.Disconnected && !DS4Pads[index].IsConnected())
                {
                    //Del_scpBus.Plugin(index + 1);
                    DS4Pads[index].Connect();
                    DS4Pads[index].FeedbackReceived += FeedbackReceivedEventHandler;
                }
            }

            _usbHub.Resume();
            _bthHub.Resume();

            _mSuspended = false;
            return true;
        }

        #endregion

        #region Events

        /// <summary>
        ///     Gets called when a device was plugged in.
        /// </summary>
        /// <param name="notification">The <see cref="ScpDevice.Notified"/> type.</param>
        /// <param name="Class">The device class of the currently affected device.</param>
        /// <param name="path">The device path of the currently affected device.</param>
        /// <returns></returns>
        public override DsPadId Notify(ScpDevice.Notified notification, string Class, string path)
        {
            // ignore while component is in sleep mode
            if (_mSuspended) return DsPadId.None;

            var classGuid = Guid.Parse(Class);

            // forward message for wired DS3 to usb hub
            if (classGuid == UsbDs3.DeviceClassGuid)
            {
                return _usbHub.Notify(notification, Class, path);
            }

            // forward message for wired Generic Gamepad to usb hub
            if (classGuid == UsbGenericGamepad.DeviceClassGuid)
            {
                return _usbHub.Notify(notification, Class, path);
            }

            // forward message for any wireless device to bluetooth hub
            if (classGuid == BthDongle.DeviceClassGuid)
            {
                _bthHub.Notify(notification, Class, path);
            }

            return DsPadId.None;
        }

        protected override void OnDeviceArrival(object sender, ArrivalEventArgs e)
        {
            var bFound = false;
            var arrived = e.Device;

            lock (Pads)
            {
                for (var index = 0; index < Pads.Count && !bFound; index++)
                {
                    if (arrived.DeviceAddress.Equals(_reservedPads[index]))
                    {
                        if (Pads[index].State == DsState.Connected)
                        {
                            if (Pads[index].Connection == DsConnection.Bluetooth)
                            {
                                Pads[index].Disconnect();
                            }

                            if (Pads[index].Connection == DsConnection.Usb)
                            {
                                arrived.Disconnect();

                                e.Handled = false;
                                return;
                            }
                        }

                        bFound = true;

                        arrived.PadId = (DsPadId)index;
                        Pads[index] = arrived;
                    }
                }

                for (var index = 0; index < Pads.Count && !bFound; index++)
                {
                    if (Pads[index].State == DsState.Disconnected)
                    {
                        bFound = true;
                        _reservedPads[index] = arrived.DeviceAddress;

                        arrived.PadId = (DsPadId)index;
                        Pads[index] = arrived;
                    }
                }
            }

            if (bFound && !DS4Pads[(int)arrived.PadId].IsConnected())
            {
                DS4Pads[(int)arrived.PadId].Connect();
                DS4Pads[(int)arrived.PadId].FeedbackReceived += FeedbackReceivedEventHandler;
                //Del_scpBus.Plugin((int)arrived.PadId + 1);

                if (!GlobalConfiguration.Instance.IsVBusDisabled)
                {
                    Log.InfoFormat("Plugged in Port #{0} for {1} on Virtual Bus", (int)arrived.PadId + 1,
                        arrived.DeviceAddress.AsFriendlyName());
                }
            }
            e.Handled = bFound;
        }

        protected override void OnHidReportReceived(object sender, ScpHidReport e)
        {
            // get current pad ID
            var serial = (int)e.PadId;

            if (GlobalConfiguration.Instance.ProfilesEnabled)
            {
                // pass current report through user profiles
                DualShockProfileManager.Instance.PassThroughAllProfiles(e);
            }

            if (e.PadState == DsState.Connected)
            {
                var report = new DualShock4Report();
                ConvertDS3(report, e);
                Pads[serial].XInputSlot = (uint) serial;
                DS4Pads[serial].SendReport(report);
            }
            else
            {
                // reset rumble/vibration to off state
                _vibration[serial][0] = _vibration[serial][1] = 0;
                _mNative[serial][0] = _mNative[serial][1] = 0;

                if (GlobalConfiguration.Instance.AlwaysUnPlugVirtualBusDevice && DS4Pads[(int)e.PadId].IsConnected())
                {
                    //Del_scpBus.Unplug(_scpBus.IndexToSerial((byte)e.PadId));
                    DS4Pads[(int)e.PadId].Disconnect();
                    DS4Pads[(int)e.PadId].FeedbackReceived -= FeedbackReceivedEventHandler;
                }
            }

            // skip broadcast if native feed is disabled
            if (GlobalConfiguration.Instance.DisableNative)
                return;

            // send native controller inputs to subscribed clients
            foreach (
                var channel in _nativeFeedSubscribers.Select(nativeFeedSubscriber => nativeFeedSubscriber.Value))
            {
                try
                {
                    channel.SendAsync(e.RawBytes);
                }
                catch (AggregateException)
                {
                    /* This might happen if the client disconnects while sending the 
                     * response is still in progress. The exception can be ignored. */
                }
            }
        }

        #endregion
    }
}