using System;
using System.Collections.Generic;

namespace RoomAliveToolkit
{
    /// <summary>
    /// Holds intrinsic and extrinsic calibration data for a depth sensor.
    /// Subclass this for sensor-specific calibration recovery (e.g., Kinect2Calibration).
    /// </summary>
    public class SensorCalibration
    {
        public virtual int DepthImageWidth { get; set; }
        public virtual int DepthImageHeight { get; set; }
        public virtual int ColorImageWidth { get; set; }
        public virtual int ColorImageHeight { get; set; }

        public Matrix ColorCameraMatrix { get; set; }   // 3x3
        public Matrix ColorLensDistortion { get; set; }  // 2x1 or 5x1
        public Matrix DepthCameraMatrix { get; set; }    // 3x3
        public Matrix DepthLensDistortion { get; set; }  // 2x1 or 5x1
        public Matrix DepthToColorTransform { get; set; } // 4x4

        /// <summary>
        /// Compute the lookup table mapping depth pixels to camera-space rays.
        /// Each entry contains the (X/Z, Y/Z) undistorted direction for that pixel.
        /// </summary>
        public virtual System.Drawing.PointF[] ComputeDepthFrameToCameraSpaceTable()
        {
            return ComputeDepthFrameToCameraSpaceTable(DepthImageWidth, DepthImageHeight);
        }

        /// <summary>
        /// Compute the lookup table mapping depth pixels to camera-space rays at
        /// an arbitrary resolution.
        /// </summary>
        public System.Drawing.PointF[] ComputeDepthFrameToCameraSpaceTable(int tableWidth, int tableHeight)
        {
            float fx = (float)DepthCameraMatrix[0, 0];
            float fy = (float)DepthCameraMatrix[1, 1];
            float cx = (float)DepthCameraMatrix[0, 2];
            float cy = (float)DepthCameraMatrix[1, 2];
            float[] kappa = new float[] { (float)DepthLensDistortion[0], (float)DepthLensDistortion[1] };

            var table = new System.Drawing.PointF[tableWidth * tableHeight];

            for (int y = 0; y < tableHeight; y++)
                for (int x = 0; x < tableWidth; x++)
                {
                    double xout, yout;
                    double framex = (double)x / (double)tableWidth * DepthImageWidth;
                    double framey = (double)y / (double)tableHeight * DepthImageHeight;

                    CameraMath.Undistort(fx, fy, cx, cy, kappa, framex, (DepthImageHeight - framey), out xout, out yout);

                    var point = new System.Drawing.PointF();
                    point.X = (float)xout;
                    point.Y = (float)yout;
                    table[tableWidth * y + x] = point;
                }
            return table;
        }
    }

    /// <summary>
    /// Tracking state for a single body joint.
    /// </summary>
    public enum JointTrackingState
    {
        NotTracked,
        Inferred,
        Tracked
    }

    /// <summary>
    /// Joint identifiers matching the Kinect v2 25-joint skeleton layout.
    /// Other sensors should map their joint sets to these values where possible.
    /// </summary>
    public enum JointType
    {
        SpineBase = 0,
        SpineMid = 1,
        Neck = 2,
        Head = 3,
        ShoulderLeft = 4,
        ElbowLeft = 5,
        WristLeft = 6,
        HandLeft = 7,
        ShoulderRight = 8,
        ElbowRight = 9,
        WristRight = 10,
        HandRight = 11,
        HipLeft = 12,
        KneeLeft = 13,
        AnkleLeft = 14,
        FootLeft = 15,
        HipRight = 16,
        KneeRight = 17,
        AnkleRight = 18,
        FootRight = 19,
        SpineShoulder = 20,
        HandTipLeft = 21,
        ThumbLeft = 22,
        HandTipRight = 23,
        ThumbRight = 24
    }

    /// <summary>
    /// A single joint position and its tracking confidence.
    /// </summary>
    public struct BodyJoint
    {
        public System.Numerics.Vector3 Position;
        public JointTrackingState TrackingState;
    }

    /// <summary>
    /// Represents a tracked human body with skeleton joint data.
    /// </summary>
    public class TrackedBody
    {
        public ulong TrackingId;
        public bool IsTracked;
        public Dictionary<JointType, BodyJoint> Joints;
    }

    /// <summary>
    /// Abstraction for depth sensors, decoupling calibration and frame acquisition
    /// from any specific SDK (Kinect v2, Azure Kinect, RealSense, etc.).
    /// </summary>
    public interface IDepthSensor : IDisposable
    {
        /// <summary>
        /// The sensor's calibration data (intrinsics, extrinsics, image dimensions).
        /// </summary>
        SensorCalibration Calibration { get; }

        // ----- Frame acquisition (blocking, returns the latest available frame) -----

        /// <summary>
        /// Acquire the latest depth frame as raw little-endian uint16 values (millimeters).
        /// Array length = DepthImageWidth * DepthImageHeight * 2.
        /// </summary>
        byte[] AcquireDepthFrame();

        /// <summary>
        /// Acquire the latest color frame in YUY2 format.
        /// Array length = ColorImageWidth * ColorImageHeight * 2.
        /// </summary>
        byte[] AcquireColorFrameYUV();

        /// <summary>
        /// Acquire the latest color frame in BGRA32 format.
        /// Array length = ColorImageWidth * ColorImageHeight * 4.
        /// </summary>
        byte[] AcquireColorFrameRGB();

        /// <summary>
        /// Acquire the latest color frame as JPEG-compressed bytes.
        /// </summary>
        byte[] AcquireColorFrameJPEG();

        // ----- Optional capabilities -----

        /// <summary>
        /// Whether this sensor supports skeleton/body tracking.
        /// </summary>
        bool SupportsBodyTracking { get; }

        /// <summary>
        /// Acquire the latest body tracking frame. Returns an array of tracked bodies
        /// (typically up to 6). Returns null or empty if body tracking is not supported.
        /// </summary>
        TrackedBody[] AcquireBodyFrame();

        // ----- Camera exposure settings -----

        /// <summary>
        /// The gain value from the most recently acquired color frame.
        /// </summary>
        float LastColorGain { get; }

        /// <summary>
        /// The exposure time in ticks from the most recently acquired color frame.
        /// </summary>
        long LastColorExposureTimeTicks { get; }
    }
}
