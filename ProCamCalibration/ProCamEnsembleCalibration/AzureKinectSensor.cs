// Requires NuGet package: Microsoft.Azure.Kinect.Sensor (>= 1.4.1)
// For body tracking, also requires: Microsoft.Azure.Kinect.BodyTracking (>= 1.1.2)
//
// Install via:
//   dotnet add package Microsoft.Azure.Kinect.Sensor
//   dotnet add package Microsoft.Azure.Kinect.BodyTracking

using System;
using System.Collections.Generic;

namespace RoomAliveToolkit
{
    /// <summary>
    /// Azure Kinect (DK) implementation of IDepthSensor.
    ///
    /// Default mode: NFOV Unbinned (640x576 depth), 1920x1080 color.
    /// Alternatively supports 3840x2160 color for higher-resolution calibration.
    /// </summary>
    public class AzureKinectSensor : IDepthSensor
    {
        // Azure Kinect NFOV Unbinned depth resolution
        private const int NfovDepthWidth = 640;
        private const int NfovDepthHeight = 576;

        // Color resolution options
        private const int ColorWidth1080p = 1920;
        private const int ColorHeight1080p = 1080;
        private const int ColorWidth2160p = 3840;
        private const int ColorHeight2160p = 2160;

        private SensorCalibration _calibration;
        private bool _disposed;

        // TODO: private Device _device;           // Microsoft.Azure.Kinect.Sensor.Device
        // TODO: private Tracker _bodyTracker;      // Microsoft.Azure.Kinect.BodyTracking.Tracker

        /// <summary>
        /// Create an AzureKinectSensor targeting the given device index.
        /// </summary>
        /// <param name="deviceIndex">Zero-based device index (default 0).</param>
        /// <param name="use4KColor">If true, use 3840x2160 color; otherwise 1920x1080.</param>
        public AzureKinectSensor(int deviceIndex = 0, bool use4KColor = false)
        {
            int colorW = use4KColor ? ColorWidth2160p : ColorWidth1080p;
            int colorH = use4KColor ? ColorHeight2160p : ColorHeight1080p;

            _calibration = new SensorCalibration
            {
                DepthImageWidth = NfovDepthWidth,
                DepthImageHeight = NfovDepthHeight,
                ColorImageWidth = colorW,
                ColorImageHeight = colorH,
            };

            // TODO: Open the device and start cameras:
            //
            // _device = Device.Open(deviceIndex);
            // _device.StartCameras(new DeviceConfiguration
            // {
            //     ColorFormat = ImageFormat.ColorBGRA32,  // or ColorMJPG
            //     ColorResolution = use4KColor ? ColorResolution.R3072p : ColorResolution.R1080p,
            //     DepthMode = DepthMode.NFOV_Unbinned,
            //     SynchronizedImagesOnly = true,
            //     CameraFPS = FPS.FPS30,
            // });
            //
            // Recover calibration:
            // var cal = _device.GetCalibration();
            // Populate _calibration.DepthCameraMatrix from cal.DepthCameraCalibration.Intrinsics
            // Populate _calibration.ColorCameraMatrix from cal.ColorCameraCalibration.Intrinsics
            // Populate _calibration.DepthToColorTransform from cal.GetExtrinsics(...)
            //
            // For body tracking:
            // _bodyTracker = Tracker.Create(cal, TrackerConfiguration.Default);

            throw new NotImplementedException("AzureKinectSensor: device initialization not yet implemented.");
        }

        public SensorCalibration Calibration
        {
            get { return _calibration; }
        }

        public byte[] AcquireDepthFrame()
        {
            // TODO: Capture a frame and extract the depth image:
            //
            // using (Capture capture = _device.GetCapture())
            // {
            //     Image depthImage = capture.Depth;
            //     // depthImage.GetPixels<ushort>() returns Memory<ushort>
            //     // Convert to byte[] (little-endian uint16, length = 640*576*2)
            //     return depthImage.Memory.ToArray();
            // }

            throw new NotImplementedException();
        }

        public byte[] AcquireColorFrameYUV()
        {
            // TODO: Azure Kinect does not natively output YUY2.
            // Option 1: Configure ColorFormat = ImageFormat.ColorYUY2 (only at 720p).
            // Option 2: Capture as BGRA32 and convert to YUY2 in software.
            //
            // using (Capture capture = _device.GetCapture())
            // {
            //     Image colorImage = capture.Color;
            //     // Convert BGRA -> YUY2
            //     return ConvertBgraToYuy2(colorImage.Memory);
            // }

            throw new NotImplementedException();
        }

        public byte[] AcquireColorFrameRGB()
        {
            // TODO: Capture in BGRA32 format:
            //
            // using (Capture capture = _device.GetCapture())
            // {
            //     Image colorImage = capture.Color;
            //     return colorImage.Memory.ToArray();  // BGRA32 bytes
            // }

            throw new NotImplementedException();
        }

        public byte[] AcquireColorFrameJPEG()
        {
            // TODO: Capture in MJPG format for native JPEG:
            //
            // Configure with ColorFormat = ImageFormat.ColorMJPG, then:
            // using (Capture capture = _device.GetCapture())
            // {
            //     Image colorImage = capture.Color;
            //     return colorImage.Memory.ToArray();  // JPEG bytes
            // }

            throw new NotImplementedException();
        }

        public bool SupportsBodyTracking
        {
            // Azure Kinect supports body tracking via the Body Tracking SDK
            get { return true; }
        }

        public TrackedBody[] AcquireBodyFrame()
        {
            // TODO: Use the body tracking SDK:
            //
            // using (Capture capture = _device.GetCapture())
            // {
            //     _bodyTracker.EnqueueCapture(capture);
            //     using (Frame bodyFrame = _bodyTracker.PopResult())
            //     {
            //         var bodies = new TrackedBody[bodyFrame.NumberOfBodies];
            //         for (uint i = 0; i < bodyFrame.NumberOfBodies; i++)
            //         {
            //             var skeleton = bodyFrame.GetBodySkeleton(i);
            //             var body = new TrackedBody
            //             {
            //                 TrackingId = bodyFrame.GetBodyId(i),
            //                 IsTracked = true,
            //                 Joints = new Dictionary<JointType, BodyJoint>()
            //             };
            //             // Map Azure Kinect's 32 joints to the 25-joint layout
            //             // Azure Kinect joint indices differ from Kinect v2
            //             // TODO: build mapping table from K4ABT_JOINT_* to JointType
            //             bodies[i] = body;
            //         }
            //         return bodies;
            //     }
            // }

            throw new NotImplementedException();
        }

        public float LastColorGain
        {
            // TODO: Read from capture metadata:
            // capture.Color.GetDeviceTimestamp() and related metadata fields
            // Azure Kinect SDK exposes ISO gain via image metadata.
            get { throw new NotImplementedException(); }
        }

        public long LastColorExposureTimeTicks
        {
            // TODO: Read from capture metadata:
            // capture.Color.Exposure returns a TimeSpan; convert to ticks.
            get { throw new NotImplementedException(); }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // TODO: Clean up managed resources:
                    // _bodyTracker?.Dispose();
                    // _device?.StopCameras();
                    // _device?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}
