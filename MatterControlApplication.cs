/*
Copyright (c) 2014, Lars Brubaker
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met: 

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer. 
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution. 

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies, 
either expressed or implied, of the FreeBSD Project.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using MatterHackers.Agg;
using MatterHackers.Agg.UI;
using MatterHackers.MatterControl.DataStorage;
using MatterHackers.MatterControl.PartPreviewWindow;
using MatterHackers.MatterControl.PluginSystem;
using MatterHackers.MatterControl.PrintQueue;
using MatterHackers.MatterControl.SlicerConfiguration;
using MatterHackers.VectorMath;

namespace MatterHackers.MatterControl
{
    public class MatterControlApplication : SystemWindow
    {
        string[] commandLineArgs = null;
        bool firstDraw = true;
        bool ShowMemoryUsed = false;
        bool DoCGCollectEveryDraw = false;
        public bool RestartOnClose = false;

        public MatterControlApplication(double width, double height)
            : base(width, height)
        {
            this.commandLineArgs = Environment.GetCommandLineArgs();
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            foreach(string command in commandLineArgs)
            {
                string commandUpper = command.ToUpper();
                switch (commandUpper)
                {
                    case "TEST":
                        Testing.TestingDispatch testDispatch = new Testing.TestingDispatch();
                        string[] testCommands = new string[commandLineArgs.Length - 2];
                        if (commandLineArgs.Length > 2)
                        {
                            commandLineArgs.CopyTo(testCommands, 2);
                        }
                        testDispatch.RunTests(testCommands);
                        return;

                    case "CLEAR_CACHE":
                        AboutPage.DeleteCacheData();
                        break;

                    case "SHOW_MEMORY":
                        ShowMemoryUsed = true;
                        break;

                    case "DO_GC_COLLECT_EVERY_DRAW":
                        ShowMemoryUsed = true;
                        DoCGCollectEveryDraw = true;
                        break;
                }
            }

            //WriteTestGCodeFile();
#if !DEBUG
            if (File.Exists("RunUnitTests.txt"))
#endif
            {
                GuiHalWidget.SetClipboardFunctions(System.Windows.Forms.Clipboard.GetText, System.Windows.Forms.Clipboard.SetText, System.Windows.Forms.Clipboard.ContainsText);

                MatterHackers.PolygonMesh.UnitTests.UnitTests.Run();
                MatterHackers.RayTracer.UnitTests.Run();
                MatterHackers.Agg.Tests.UnitTests.Run();
                MatterHackers.VectorMath.Tests.UnitTests.Run();
                MatterHackers.Agg.UI.Tests.UnitTests.Run();

                // you can turn this on to debug some bounds issues
                //GuiWidget.DebugBoundsUnderMouse = true;
            }

            GuiWidget.DefaultEnforceIntegerBounds = true;

            this.AddChild(ApplicationWidget.Instance);
            this.Padding = new BorderDouble(0); //To be re-enabled once native borders are turned off

#if false // this is to test freeing gcodefile memory
            Button test = new Button("test");
            test.Click += (sender, e) =>
            {
                //MatterHackers.GCodeVisualizer.GCodeFile gcode = new GCodeVisualizer.GCodeFile();
                //gcode.Load(@"C:\Users\lbrubaker\Downloads\drive assy.gcode");
                SystemWindow window = new SystemWindow(100, 100);
                window.ShowAsSystemWindow();
            };
            allControls.AddChild(test);
#endif
            this.AnchorAll();

            UseOpenGL = true;
            Title = "MatterControl (beta)";

            ActivePrinterProfile.CheckForAndDoAutoConnect();
            UiThread.RunOnIdle(CheckOnPrinter);

            MinimumSize = new Vector2(590, 540);

            ShowAsSystemWindow();
        }

        private static void WriteMove(StringBuilder gcodeStringBuilder, Vector2 center)
        {
            gcodeStringBuilder.AppendLine("G1 X" + center.x.ToString() + " Y" + center.y.ToString());
        }

        public static void WriteTestGCodeFile()
        {
            StringBuilder gcodeStringBuilder = new StringBuilder();

            int loops = 15;
            int steps = 20;
            double radius = 90;
            Vector2 center = new Vector2(0, 0);

            gcodeStringBuilder.AppendLine("G28 ; home all axes");
            gcodeStringBuilder.AppendLine("G90 ; use absolute coordinates");
            gcodeStringBuilder.AppendLine("G21 ; set units to millimeters");
            gcodeStringBuilder.AppendLine("G92 E0");
            gcodeStringBuilder.AppendLine("G1 F7800.000");
            //gcodeStringBuilder.AppendLine("G1 Z" + (30).ToString());
            WriteMove(gcodeStringBuilder, center);

            for (int loop = 0; loop < loops; loop++)
            {
                for (int step = 0; step < steps; step++)
                {
                    Vector2 nextPosition = new Vector2(radius, 0);
                    nextPosition.Rotate(MathHelper.Tau / steps * step);
                    WriteMove(gcodeStringBuilder, center + nextPosition);
                }
            }

            gcodeStringBuilder.AppendLine("M84     ; disable motors");

            System.IO.File.WriteAllText("PerformanceTest.gcode", gcodeStringBuilder.ToString());
        }

        void CheckOnPrinter(object state)
        {
            PrinterCommunication.Instance.OnIdle();
            UiThread.RunOnIdle(CheckOnPrinter);
        }

        public override void OnParentChanged(EventArgs e)
        {
            if (File.Exists("RunUnitTests.txt"))
            {
                //DiagnosticWidget diagnosticView = new DiagnosticWidget(this);
            }

            base.OnParentChanged(e);

            // now that we are all set up lets load our plugins and allow them their chance to set things up
            FindAndInstantiatePlugins();
        }

        private void FindAndInstantiatePlugins()
        {
#if false
            string pluginDirectory = Path.Combine("..", "..", "..", "MatterControlPlugins", "bin");
#if DEBUG
            pluginDirectory = Path.Combine(pluginDirectory, "Debug");
#else
            pluginDirectory = Path.Combine(pluginDirectory, "Release");
#endif
            if (!Directory.Exists(pluginDirectory))
            {
                string dataPath = DataStorage.ApplicationDataStorage.Instance.ApplicationUserDataPath;
                pluginDirectory = Path.Combine(dataPath, "Plugins");
            }
            // TODO: this should look in a plugin folder rather than just the application directory (we probably want it in the user folder).
            PluginFinder<MatterControlPlugin> pulginFinder = new PluginFinder<MatterControlPlugin>(pluginDirectory);
#else
            PluginFinder<MatterControlPlugin> pulginFinder = new PluginFinder<MatterControlPlugin>();
#endif
            string oemName = ApplicationSettings.Instance.GetOEMName();
            foreach (MatterControlPlugin plugin in pulginFinder.Plugins)
            {
                string pluginInfo = plugin.GetPluginInfoJSon();
                Dictionary<string, string> nameValuePairs = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(pluginInfo);

                if (nameValuePairs != null && nameValuePairs.ContainsKey("OEM"))
                {
                    if (nameValuePairs["OEM"] == oemName)
                    {
                        plugin.Initialize(this);
                    }
                }
                else
                {
                    plugin.Initialize(this);
                }
            }
        }

        private GuiWidget CreateMenues()
        {
            Menu dropListMenu = new Menu(new TextWidget("Action v"));
            dropListMenu.Name = "ListMenu Down";
            AddMenu(dropListMenu, "Walk");
            AddMenu(dropListMenu, "Jog");
            AddMenu(dropListMenu, "Run");
            return dropListMenu;
        }

        private void AddMenu(Menu listMenuToAddTo, string name)
        {
            GuiWidget normal = new TextWidget("-" + name);
            normal.HAnchor = Agg.UI.HAnchor.ParentLeftRight;
            normal.BackgroundColor = RGBA_Bytes.White;
            GuiWidget hover = new TextWidget("-" + name);
            hover.HAnchor = Agg.UI.HAnchor.ParentLeftRight;
            hover.BackgroundColor = RGBA_Bytes.LightGray;
            MenuItem menuItem = new MenuItem(new MenuItemStatesView(normal, hover));
            menuItem.HAnchor = Agg.UI.HAnchor.ParentLeftRight;
            listMenuToAddTo.MenuItems.Add(menuItem);
        }

        Stopwatch totalDrawTime = new Stopwatch();
        int drawCount = 0;

        public override void OnDraw(Graphics2D graphics2D)
        {
            totalDrawTime.Restart();
            base.OnDraw(graphics2D);
            totalDrawTime.Stop();

            if (ShowMemoryUsed)
            {
                long memory = GC.GetTotalMemory(false);
                this.Title = string.Format("Allocated = {0:n0} : {1}ms, d{2} Size = {3}x{4}", memory, totalDrawTime.ElapsedMilliseconds, drawCount++, this.Width, this.Height);
                if (DoCGCollectEveryDraw)
                {
                    GC.Collect();
                }
            }

            if (firstDraw)
            {
                string desktopPosition = ApplicationSettings.Instance.get("DesktopPosition");
                if (desktopPosition != null && desktopPosition != "")
                {
                    string[] sizes = desktopPosition.Split(',');
                    DesktopPosition = new Point2D(int.Parse(sizes[0]), int.Parse(sizes[1]));
                }
                
                firstDraw = false;
                foreach (string arg in commandLineArgs)
                {
                    if (Path.GetExtension(arg).ToUpper() == ".STL")
                    {
                        new PartPreviewMainWindow(new PrintItemWrapper(new DataStorage.PrintItem(Path.GetFileName(arg), Path.GetFullPath(arg))));
                    }
                }
            }
        }

        public override void OnMouseMove(MouseEventArgs mouseEvent)
        {
            if (GuiWidget.DebugBoundsUnderMouse)
            {
                Invalidate();
            }
            base.OnMouseMove(mouseEvent);
        }

        [STAThread]
        public static void Main()
        {
            Datastore.Instance.Initialize();

            // try and open our window matching the last size that we had for it.
            string windowSize = ApplicationSettings.Instance.get("WindowSize");
            int width = 600;
            int height = 640;
            if (windowSize != null && windowSize != "")
            {
                string[] sizes = windowSize.Split(',');
                width = int.Parse(sizes[0]);
                height = int.Parse(sizes[1]);
            }
            //MessageBox.ShowMessageBox(timerInfo, "Timing", MessageBox.MessageType.OK);
            new MatterControlApplication(width, height);
        }

        public override void OnClosed(EventArgs e)
        {
            // save the last size of the window so we can restore it next time.
            ApplicationSettings.Instance.set("WindowSize", string.Format("{0},{1}", Width, Height));
            ApplicationSettings.Instance.set("DesktopPosition", string.Format("{0},{1}", DesktopPosition.x, DesktopPosition.y));
            PrinterCommunication.Instance.Disable();
            //Close connection to the local datastore
            Datastore.Instance.Exit();
            PrinterCommunication.Instance.HaltConnectionThread();
            SlicingQueue.Instance.ShutDownSlicingThread();
            if (RestartOnClose)
            {
                string appPathAndFile = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string pathToAppFolder = Path.GetDirectoryName(appPathAndFile);

                ProcessStartInfo runAppLauncherStartInfo = new ProcessStartInfo();
                runAppLauncherStartInfo.Arguments = "\"{0}\" \"{1}\"".FormatWith(appPathAndFile, 1000);
                runAppLauncherStartInfo.FileName = Path.Combine(pathToAppFolder, "Launcher.exe");
                runAppLauncherStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                runAppLauncherStartInfo.CreateNoWindow = true;

                Process.Start(runAppLauncherStartInfo);
            }
            base.OnClosed(e);
        }

        public override void OnClosing(out bool CancelClose)
        {
            //Save a snapshot of the prints in queue
            PrintQueueControl.Instance.SaveDefaultQueue();

            if (PrinterCommunication.Instance.PrinterIsPrinting)
            {
                StyledMessageBox.ShowMessageBox("Oop! You cannot exit while a print is active.", "Unable to Exit");
                CancelClose = true;
            }
            else if (PartsSheet.IsSaving())
            {
                if (!StyledMessageBox.ShowMessageBox("You are currently saving a parts sheet, are you sure you want to exit?", "Confirm Exit", StyledMessageBox.MessageType.YES_NO))
                {
                    CancelClose = true;
                }
                else
                {
                    base.OnClosing(out CancelClose);
                }
            }
            else
            {
                base.OnClosing(out CancelClose);
            }
        }
    }
}
