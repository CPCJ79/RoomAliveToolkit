using System;
using System.Runtime.InteropServices;

namespace RoomAliveToolkit
{
    public class CoordinateMappingExample
    {
        /// <summary>
        /// Demonstrates how to use our sensor calibration to convert a depth image point to color image coordinates.
        /// Uses the IDepthSensor abstraction instead of Kinect SDK directly.
        /// </summary>
        /// <param name="calibration">The sensor's calibration data.</param>
        /// <param name="sensor">The depth sensor to acquire frames from.</param>
        public void Run(SensorCalibration calibration, IDepthSensor sensor)
        {
            this.calibration = calibration;
            this.sensor = sensor;

            int depthWidth = calibration.DepthImageWidth;
            int depthHeight = calibration.DepthImageHeight;

            depthImage = new ShortImage(depthWidth, depthHeight);

            // Poll for a single depth frame and demonstrate coordinate mapping
            byte[] depthBytes = sensor.AcquireDepthFrame();
            if (depthBytes == null)
            {
                Console.WriteLine("Failed to acquire depth frame.");
                return;
            }

            Marshal.Copy(depthBytes, 0, depthImage.DataIntPtr, depthWidth * depthHeight * 2);

            // Convert depth image coords to color image coords
            int x = 100, y = 100;
            ushort depthImageValue = depthImage[x, y]; // depth image values are in mm

            if (depthImageValue == 0)
            {
                Console.WriteLine("Sorry, depth value at input coordinates is zero");
                return;
            }

            float depth = (float)depthImageValue / 1000f; // convert to m

            // Use calibration to map depth pixel to color pixel
            // Note: if calibration is a Kinect2Calibration, the same DepthImageToColorImage
            // method is available on the subclass.
            if (calibration is Kinect2Calibration kinect2Cal)
            {
                double colorX, colorY;
                kinect2Cal.DepthImageToColorImage(x, y, depth, out colorX, out colorY);
                Console.WriteLine("our color coordinates: {0} {1}", colorX, colorY);

                // Convert back to depth image
                Matrix depthPoint;
                double depthX, depthY;
                kinect2Cal.ColorImageToDepthImage(colorX, colorY, depthImage, out depthPoint, out depthX, out depthY);
                Console.WriteLine("convert back to depth: {0} {1}", depthX, depthY);
            }
            else
            {
                Console.WriteLine("Coordinate mapping demo requires Kinect2Calibration (or equivalent) for DepthImageToColorImage.");
            }
        }

        ShortImage depthImage;
        SensorCalibration calibration;
        IDepthSensor sensor;
    }
}
