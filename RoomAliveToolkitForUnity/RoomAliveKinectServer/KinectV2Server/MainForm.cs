using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;
using System.Xml.Serialization;
using RoomAliveToolkit;


namespace KinectV2Server
{
    public partial class MainForm : Form
    {

        #region Variables
        public static MainForm instance;
        public bool ShowTimingInformation = false;
        public const string version_number = "Release 2.0.0";

        enum DisplayType { Depth, Color, InfraRed, DepthForeground, DepthBackground, BodyIndex }
        DisplayType display = DisplayType.Depth;

        public enum MessageTypes { DEPTH, IR, BODYINDEX, DEPTH_FOREGROUND, COLOR_NONE, COLOR_JPEG, COLOR_RAWYUY, AUDIO}

        private KinectServerSettings settings;
        private string settingsFileName = "KinectServerSettings.xml";

        private TCPNetworkStreamer depthServer;
        private TCPNetworkStreamer colorServer;
        private TCPNetworkStreamer audioServer;
        private TCPNetworkStreamer skeletonServer;
        private TCPNetworkStreamer infraredServer;
        private TCPNetworkStreamer configurationServer;

        Thread processColorImageThread = null;

        AutoResetEvent nextColorFrameReadyForProcess = new AutoResetEvent(false);

        // Display variables - using System.Drawing instead of SharpDX
        private Bitmap depthDisplayBitmap;
        private Bitmap colorDisplayBitmap;

        // easy access variables
        public const int depthImageWidth = 512;
        public const int depthImageHeight = 424;
        public const int colorImageWidth = 1920;
        public const int colorImageHeight = 1080;
        public const float depthToColorWidthRatio = depthImageWidth / colorImageWidth;
        public float lastColorGain;
        public long lastColorExposureTimeTicks;

        private ShortImage mDepthImage, mInfraredImage, mDepthForegroundImage, mDepthPlayerIndexImage, mDepthBackgroundImage, mSmoothedDepthForegroundImage;
        private ShortImage mDepthImageM, mInfraredImageM;
        private ByteImage mBodyIndexImage, mBodyIndexImageM;
        private ARGBImage mDepthDisplayImage, mColorImageM, mColorImage;
        private ShortImage mColorImageYUY, mColorImageYUYM; // YUY is 16 bits/pixel

        private static System.Object mDepthFrameDataLock = new System.Object();
        private ushort[] mDepthFrameData;

        public Float2Image depthFrameToCameraSpaceTable, depthFrameToCameraSpaceTableFlipped;

        bool mRunningKinect = false;

        string backgroundFileName = "Background";
        int acquireBackgroundCounter = 0;
        PreProcessDepthMap_Variance preProcess;

        // Sensor - uses IDepthSensor abstraction instead of KinectSensor directly
        private IDepthSensor mDepthSensor = null;
        private Stopwatch kinectTimer = new Stopwatch();

        private byte[] nextFrameDepth = new byte[1];
        public byte[] currentFrameDepth = new byte[1];
        private byte[] nextFrameIr = new byte[1];
        public byte[] currentFrameIr = new byte[1];
        private byte[] nextFrameColor = new byte[1];
        public byte[] currentFrameColor = new byte[1];
        private byte[] nextFrameColorRaw = new byte[1];
        public byte[] currentFrameColorRaw = new byte[1];
        private byte[] nextFrameSkeleton = new byte[1];
        public byte[] currentFrameSkeleton = new byte[1];
        private byte[] nextFrameAudio = new byte[1];
        public byte[] currentFrameAudio = new byte[1];


        AutoResetEvent nextFrameReady = new AutoResetEvent(false);
        AutoResetEvent nextColorFrameReady = new AutoResetEvent(false);
        AutoResetEvent nextAudioFrameReady = new AutoResetEvent(false);
        AutoResetEvent processColorThreadCompleted = new AutoResetEvent(false);
        bool applicationRunning = true;

        FrameRate fpsRendering = new FrameRate(1);
        FrameRate fpsServerDepth = new FrameRate(1);
        FrameRate fpsServerColor = new FrameRate(1);
        FrameRate fpsServerAudio = new FrameRate(1);
        FrameRate fpsKinectDepth = new FrameRate(1);
        FrameRate fpsKinectColor = new FrameRate(1);
        FrameRate fpsServerSkeleton = new FrameRate(1);

        WaitHandle[] framesReady = null;
        DateTime startTime;

        public Kinect2Calibration kinect2Calibration;

#endregion

        #region Initialization
        static void Swap<T>(ref T lhs, ref T rhs)
        {
            T temp;
            temp = lhs;
            lhs = rhs;
            rhs = temp;
        }

        public MainForm(string[] args)
        {
            instance = this;

            settings = KinectServerSettings.Load(settingsFileName);

            InitializeComponent();


        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            this.Text = "KinectV2Server " + version_number;
            InitBuffers();
            startTime = DateTime.Now;

            //Start sensor on new thread
            Thread t0 = new Thread(InitSensor);
            t0.Priority = ThreadPriority.AboveNormal;
            t0.Start();
            Thread.Sleep(0);

            // TODO: Audio capture is not yet implemented for the modernized version.
            // The old code used SharpDX.DirectSound for audio capture.
            // For now, audio streaming is disabled.
            checkBoxStreamAudio.Checked = false;
            settings.StreamAudio = false;

            // Create display bitmaps (replacing SharpDX Direct2D rendering)
            depthDisplayBitmap = new Bitmap(depthImageWidth, depthImageHeight, PixelFormat.Format32bppArgb);
            colorDisplayBitmap = new Bitmap(colorImageWidth, colorImageHeight, PixelFormat.Format32bppArgb);

            // Create an array of wait handles for frame events
            framesReady = new WaitHandle[2];
            framesReady[0] = nextFrameReady;
            framesReady[1] = nextColorFrameReady;

            //starting server
            depthServer = new TCPNetworkStreamer(true, settings.depthPort, "Depth");
            colorServer = new TCPNetworkStreamer(true, settings.colorPort, "Color");
            audioServer = new TCPNetworkStreamer(true, settings.audioPort, "Audio");
            skeletonServer = new TCPNetworkStreamer(true, settings.skeletonPort, "Skeleton");
            infraredServer = new TCPNetworkStreamer(true, settings.infraredPort, "Infrared");
            configurationServer = new TCPNetworkStreamer(true, settings.configurationPort, "Configuration");
            configurationServer.ReceivedMessage += new TCPNetworkStreamer.ReceivedMessageEventHandler(ReceiveConfigurationRequest);

            Thread t1 = new Thread(ProcessingThread);
            t1.Start();

            Thread.Sleep(0);

            checkBoxStreamColor.Checked = settings.StreamColor;
            checkBoxSkeleton.Checked = settings.RenderSkeleton;
            checkBoxFlip.Checked = settings.FlipImages;

            trackBarThreshold.Value = settings.ThresholdNoise;

            comboBoxColorCompression.SelectedText = settings.colorCompression == KinectServerSettings.ColorCompressionType.JPEG ? "JPEG" : "NONE";
            comboBoxStreamType.SelectedText = settings.streamType == KinectServerSettings.StreamType.All ? "Full Depth" : (settings.streamType == KinectServerSettings.StreamType.Foreground ? "Foreground Depth" : "Body Index Depth");

            Console.WriteLine("KinectV2Server initialized!");
        }

        private unsafe void InitBuffers()
        {
            // Create Images
            mDepthDisplayImage = new ARGBImage(depthImageWidth, depthImageHeight);
            mInfraredImage = new ShortImage(depthImageWidth, depthImageHeight);
            mDepthImage = new ShortImage(depthImageWidth, depthImageHeight);
            mBodyIndexImage = new ByteImage(depthImageWidth, depthImageHeight);
            mInfraredImageM = new ShortImage(depthImageWidth, depthImageHeight);
            mDepthImageM = new ShortImage(depthImageWidth, depthImageHeight);
            mBodyIndexImageM = new ByteImage(depthImageWidth, depthImageHeight);
            mColorImage = new ARGBImage(colorImageWidth, colorImageHeight);
            mColorImageM = new ARGBImage(colorImageWidth, colorImageHeight);
            mColorImageYUY = new ShortImage(colorImageWidth, colorImageHeight);
            mColorImageYUYM = new ShortImage(colorImageWidth, colorImageHeight);
            mDepthForegroundImage = new ShortImage(depthImageWidth, depthImageHeight);
            mSmoothedDepthForegroundImage = new ShortImage(depthImageWidth, depthImageHeight);
            mDepthBackgroundImage = new ShortImage(depthImageWidth, depthImageHeight);
            mDepthPlayerIndexImage = new ShortImage(depthImageWidth, depthImageHeight);

            mDepthFrameData = new ushort[depthImageWidth * depthImageHeight];

            if (File.Exists(backgroundFileName + ".bin") && File.Exists(backgroundFileName + ".jpg"))
            {
                Console.WriteLine("Loading background image from file " + backgroundFileName + ".bin");
                mDepthBackgroundImage.LoadFromFile(backgroundFileName + ".bin");
            }
            else
            {
                Console.WriteLine("Starting background acquisition...");

                acquireBackgroundCounter = 100;
            }

            preProcess = new PreProcessDepthMap_Variance(depthImageWidth, depthImageHeight);
        }

        private void InitSensor()
        {
            Console.WriteLine("Initializing depth sensor.");

            // TODO: Replace this with actual sensor creation.
            // For Azure Kinect, use: mDepthSensor = new AzureKinectSensor();
            // For Kinect v2, a new Kinect2Sensor implementation of IDepthSensor would be needed.
            Console.WriteLine("WARNING: No IDepthSensor implementation instantiated. Sensor features are stubbed out.");
            Console.WriteLine("To use a real sensor, create an IDepthSensor implementation and assign it to mDepthSensor.");

            // If we had a sensor, we would start frame acquisition threads here.
            // For now, load calibration from file if available.
            if (File.Exists("kinect2calibration.xml"))
            {
                Console.WriteLine("Loading Kinect2Calibration from file: kinect2calibration.xml");
                var serializer = new XmlSerializer(typeof(Kinect2Calibration));
                using (var fs = new FileStream("kinect2calibration.xml", FileMode.Open))
                {
                    kinect2Calibration = (Kinect2Calibration)serializer.Deserialize(fs);
                }

                InitDepthCameraSpaceTable();
                StartSensorThreads();
            }
            else
            {
                Console.WriteLine("No calibration file found. Waiting for sensor...");
                // If a sensor is available, calibration would be recovered from it.
                if (mDepthSensor != null)
                {
                    kinect2Calibration = (Kinect2Calibration)mDepthSensor.Calibration;
                    InitDepthCameraSpaceTable();
                    StartSensorThreads();
                }
            }
        }

        private void InitDepthCameraSpaceTable()
        {
            Console.WriteLine("Computing DepthFrameToCameraSpaceTable...");
            var tableEntries = kinect2Calibration.ComputeDepthFrameToCameraSpaceTable();

            var buffer = new float[depthImageWidth * depthImageHeight * 2];
            var bufferFlipped = new float[depthImageWidth * depthImageHeight * 2];

            int j = 0;
            for (int r = 0; r < depthImageHeight; r++)
                for (int c = 0; c < depthImageWidth; c++)
                {
                    var ray = tableEntries[r * depthImageWidth + c];
                    buffer[j] = ray.X;
                    buffer[j + 1] = ray.Y;

                    var rayFlipped = tableEntries[r * depthImageWidth + (depthImageWidth - 1 - c)];
                    bufferFlipped[j] = rayFlipped.X;
                    bufferFlipped[j + 1] = rayFlipped.Y;

                    j += 2;
                }

            depthFrameToCameraSpaceTable = new Float2Image(depthImageWidth, depthImageHeight);
            depthFrameToCameraSpaceTable.FromArray(buffer);

            depthFrameToCameraSpaceTableFlipped = new Float2Image(depthImageWidth, depthImageHeight);
            depthFrameToCameraSpaceTableFlipped.FromArray(bufferFlipped);
        }

        private void StartSensorThreads()
        {
            mRunningKinect = true;
            kinectTimer.Start();

            // Start depth frame acquisition thread
            Thread depthThread = new Thread(DepthAcquisitionLoop);
            depthThread.IsBackground = true;
            depthThread.Start();

            // Start color frame acquisition thread
            Thread colorThread = new Thread(ColorAcquisitionLoop);
            colorThread.IsBackground = true;
            colorThread.Start();
        }

        private unsafe void DepthAcquisitionLoop()
        {
            while (applicationRunning && mDepthSensor != null)
            {
                try
                {
                    fpsKinectDepth.Tick();

                    // Acquire depth frame
                    byte[] depthBytes = mDepthSensor.AcquireDepthFrame();
                    if (depthBytes != null)
                    {
                        fixed (byte* pSrc = depthBytes)
                        {
                            mDepthImageM.Copy((IntPtr)pSrc);
                        }
                        lock (mDepthImage)
                        {
                            if (settings.FlipImages)
                                mDepthImage.XMirror(mDepthImageM);
                            else
                                Swap<ShortImage>(ref mDepthImage, ref mDepthImageM);
                        }

                        // Copy to ushort array for depth frame data
                        lock (mDepthFrameDataLock)
                        {
                            Buffer.BlockCopy(depthBytes, 0, mDepthFrameData, 0, depthBytes.Length);
                        }
                    }

                    // TODO: Acquire infrared and body index frames from sensor if supported
                    // For now these remain zeroed out.

                    // Background acquisition
                    if (acquireBackgroundCounter > 0)
                    {
                        preProcess.Update(mDepthImage);
                        acquireBackgroundCounter--;
                        if (acquireBackgroundCounter == 0)
                        {
                            Console.WriteLine("Acquired enough frames. Computing background image!");
                            FloatImage avgDepth;
                            FloatImage varDepthIm;
                            ByteImage mask;

                            preProcess.Compute(out avgDepth, out varDepthIm, out mask);
                            avgDepth.Mult(1000f);

                            lock (mDepthBackgroundImage)
                            {
                                mDepthBackgroundImage.Copy(avgDepth);
                                Console.WriteLine("Saving background DEPTH image to file " + backgroundFileName + ".bin");
                                mDepthBackgroundImage.SaveToFile(backgroundFileName + ".bin");
                            }
                        }
                    }

                    // Foreground computation
                    lock (mDepthForegroundImage)
                    {
                        ushort* pIn = mDepthImage.Data(0, 0);
                        ushort* pBack = mDepthBackgroundImage.Data(0, 0);
                        ushort* pOut = mDepthForegroundImage.Data(0, 0);
                        byte* pBody = mBodyIndexImage.Data(0, 0);

                        for (int i = 0; i < depthImageWidth * depthImageHeight; i++)
                        {
                            if (*pBack > settings.ThresholdNoise)
                                *pOut++ = (*pIn < (*pBack - settings.ThresholdNoise) || *pBody != 255) ? *pIn : (byte)0;
                            else
                                *pOut++ = *pIn;

                            pIn++; pBack++; pBody++;
                        }
                    }

                    if (settings.BlurDepthImages)
                    {
                        lock (mSmoothedDepthForegroundImage)
                            mSmoothedDepthForegroundImage.Blur5x5NonZero(mDepthForegroundImage);
                    }

                    lock (mDepthPlayerIndexImage)
                    {
                        ushort* pIn = mDepthImage.Data(0, 0);
                        byte* pMask = mBodyIndexImage.Data(0, 0);
                        ushort* pOut = mDepthPlayerIndexImage.Data(0, 0);

                        for (int i = 0; i < depthImageWidth * depthImageHeight; i++)
                        {
                            *pOut++ = (*pMask != 255) ? *pIn : (byte)0;
                            *pMask++ *= 40;
                            pIn++;
                        }
                    }

                    long currTime = kinectTimer.ElapsedMilliseconds;

                    // Assemble skeleton packet (empty for now - no body tracking without Kinect SDK)
                    AssembleSkeletonsPacket(currTime);
                    lock (nextFrameSkeleton)
                    {
                        Swap<byte[]>(ref nextFrameSkeleton, ref currentFrameSkeleton);
                    }

                    lock (nextFrameDepth)
                    {
                        switch (settings.streamType)
                        {
                            case KinectServerSettings.StreamType.All:
                                nextFrameDepth = AssembleGenericImagePacket(currTime, mDepthImage.DataIntPtr, depthImageWidth * depthImageHeight * 2, (int)MessageTypes.DEPTH);
                                break;
                            case KinectServerSettings.StreamType.BodyIndex:
                                nextFrameDepth = AssembleGenericImagePacket(currTime, mDepthPlayerIndexImage.DataIntPtr, depthImageWidth * depthImageHeight * 2, (int)MessageTypes.BODYINDEX);
                                break;
                            default:
                            case KinectServerSettings.StreamType.Foreground:
                                if (settings.BlurDepthImages)
                                    nextFrameDepth = AssembleGenericImagePacket(currTime, mSmoothedDepthForegroundImage.DataIntPtr, depthImageWidth * depthImageHeight * 2, (int)MessageTypes.DEPTH_FOREGROUND);
                                else
                                    nextFrameDepth = AssembleGenericImagePacket(currTime, mDepthForegroundImage.DataIntPtr, depthImageWidth * depthImageHeight * 2, (int)MessageTypes.DEPTH_FOREGROUND);
                                break;
                        }
                        Swap<byte[]>(ref nextFrameDepth, ref currentFrameDepth);
                    }

                    lock (nextFrameIr)
                    {
                        nextFrameIr = AssembleGenericImagePacket(currTime, mInfraredImage.DataIntPtr, depthImageWidth * depthImageHeight * 2, (int)MessageTypes.IR);
                        Swap<byte[]>(ref nextFrameIr, ref currentFrameIr);
                    }

                    nextFrameReady.Set();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("DepthAcquisitionLoop exception: " + ex.ToString());
                }

                Thread.Sleep(1); // ~30fps target
            }
        }

        private unsafe void ColorAcquisitionLoop()
        {
            while (applicationRunning && mDepthSensor != null)
            {
                try
                {
                    fpsKinectColor.Tick();

                    byte[] colorYUV = mDepthSensor.AcquireColorFrameYUV();
                    if (colorYUV != null)
                    {
                        lock (mColorImage)
                        {
                            fixed (byte* pSrc = colorYUV)
                            {
                                mColorImageYUYM.Copy((IntPtr)pSrc);
                            }
                            if (settings.FlipImages)
                                mColorImageYUY.XMirror_YUYSpecial(mColorImageYUYM);
                            else
                                Swap<ShortImage>(ref mColorImageYUY, ref mColorImageYUYM);

                            // TODO: Convert YUV to BGRA for display
                            // For now, color display will be blank until a conversion is implemented
                        }

                        if (settings.StreamColor && processColorImageThread == null)
                        {
                            processColorImageThread = new Thread(ProcessColorImage);
                            processColorImageThread.Start();
                        }

                        nextColorFrameReadyForProcess.Set();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ColorAcquisitionLoop exception: " + ex.ToString());
                }

                Thread.Sleep(1);
            }
        }

        Dictionary<int, long> clientsToClose = new Dictionary<int, long>();
        public void ReceiveConfigurationRequest(object sender, ReceivedMessageEventArgs e)
        {
            byte[] request = e.data;
            byte requestType = e.data[0];
            int id = ((ClientState)sender).ID;

            switch(requestType)
            {
                case 1:    //send the depthFrameToCameraSpaceTable

                    var res1 = this.checkBoxFlip.Checked ? depthFrameToCameraSpaceTableFlipped.ToByteArray() : depthFrameToCameraSpaceTable.ToByteArray();

                    Thread.Sleep(1000); //hack, otherwise Unity crashes

                    byte[] message1 = new byte[res1.Length + 1];
                    message1[0] = (byte)1; //message type
                    Array.Copy(res1, 0, message1, 1, res1.Length);

                    Console.WriteLine("Configuration Server received a request from " + id + " request:" + request[0] + " responseLen:" + message1.Length);
                    configurationServer.SendMessageToClient(message1, id);
                    break;
                case 2: //send the serialized Kinect2Calibration

                    XmlSerializer serializer = new XmlSerializer(typeof(Kinect2Calibration));
                    MemoryStream ms = new MemoryStream();
                    var writer = new StreamWriter(ms);
                    serializer.Serialize(writer, kinect2Calibration);
                    byte[] res2 = ms.ToArray();
                    writer.Close();

                    Thread.Sleep(1000);//hack, otherwise Unity crashes

                    byte[] message2 = new byte[res2.Length + 1];
                    message2[0] = (byte)2; //message type
                    Array.Copy(res2, 0, message2, 1, res2.Length);
                    Console.WriteLine("Configuration Server received a request from " + id + " request:" + request[0] + " responseLen:" + message2.Length);
                    configurationServer.SendMessageToClient(message2, id);
                    break;
                case 0:
                    Console.WriteLine("Configuration Server received a request to terminate client " + id + " connection.");
                    clientsToClose.Add(id, DateTime.Now.Ticks+10000000);
                    break;
            }
        }

        private void Stop()
        {
            applicationRunning = false;
            nextFrameReady.Set();

            if (mDepthSensor != null)
            {
                mDepthSensor.Dispose();
                mDepthSensor = null;
            }

            if (depthServer != null)
            {
                depthServer.Close();
                depthServer = null;
            }
            if (colorServer != null)
            {
                colorServer.Close();
                colorServer = null;
            }
            if (skeletonServer != null)
            {
                skeletonServer.Close();
                skeletonServer = null;
            }
            if (audioServer != null)
            {
                audioServer.Close();
                audioServer = null;
            }
            if (infraredServer != null)
            {
                infraredServer.Close();
                infraredServer = null;
            }
            if (configurationServer != null)
            {
                configurationServer.Close();
                configurationServer = null;
            }
        }
#endregion

#region Processing Thread

        System.Diagnostics.Stopwatch stopwatch3 = new System.Diagnostics.Stopwatch();

        // Send messages to clients
        private void ProcessingThread()
        {
            while (applicationRunning)
            {
                stopwatch3.Reset();
                stopwatch3.Start();


                if(clientsToClose.Count>0) //house cleaning
                {
                    long now = DateTime.Now.Ticks;
                    int[] list = new int[clientsToClose.Count];
                    clientsToClose.Keys.CopyTo(list,0);
                    foreach (int id in list)
                    {
                        if(now > clientsToClose[id] )
                        {
                            configurationServer.CloseClient(id);
                            clientsToClose.Remove(id);
                        }
                    }
                }

                if (killAllClients)
                {
                    if (depthServer != null) depthServer.CloseAllClients();
                    if (colorServer != null) colorServer.CloseAllClients();
                    if (audioServer != null) audioServer.CloseAllClients();
                    if (skeletonServer != null) skeletonServer.CloseAllClients();
                    if (configurationServer != null) configurationServer.CloseAllClients();
                    killAllClients = false;
                }

                int signalIndex = WaitHandle.WaitAny(framesReady);

                if (signalIndex == 0) // depth and skeleton
                {
                    //don't do the work if there are no clients connected...
                    if (depthServer.GetClientCount() > 0)
                    {
                        depthServer.SendMessageToAllClients(currentFrameDepth);
                        fpsServerDepth.Tick();
                    }
                    if (skeletonServer.GetClientCount() > 0)
                    {
                        skeletonServer.SendMessageToAllClients(currentFrameSkeleton);
                        fpsServerSkeleton.Tick();
                    }

                    if (this.WindowState != FormWindowState.Minimized) Render();
                }
                else if (settings.StreamColor && signalIndex == 1) // color
                {
                    if (colorServer.GetClientCount() > 0 && currentFrameColor.Length > 1)
                    {
                        colorServer.SendMessageToAllClients(currentFrameColor);

                        fpsServerColor.Tick();
                    }
                }
                Thread.Sleep(0); //makes sure that UI thread is not starved
            }
        }
#endregion

#region Rendering

        void Render()
        {
            fpsRendering.Tick();
            this.BeginInvoke(new InvokeDelegate(UpdateLabels));

            // Simple GDI+ rendering to replace SharpDX Direct2D
            switch (display)
            {
                case DisplayType.Depth:
                    lock (mDepthImage)
                    {
                        mDepthDisplayImage.CopyShortImageForGrayscaleDisplay(mDepthImage, 8000);
                    }
                    break;
                case DisplayType.Color:
                    // handled below
                    break;
                case DisplayType.InfraRed:
                    lock (mInfraredImage)
                    {
                        mDepthDisplayImage.CopyShortImageForGrayscaleDisplay(mInfraredImage, ushort.MaxValue);
                    }
                    break;
                case DisplayType.DepthForeground:
                    lock (mDepthForegroundImage)
                    {
                        if(settings.BlurDepthImages) mDepthDisplayImage.CopyShortImageForGrayscaleDisplay(mSmoothedDepthForegroundImage, 8000);
                        else mDepthDisplayImage.CopyShortImageForGrayscaleDisplay(mDepthForegroundImage, 8000);
                    }
                    break;
                case DisplayType.DepthBackground:
                    lock (mDepthBackgroundImage)
                    {
                        mDepthDisplayImage.CopyShortImageForGrayscaleDisplay(mDepthBackgroundImage, 8000);
                    }
                    break;
                case DisplayType.BodyIndex:
                    lock (mDepthForegroundImage)
                    {
                        mDepthDisplayImage.Copy(mBodyIndexImage);
                    }
                    break;
            }

            // Render using GDI+
            try
            {
                using (var g = panelDisplay.CreateGraphics())
                {
                    if (display != DisplayType.Color)
                    {
                        // Copy ARGB data to bitmap
                        var bmpData = depthDisplayBitmap.LockBits(
                            new Rectangle(0, 0, depthImageWidth, depthImageHeight),
                            ImageLockMode.WriteOnly,
                            PixelFormat.Format32bppArgb);
                        unsafe
                        {
                            Win32.CopyMemory(bmpData.Scan0, mDepthDisplayImage.DataIntPtr, (UIntPtr)(depthImageWidth * depthImageHeight * 4));
                        }
                        depthDisplayBitmap.UnlockBits(bmpData);
                        g.DrawImage(depthDisplayBitmap, 0, 0, panelDisplay.Width, panelDisplay.Height);
                    }
                    else
                    {
                        var bmpData = colorDisplayBitmap.LockBits(
                            new Rectangle(0, 0, colorImageWidth, colorImageHeight),
                            ImageLockMode.WriteOnly,
                            PixelFormat.Format32bppArgb);
                        unsafe
                        {
                            Win32.CopyMemory(bmpData.Scan0, mColorImage.DataIntPtr, (UIntPtr)(colorImageWidth * colorImageHeight * 4));
                        }
                        colorDisplayBitmap.UnlockBits(bmpData);
                        g.DrawImage(colorDisplayBitmap, 0, 0, panelDisplay.Width, panelDisplay.Height);
                    }
                }
            }
            catch (Exception)
            {
                // Ignore rendering errors (e.g. form closing)
            }
        }

        public delegate void InvokeDelegate();
        void UpdateLabels()
        {
            if (!applicationRunning) return;

            checkBoxStreamColor.Checked = settings.StreamColor;
            TimeSpan upTime = DateTime.Now - startTime;
            labelUpTime.Text = upTime.ToString();
            if (acquireBackgroundCounter <= 0) acquireBackgroundToolStripMenuItem.Enabled = true;
            labelDepthFPS.Text = Math.Round(fpsKinectDepth.Framerate, 2).ToString() + " Hz";
            labelColorFPS.Text = Math.Round(fpsKinectColor.Framerate, 2).ToString() + " Hz";

            labelDepth.Text = depthServer.GetClientCount().ToString() + "  ("+ Math.Round(fpsServerDepth.Framerate, 2).ToString() + " Hz)";
            labelColor.Text = colorServer.GetClientCount().ToString() + "  (" + Math.Round(fpsServerColor.Framerate, 2).ToString() + " Hz)";
            labelSkeleton.Text = skeletonServer.GetClientCount().ToString() + "  (" + Math.Round(fpsServerSkeleton.Framerate, 2).ToString() + " Hz)";
            labelAudio.Text = audioServer.GetClientCount().ToString() + "  (" + Math.Round(fpsServerAudio.Framerate, 2).ToString() + " Hz)";
            labelConfig.Text = configurationServer.GetClientCount().ToString();

            labelBodies.Text = "0"; // TODO: Body tracking not implemented without Kinect SDK
            labelThreshold.Text = settings.ThresholdNoise.ToString();
        }
#endregion

#region Color Processing

        bool killProcessColorThread = false;
        private void ProcessColorImage()
        {
            while (settings.StreamColor)
            {
                bool ok = nextColorFrameReadyForProcess.WaitOne(10000);
                if (!ok) break;
                long ticks = kinectTimer.ElapsedMilliseconds;

                lock (nextFrameColor)
                {
                    switch(settings.colorCompression)
                    {
                        case KinectServerSettings.ColorCompressionType.NONE:
                            nextFrameColor = AssembleGenericImagePacket(ticks, mColorImage.DataIntPtr, colorImageWidth * colorImageHeight * 4, (int)MessageTypes.COLOR_NONE);
                            break;
                        case KinectServerSettings.ColorCompressionType.JPEG:
                            nextFrameColor = AssembleColorPacketJPEG(ticks, mColorImage);
                            break;
                    }

                    Swap<byte[]>(ref nextFrameColor, ref currentFrameColor);
                }

                if (settings.ProcessColorRAW)
                {
                    lock(nextFrameColorRaw)
                    {
                        nextFrameColorRaw = AssembleGenericImagePacket(ticks, mColorImageYUY.DataIntPtr, colorImageWidth*colorImageHeight*2, (int)MessageTypes.COLOR_RAWYUY);
                        Swap<byte[]>(ref nextFrameColorRaw, ref currentFrameColorRaw);
                    }
                }
                nextColorFrameReady.Set();
            }

            killProcessColorThread = true;

            processColorThreadCompleted.Set();
        }

        public void ResetColorStreaming(KinectServerSettings.ColorCompressionType newCompressionType)
        {
            settings.StreamColor = false;
            bool success = processColorThreadCompleted.WaitOne(300);
            Console.WriteLine("Reset color streaming success = " + success);
            settings.colorCompression = newCompressionType;
            settings.StreamColor = true;
        }
        public void ResetColorStreaming()
        {
            ResetColorStreaming(settings.colorCompression);
        }

        /// <summary>
        /// Assembles a generic image packet with a timestamp (long) followed by a size of the image (int) and the image data
        /// </summary>
        private unsafe byte[] AssembleGenericImagePacket(long timeStamp, IntPtr imageDataPtr, int imageSizeBytes, int packetType)
        {
            stopwatch4.Reset();
            stopwatch4.Start();
            int preambleLength = 8 + 4;
            byte[] arr = new byte[imageSizeBytes + preambleLength];

            using (MemoryStream memoryStream = new MemoryStream(arr))
            {
                memoryStream.Write(BitConverter.GetBytes(timeStamp), 0, 8);
                memoryStream.Write(BitConverter.GetBytes(packetType), 0, 4);
            }

            //now move the data to the managed heap
            fixed (byte* p = arr)
            {
                byte* p1 = p + preambleLength;
                Win32.CopyMemory((IntPtr)p1, imageDataPtr, (UIntPtr)imageSizeBytes);
            }
            stopwatch4.Stop();
            return arr;
        }

        /// <summary>
        /// Assembles a color packet as JPEG using System.Drawing instead of WPF/WIC.
        /// </summary>
        private unsafe byte[] AssembleColorPacketJPEG(long timeStamp, ARGBImage colorImage)
        {
            stopwatch4.Reset();
            stopwatch4.Start();

            using (MemoryStream memoryStream = new MemoryStream())
            {
                memoryStream.Write(BitConverter.GetBytes(timeStamp), 0, 8);
                memoryStream.Write(BitConverter.GetBytes((int)MessageTypes.COLOR_JPEG), 0, 4);

                // Create a Bitmap from the ARGB data and save as JPEG
                using (var bmp = new Bitmap(colorImage.Width, colorImage.Height, PixelFormat.Format32bppArgb))
                {
                    var bmpData = bmp.LockBits(
                        new Rectangle(0, 0, colorImage.Width, colorImage.Height),
                        ImageLockMode.WriteOnly,
                        PixelFormat.Format32bppArgb);
                    Win32.CopyMemory(bmpData.Scan0, colorImage.DataIntPtr, (UIntPtr)(colorImage.Width * colorImage.Height * 4));
                    bmp.UnlockBits(bmpData);

                    bmp.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Jpeg);
                }

                var buf = memoryStream.ToArray();

                stopwatch4.Stop();
                if (ShowTimingInformation) Console.WriteLine(string.Format("JPEG: {0} ms {1} bytes", stopwatch4.ElapsedMilliseconds, buf.Length));

                return buf;
            }
        }
#endregion

#region DepthProcessing
        System.Diagnostics.Stopwatch stopwatchTotal = new System.Diagnostics.Stopwatch();
        System.Diagnostics.Stopwatch stopwatch4 = new System.Diagnostics.Stopwatch();

        public long timeStampInfrared;

        private void AssembleSkeletonsPacket(long timeStamp)
        {
            // Assemble a message - currently empty since body tracking requires Kinect SDK
            // TODO: Implement body tracking via Azure Kinect Body Tracking SDK or alternative
            MemoryStream stream = new MemoryStream();
            BinaryWriter binaryWriter = new BinaryWriter(stream);

            binaryWriter.Write(timeStamp);

            binaryWriter.Write((byte)0); // zero skeletons

            // Write a default accelerometer reading (gravity pointing down)
            binaryWriter.Write(0f); // X
            binaryWriter.Write(-1f); // Y
            binaryWriter.Write(0f); // Z
            binaryWriter.Write(0f); // W (floor distance)

            lock (nextFrameSkeleton)
            {
                nextFrameSkeleton = stream.ToArray();
            }
        }

#endregion

#region Form Handling Methods
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Stop();
        }

        private void comboBoxDisplay_SelectedIndexChanged(object sender, EventArgs e)
        {
            display = (DisplayType)comboBoxDisplay.SelectedIndex;
        }

        private void checkBoxSkeleton_CheckedChanged(object sender, EventArgs e)
        {
            settings.RenderSkeleton = checkBoxSkeleton.Checked;
        }

        private void comboBoxStreamType_SelectedIndexChanged(object sender, EventArgs e)
        {
            settings.streamType = (KinectServerSettings.StreamType)comboBoxStreamType.SelectedIndex;
        }

        private void buttonBackground_Click(object sender, EventArgs e)
        {
            Console.WriteLine("Starting background acquisition...");
            acquireBackgroundToolStripMenuItem.Enabled = false;
            preProcess.Reset();
            acquireBackgroundCounter = 100;
        }

        private void trackBarThreshold_Scroll(object sender, EventArgs e)
        {
            settings.ThresholdNoise = (ushort)trackBarThreshold.Value;
        }

        private void checkBoxBlur_CheckedChanged(object sender, EventArgs e)
        {
            settings.BlurDepthImages = checkBoxBlur.Checked;
        }

        private void checkBoxStreamColor_CheckedChanged(object sender, EventArgs e)
        {
            settings.StreamColor = checkBoxStreamColor.Checked;
            if (!settings.StreamColor) checkBoxProcessRAW.Checked = false;
            checkBoxProcessRAW.Enabled = settings.StreamColor;
        }

        private void checkBoxStreamAudio_CheckedChanged(object sender, EventArgs e)
        {
            settings.StreamAudio = checkBoxStreamAudio.Checked;
        }

        private void comboBoxColorCompression_SelectedIndexChanged(object sender, EventArgs e)
        {
            ResetColorStreaming((KinectServerSettings.ColorCompressionType)comboBoxColorCompression.SelectedIndex);
        }

        private void checkBoxEncoderTiming_CheckedChanged(object sender, EventArgs e)
        {
            ShowTimingInformation = checkBoxEncoderTiming.Checked;
        }

        private void checkBoxProcessRAW_CheckedChanged(object sender, EventArgs e)
        {
            settings.ProcessColorRAW = checkBoxProcessRAW.Checked;
        }

        bool killAllClients = false;
        private void buttonKillAll_Click(object sender, EventArgs e)
        {
            Console.WriteLine("Killing all clients");

            killAllClients = true;
        }


        private void checkBoxRenderFaceTracking_CheckedChanged(object sender, EventArgs e)
        {
            settings.RenderFaces = checkBoxRenderFaceTracking.Checked;
        }


        private void checkBoxFlip_CheckedChanged(object sender, EventArgs e)
        {
            settings.FlipImages = checkBoxFlip.Checked;
        }


        private void saveSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            settings.Save(settingsFileName);
        }

        private void saveToOBJToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var saveFileDialog = new SaveFileDialog();

            saveFileDialog.Filter = "obj files (*.obj)|*.obj|All files (*.*)|*.*";
            saveFileDialog.FilterIndex = 0;
            saveFileDialog.RestoreDirectory = true;

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    Console.WriteLine("Saving OBJ file " + saveFileDialog.FileName);
                    Console.WriteLine("IMPORTANT: Make sure that Flip Image option is turned OFF when the background images are acquired! Otherwise the saved OBJ will not be correct!");
                    ObjFile.Save(saveFileDialog.FileName, kinect2Calibration, mDepthBackgroundImage, backgroundFileName + ".jpg", RoomAliveToolkit.Matrix.Identity(4,4));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Could not save OBJ file to disk.\n" + ex);
                }
            }
        }


        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }
#endregion
    }
}
