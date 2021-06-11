﻿/*
MIT License

Copyright (c) 2020 gpsnmeajp

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Valve.VR;
using EasyLazyLibrary;
using System.Numerics;
using Rug.Osc;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace vmt_manager
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        const string Version = "VMT_012";
        private DispatcherTimer dispatcherTimer;
        Random rnd;
        string title = "";
        string installPath = "";

        int aliveCnt = 0;
        bool ReceiveError = false;
        EasyOpenVRUtil util;
        OSC osc;

        bool loadTest = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Console.WriteLine("Loaded");
                title = this.Title;
                ManagerVersion.Text = Version;
                DriverVersion.Text = "-";

                rnd = new Random();

                util = new EasyOpenVRUtil();
                if (!util.StartOpenVR())
                {
                    var result = MessageBox.Show("Steam VR not ready. Maybe not ready for HMD or Tracking system.\nStream VRが利用できません。HMDやトラッキングシステムが利用できない状態の可能性があります。", title, MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }

                osc = new OSC("127.0.0.1", 39571, 39570, OnBundle, OnMessage);

                //セーフモードチェック
                EVRSettingsError eVRSettingsError = EVRSettingsError.None;
                bool safemode = OpenVR.Settings.GetBool("driver_vmt", OpenVR.k_pch_Driver_BlockedBySafemode_Bool, ref eVRSettingsError);
                if (eVRSettingsError == EVRSettingsError.None && safemode)
                {
                    var result = MessageBox.Show("SteamVR runnning on safe mode and VMT has blocked.\nPlease unblock, and restart SteamVR.\n\nSteamVRがセーフモードで動作し、VMTがブロックされています。\nブロックを解除し、SteamVRを再起動してください。", title, MessageBoxButton.OK, MessageBoxImage.Error);
                }
                //Enableチェック
                eVRSettingsError = EVRSettingsError.None;
                bool enable = OpenVR.Settings.GetBool("driver_vmt", OpenVR.k_pch_Driver_Enable_Bool, ref eVRSettingsError);
                if (eVRSettingsError == EVRSettingsError.None && !enable)
                {
                    var result = MessageBox.Show("VMT has disabled in Steam VR setting.\nPlease enable, and restart SteamVR.\n\nVMTはSteamVR上で無効に設定されています。\n有効にし、SteamVRを再起動してください。", title, MessageBoxButton.OK, MessageBoxImage.Error);
                }

                //デバッグのためにセーフモードを有効化
                //OpenVR.Settings.SetBool(OpenVR.k_pch_SteamVR_Section, OpenVR.k_pch_SteamVR_EnableSafeMode, true,ref eVRSettingsError);

                //タイマー起動
                dispatcherTimer = new DispatcherTimer();
                dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 100);
                dispatcherTimer.Tick += new EventHandler(GenericTimer);
                dispatcherTimer.Start();

                string[] args = System.Environment.GetCommandLineArgs();
                if (args.Length > 1)
                {
                    if (args[1] == "install")
                    {
                        InstallButton(null, null);
                        Close();
                        return;
                    }
                    if (args[1] == "uninstall")
                    {
                        UninstallButton(null, null);
                        Close();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message + "\n" + ex.StackTrace, title);
                Close();
                return;
            }
        }

        private void OnMessage(OscMessage message)
        {
            //エラー時はそれ以上受信しない
            if (ReceiveError)
            {
                return;
            }
            try
            {
                if (message.Address == "/VMT/Out/Log")
                {
                    int stat = (int)message[0];
                    string msg = (string)message[1];

                    switch (stat)
                    {
                        case 0:
                            MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Information);
                            break;
                        case 1:
                            MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                            break;
                        case 2:
                            MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Error);
                            break;
                        default:
                            MessageBox.Show(msg, title, MessageBoxButton.OK);
                            break;
                    }
                }
                else if (message.Address == "/VMT/Out/Alive")
                {
                    //Keep Alive
                    string versionFromOSC = (string)message[0];
                    this.Dispatcher.Invoke(() =>
                    {
                        DriverVersion.Text = versionFromOSC;
                        if (message.Count > 1 && message[1] is string)
                        {
                            installPath = (string)message[1];
                        }

                        ControlDock.IsEnabled = true;

                        if (versionFromOSC != Version)
                        {
                            DriverVersion.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                        }
                        else
                        {
                            DriverVersion.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 0));
                        }

                        aliveCnt = 0;
                    });
                }
                else if (message.Address == "/VMT/Out/Haptic")
                {
                    int index = (int)message[0];
                    float frequency = (float)message[1];
                    float amplitude = (float)message[2];
                    float duration = (float)message[3];
                    //振動
                    this.Dispatcher.Invoke(() =>
                    {
                        InputVMTHapticTextBox.Text = string.Format("VMT_{0,0} f:{1:0.0}Hz A:{2:0.0} d:{3:0.0}s", index, frequency, amplitude, duration);
                        InputVMTHapticTextBox.Background = new SolidColorBrush(Color.FromRgb(0, 255, 0));
                    });
                }
                else if (message.Address == "/VMT/Out/Unavailable")
                {
                    //利用不可ステータス
                    int code = (int)message[0];
                    string reason = (string)message[1];

                    this.Dispatcher.Invoke(() =>
                    {
                        StatusBarTextBlock.Text = code.ToString() + " : " + reason;
                        if (code != 0)
                        {
                            StatusBar.Background = new SolidColorBrush(Color.FromRgb(255, 255, 0));
                        }
                        else {
                            StatusBar.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                        }
                    });
                }
                else
                {
                    //Do noting
                }

            }
            catch (Exception ex)
            {
                ReceiveError = true;
                MessageBox.Show(ex.Message + "\n" + ex.StackTrace, title);
                this.Dispatcher.Invoke(() =>
                {
                    Close();
                });
                return;
            }
        }

        private void OnBundle(OscBundle bundle)
        {
            for (int i = 0; i < bundle.Count; i++)
            {
                switch (bundle[i])
                {
                    //Messageを受信した
                    case OscMessage msg:
                        OnMessage(msg);
                        break;
                    default:
                        //Do noting
                        break;
                }
            }
        }

        private void GenericTimer(object sender, EventArgs e)
        {
            try
            {
                HmdMatrix34_t m = new HmdMatrix34_t();
                OpenVR.ChaperoneSetup.GetWorkingStandingZeroPoseToRawTrackingPose(ref m);

                RoomMatrixTextBox.Text =
                    String.Format("{0:0.00}, {1:0.00}, {2:0.00}, {3:0.00}\n{4:0.00}, {5:0.00}, {6:0.00}, {7:0.00}\n{8:0.00}, {9:0.00}, {10:0.00}, {11:0.00}\n", m.m0, m.m1, m.m2, m.m3, m.m4, m.m5, m.m6, m.m7, m.m8, m.m9, m.m10, m.m11);
                if (
                    m.m0 == 0 &&
                    m.m1 == 0 &&
                    m.m2 == 0 &&
                    m.m3 == 0 &&
                    m.m4 == 0 &&
                    m.m5 == 0 &&
                    m.m6 == 0 &&
                    m.m7 == 0 &&
                    m.m8 == 0 &&
                    m.m9 == 0 &&
                    m.m10 == 0 &&
                    m.m11 == 0
                    )
                {
                    RoomMatrixTextBox.Background = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                    SetRoomMatrixButtonName.IsEnabled = false;
                }
                else
                {
                    RoomMatrixTextBox.Background = new SolidColorBrush(Color.FromRgb(0, 255, 0));
                    SetRoomMatrixButtonName.IsEnabled = true;
                }


                var t1 = util.GetTransformBySerialNumber("VMT_0");
                if (t1 != null)
                {
                    string roomPos = string.Format("{0:0.00}, {1:0.00}, {2:0.00}", t1.position.X, t1.position.Y, t1.position.Z);

                    //Unity座標系ではZが反転する
                    if (CoordinateCombo.SelectedIndex == 1)
                    {
                        roomPos = string.Format("{0:0.00}, {1:0.00}, {2:0.00}", t1.position.X, t1.position.Y, -t1.position.Z);
                    }

                    CheckPositionTextBox.Text = roomPos;
                    if (roomPos == "1.00, 1.00, 1.00")
                    {
                        CheckPositionTextBox.Background = new SolidColorBrush(Color.FromRgb(0, 255, 0));
                    }
                    else
                    {
                        CheckPositionTextBox.Background = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                    }
                }
                var t2 = util.GetTransformBySerialNumberRaw("VMT_0");
                if (t2 != null)
                {
                    CheckPositionRawTextBox.Text = string.Format("{0:0.00}, {1:0.00}, {2:0.00}", t2.position.X, t2.position.Y, t2.position.Z);
                    CheckPositionRawSpeedTextBox.Text = string.Format("{0:0.00}, {1:0.00}, {2:0.00}", t2.velocity.X, t2.velocity.Y, t2.velocity.Z);                    
                }

                if (aliveCnt > 90)
                {
                    installPath = "";
                    DriverVersion.Text = "-";
                    DriverVersion.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                    ControlDock.IsEnabled = false;
                }
                else
                {
                    aliveCnt++;
                }

                InputVMTHapticTextBox.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));

                if (loadTest) {
                    ShowAllButton(null, null);
                }
            }
            catch (Exception ex)
            {
                dispatcherTimer.Stop(); //タイマー停止
                MessageBox.Show(ex.Message + "\n" + ex.StackTrace, title);
                Close();
                return;
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            //クローズ
            Console.WriteLine("Closed");
            if (osc != null) {
                osc.Dispose();
            }
        }

        private void ResetButton(object sender, RoutedEventArgs e)
        {
            osc.Send(new OscMessage("/VMT/Reset"));
        }

        private void ReloadJsonButton(object sender, RoutedEventArgs e)
        {
            osc.Send(new OscMessage("/VMT/LoadSetting"));
        }

        private void SetRoomMatrixButton(object sender, RoutedEventArgs e)
        {
            HmdMatrix34_t m = new HmdMatrix34_t();
            OpenVR.ChaperoneSetup.GetWorkingStandingZeroPoseToRawTrackingPose(ref m);

            if (SetRoomMatrixTemporaryCheckBox.IsChecked.Value)
            {
                osc.Send(new OscMessage("/VMT/SetRoomMatrix/Temporary",
                    m.m0, m.m1, m.m2, m.m3,
                    m.m4, m.m5, m.m6, m.m7,
                    m.m8, m.m9, m.m10, m.m11));
            }
            else
            {
                osc.Send(new OscMessage("/VMT/SetRoomMatrix",
                    m.m0, m.m1, m.m2, m.m3,
                    m.m4, m.m5, m.m6, m.m7,
                    m.m8, m.m9, m.m10, m.m11));
            }
        }

        private void ResetRoomMatrixButton(object sender, RoutedEventArgs e)
        {
            if (SetRoomMatrixTemporaryCheckBox.IsChecked.Value)
            {
                osc.Send(new OscMessage("/VMT/SetRoomMatrix/Temporary",
                    1f, 0f, 0f, 0f,
                    0f, 1f, 0f, 0f,
                    0f, 0f, 1f, 0f));
            }
            else
            {
                osc.Send(new OscMessage("/VMT/SetRoomMatrix",
                    1f, 0f, 0f, 0f,
                    0f, 1f, 0f, 0f,
                    0f, 0f, 1f, 0f));
            }
        }
        private void CheckPositionButton(object sender, RoutedEventArgs e)
        {
            if (CoordinateCombo.SelectedIndex == 1)
            {
                osc.Send(new OscMessage("/VMT/Room/Unity",
                0, 1, 0f,
                1f, 1f, 1f,
                0f, 0f, 0f, 1f));
            }
            else
            {
                osc.Send(new OscMessage("/VMT/Room/Driver",
                0, 1, 0f,
                1f, 1f, 1f,
                0f, 0f, 0f, 1f));
            }
        }
        private void CheckJointPositionButton(object sender, RoutedEventArgs e)
        {
            if (CoordinateCombo.SelectedIndex == 1)
            {
                osc.Send(new OscMessage("/VMT/Joint/Unity",
                    0, 1, 0f,
                    0.1f, 0f, -0.5f,
                    0f, 0f, 0f, 1f,
                    JointSerialNoTextBox.Text));
            }
            else
            {
                osc.Send(new OscMessage("/VMT/Joint/Driver",
                    0, 1, 0f,
                    0.1f, 0f, -0.5f,
                    0f, 0f, 0f, 1f,
                    JointSerialNoTextBox.Text));
            }
        }
        private void CheckJointPosition2Button(object sender, RoutedEventArgs e)
        {
            if (CoordinateCombo.SelectedIndex == 1)
            {
                osc.Send(new OscMessage("/VMT/Follow/Unity",
                    1, 1, 0f,
                    -0.1f, 0f, -0.5f,
                    0f, 0f, 0f, 1f,
                    JointSerialNo2TextBox.Text));
            }
            else
            {
                osc.Send(new OscMessage("/VMT/Follow/Driver",
                    1, 1, 0f,
                    -0.1f, 0f, -0.5f,
                    0f, 0f, 0f, 1f,
                    JointSerialNo2TextBox.Text));
            }
        }
        private void CheckJointPosition2YButton(object sender, RoutedEventArgs e)
        {
            if (CoordinateCombo.SelectedIndex == 1)
            {
                osc.Send(new OscMessage("/VMT/FollowRotY/Unity",
                    1, 1, 0f,
                    -0.1f, 0f, -0.5f,
                    0f, 0f, 0f, 1f,
                    JointSerialNo2TextBox.Text));
            }
            else
            {
                osc.Send(new OscMessage("/VMT/FollowRotY/Driver",
                    1, 1, 0f,
                    -0.1f, 0f, -0.5f,
                    0f, 0f, 0f, 1f,
                    JointSerialNo2TextBox.Text));
            }
        }

        private void InstallButton(object sender, RoutedEventArgs e)
        {
            if (DriverVersion.Text != "-")
            {
                MessageBox.Show("Please uninstall VMT before install.\nインストールを続ける前に、VMTをアンインストールしてください", title, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                string driverPath_rel = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + @"\..\vmt";
                string driverPath = System.IO.Path.GetFullPath(driverPath_rel);
                Console.WriteLine(OpenVR.RuntimePath() + @"\bin\win64");
                Console.WriteLine(driverPath);

                System.Diagnostics.Process process = new System.Diagnostics.Process();
                process.StartInfo.WorkingDirectory = OpenVR.RuntimePath() + @"\bin\win64";
                process.StartInfo.FileName = OpenVR.RuntimePath() + @"\bin\win64\vrpathreg.exe";
                process.StartInfo.Arguments = "adddriver \"" + driverPath + "\"";
                process.StartInfo.UseShellExecute = false;
                process.Start();
                process.WaitForExit();

                MessageBox.Show("OK (ExitCode=" + process.ExitCode + ")\nPlease restart SteamVR.", title);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message + "\n" + ex.StackTrace, title);
                Close();
                return;
            }
        }

        private async void UninstallButton(object sender, RoutedEventArgs e)
        {
            string driverPath_rel = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + @"\..\vmt";
            string driverPath = System.IO.Path.GetFullPath(driverPath_rel);

            if (installPath == "")
            {
                //インストールパスが受信できていない場合少し待つ
                await Task.Delay(2000);
            }

            if (installPath != "")
            {
                //場所がわかっている
                var res = MessageBox.Show("Uninstall VMT Driver?\nVMTドライバをアンインストールしますか?\n\n" + installPath, title, MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (res != MessageBoxResult.OK)
                {
                    return;
                }
                //現在のフォルダパスの代わりに、受信したパスでアンインストールを試す
                driverPath = installPath;
            }
            else
            {
                //場所不明だがとりあえず現在の場所としてアンインストールしようとするか確認する
                var res = MessageBox.Show("Other VMT Driver version detected.\nIn many cases, this driver located on different path.\nmanager can not uninstallation these.\nIf you want to remove currently installed VMT driver, press cancel and please use manager of drivers itself.\n\nPress OK, Try uninstallation anyway. (it will fail in many cases.)\n\n違うバージョンのVMTドライバを検出しました。\n多くの場合これは別のパスにあります。\n本Managerではアンインストールできません。\nアンインストールしたい場合は、キャンセルを押し、そのVMTドライバに付属のManagerを使用してください\n\nOKを押すと、とにかくアンインストールを試します。(多くの場合失敗します。)", title, MessageBoxButton.OKCancel, MessageBoxImage.Error);
                if (res != MessageBoxResult.OK)
                {
                    return;
                }
            }

            try
            {
                System.Diagnostics.Process process = new System.Diagnostics.Process();
                process.StartInfo.WorkingDirectory = OpenVR.RuntimePath() + @"\bin\win64";
                process.StartInfo.FileName = OpenVR.RuntimePath() + @"\bin\win64\vrpathreg.exe";
                process.StartInfo.Arguments = "removedriver \"" + driverPath + "\"";
                process.StartInfo.UseShellExecute = false;
                process.Start();
                process.WaitForExit();

                MessageBox.Show("OK (ExitCode=" + process.ExitCode + ")\nPlease restart SteamVR.", title);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message + "\n" + ex.StackTrace, title);
                Close();
                return;
            }
        }

        private void ShowAllButton(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < 58; i++)
            {
                osc.Send(new OscMessage("/VMT/Room/Driver",
                    i, 1, 0f,
                    (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f,
                    0f, 0f, 0f, 1f));
            }
        }

        private (bool ok, int i) GetInputIndex()
        {
            int index = 0;
            if (InputVMTNoTextBox != null && int.TryParse(InputVMTNoTextBox?.Text, out index))
            {
                return (ok: true, i: index);
            }
            return (ok: true, i: index);
        }
        private void ButtonSend(int buttonIndex, bool press)
        {
            var index = GetInputIndex();
            int value = press ? 1 : 0;
            float timeoffset = 0;
            if (index.ok)
            {
                osc.Send(new OscMessage("/VMT/Input/Button",
                    index.i, buttonIndex, timeoffset, value));
            }
        }

        private void Button0_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ButtonSend(0, true);
        }
        private void Button0_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ButtonSend(0, false);
        }
        private void Button1_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ButtonSend(1, true);
        }
        private void Button1_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ButtonSend(1, false);
        }
        private void Button2_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ButtonSend(2, true);
        }
        private void Button2_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ButtonSend(2, false);
        }
        private void Button3_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ButtonSend(3, true);
        }
        private void Button3_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ButtonSend(3, false);
        }
        private void Button4_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ButtonSend(4, true);
        }
        private void Button4_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ButtonSend(4, false);
        }
        private void Button5_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ButtonSend(5, true);
        }
        private void Button5_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ButtonSend(5, false);
        }
        private void Button6_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ButtonSend(6, true);
        }
        private void Button6_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ButtonSend(6, false);
        }
        private void Button7_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ButtonSend(7, true);
        }
        private void Button7_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ButtonSend(7, false);
        }

        private void Slider0_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var index = GetInputIndex();

            int triggerIndex = 0;
            float timeoffset = 0;
            if (index.ok && Slider0 != null)
            {
                osc.Send(new OscMessage("/VMT/Input/Trigger",
                    index.i, triggerIndex, timeoffset, (float)Slider0.Value));
            }
        }
        private void Slider1_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var index = GetInputIndex();

            int triggerIndex = 1;
            float timeoffset = 0;
            if (index.ok && Slider1 != null)
            {
                osc.Send(new OscMessage("/VMT/Input/Trigger",
                    index.i, triggerIndex, timeoffset, (float)Slider1.Value));
            }
        }
        private void SliderXY_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var index = GetInputIndex();

            int joystickIndex = 0;
            float timeoffset = 0;
            if (index.ok && SliderX != null && SliderY != null)
            {
                osc.Send(new OscMessage("/VMT/Input/Joystick",
                    index.i, joystickIndex, timeoffset, (float)SliderX.Value, (float)SliderY.Value));
            }
        }
        private void InputResetButton(object sender, RoutedEventArgs e)
        {
            Slider0.Value = 0f;
            Slider1.Value = 0f;
            SliderX.Value = 0f;
            SliderY.Value = 0f;
        }
        private void HapticTestButton(object sender, RoutedEventArgs e)
        {
            var index = GetInputIndex();
            if (index.ok)
            {
                var deviceIndex = util.GetDeviceIndexBySerialNumber("VMT_" + index.i);
                if (deviceIndex != EasyOpenVRUtil.InvalidDeviceIndex)
                {
                    util.TriggerHapticPulse(deviceIndex);
                }
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(e.Uri.AbsoluteUri);
        }
        private void TrackingReferenceButton(object sender, RoutedEventArgs e)
        {
            var index = GetInputIndex();
            if (index.ok)
            {
                osc.Send(new OscMessage("/VMT/Room/Driver",
                index.i, 4, 0f,
                0f, 0f, 0f,
                0f, 0f, 0f, 1f));
            }
        }

        private void RightHandButton(object sender, RoutedEventArgs e)
        {
            var index = GetInputIndex();
            if (index.ok)
            {
                osc.Send(new OscMessage("/VMT/Room/Driver",
                index.i, 3, 0f,
                0f, 0f, 0f,
                0f, 0f, 0f, 1f));
            }
        }

        private void LeftHandButton(object sender, RoutedEventArgs e)
        {
            var index = GetInputIndex();
            if (index.ok)
            {
                osc.Send(new OscMessage("/VMT/Room/Driver",
                index.i, 2, 0f,
                0f, 0f, 0f,
                0f, 0f, 0f, 1f));
            }
        }

        private void TrackerButton(object sender, RoutedEventArgs e)
        {
            var index = GetInputIndex();
            if (index.ok)
            {
                osc.Send(new OscMessage("/VMT/Room/Driver",
                index.i, 1, 0f,
                0f, 0f, 0f,
                0f, 0f, 0f, 1f));
            }
        }

        private void EnableAutoPoseUdateButton(object sender, RoutedEventArgs e)
        {
            osc.Send(new OscMessage("/VMT/SetAutoPoseUpdate", 1));
        }
        private void DisableAutoPoseUdateButton(object sender, RoutedEventArgs e)
        {
            osc.Send(new OscMessage("/VMT/SetAutoPoseUpdate", 0));
        }
        private void DeviceReloadButton(object sender, RoutedEventArgs e)
        {
            DeviceListView.Items.Clear();

            for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
            {
                string s = i.ToString() + " : " + util.GetSerialNumber(i);
                DeviceListView.Items.Add(s);
            }
        }
        private void DeviceCopyButton(object sender, RoutedEventArgs e)
        {
            if (DeviceListView.SelectedItem != null)
            {
                string s = (string)DeviceListView.SelectedItem;
                s = s.Split(':')[1].Trim();

                System.Windows.Clipboard.SetText(s);
            }
        }
        private void JsonLoadButton(object sender, RoutedEventArgs e)
        {
            string jsonPath_rel = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + @"\..\vmt\setting.json";
            string jsonPath = System.IO.Path.GetFullPath(jsonPath_rel);

            try
            {
                JsonTextBox.Text = File.ReadAllText(jsonPath, new UTF8Encoding(false));
                MessageBox.Show("Loaded.", title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) {
                MessageBox.Show(ex.Message + "\n" + ex.StackTrace, title);
            }

        }
        private void JsonSaveButton(object sender, RoutedEventArgs e)
        {
            string jsonPath_rel = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + @"\..\vmt\setting.json";
            string jsonPath = System.IO.Path.GetFullPath(jsonPath_rel);
            try
            {
                File.WriteAllText(jsonPath, JsonTextBox.Text, new UTF8Encoding(false));
                MessageBox.Show("Saved.", title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message + "\n" + ex.StackTrace, title);
            }
        }

        private void LoadTestCheckBoxEvent(object sender, RoutedEventArgs e)
        {
            loadTest = LoadTestCheckBox.IsChecked.Value;
        }
    }
}
