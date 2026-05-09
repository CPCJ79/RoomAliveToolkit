using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using RoomAlive.Grpc;
using RoomAliveToolkit;

namespace RoomAliveToolkit
{
    /// <summary>
    /// A singleton which polls the IDepthSensor on background threads
    /// and distributes frames to per-client wait handles.
    /// </summary>
    public class KinectHandler : IDisposable
    {
        public static KinectHandler instance;

        private readonly IDepthSensor sensor;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        // Depth
        public byte[] depthByteBuffer;
        public readonly List<AutoResetEvent> depthFrameReady = new List<AutoResetEvent>();

        // Color YUV
        public byte[] yuvByteBuffer;
        public readonly List<AutoResetEvent> yuvFrameReady = new List<AutoResetEvent>();

        // Color RGB (BGRA32)
        public byte[] rgbByteBuffer;
        public readonly List<AutoResetEvent> rgbFrameReady = new List<AutoResetEvent>();

        // JPEG
        public byte[] jpegByteBuffer;
        public int nJpegBytes = 0;
        public readonly List<AutoResetEvent> jpegFrameReady = new List<AutoResetEvent>();

        // Audio (not provided by IDepthSensor; left as placeholder)
        public readonly List<AutoResetEvent> audioFrameReady = new List<AutoResetEvent>();
        public readonly List<Queue<byte[]>> audioFrameQueues = new List<Queue<byte[]>>();

        // Exposure metadata
        public float lastColorGain;
        public long lastColorExposureTimeTicks;

        // Calibration
        public SensorCalibration sensorCalibration;

        private readonly Stopwatch stopWatch = new Stopwatch();

        public KinectHandler(IDepthSensor sensor)
        {
            instance = this;
            this.sensor = sensor;

            sensorCalibration = sensor.Calibration;

            int depthPixels = sensorCalibration.DepthImageWidth * sensorCalibration.DepthImageHeight;
            int colorPixels = sensorCalibration.ColorImageWidth * sensorCalibration.ColorImageHeight;

            depthByteBuffer = new byte[depthPixels * 2];
            yuvByteBuffer = new byte[colorPixels * 2];
            rgbByteBuffer = new byte[colorPixels * 4];
            jpegByteBuffer = new byte[colorPixels * 4];

            // Start background polling threads
            var depthThread = new Thread(DepthPollingLoop) { IsBackground = true, Name = "DepthPoll" };
            depthThread.Start();

            var colorThread = new Thread(ColorPollingLoop) { IsBackground = true, Name = "ColorPoll" };
            colorThread.Start();
        }

        private void DepthPollingLoop()
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    byte[] frame = sensor.AcquireDepthFrame();
                    if (frame != null && depthFrameReady.Count > 0)
                    {
                        lock (depthByteBuffer)
                            Buffer.BlockCopy(frame, 0, depthByteBuffer, 0, frame.Length);
                        lock (depthFrameReady)
                            foreach (var evt in depthFrameReady)
                                evt.Set();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("DepthPoll error: " + ex.Message);
                    Thread.Sleep(100);
                }
            }
        }

        private void ColorPollingLoop()
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    bool needYuv = yuvFrameReady.Count > 0;
                    bool needRgb = rgbFrameReady.Count > 0;
                    bool needJpeg = jpegFrameReady.Count > 0;

                    if (!needYuv && !needRgb && !needJpeg)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    // Update exposure metadata
                    lastColorGain = sensor.LastColorGain;
                    lastColorExposureTimeTicks = sensor.LastColorExposureTimeTicks;

                    if (needYuv)
                    {
                        byte[] yuvFrame = sensor.AcquireColorFrameYUV();
                        if (yuvFrame != null)
                        {
                            lock (yuvByteBuffer)
                                Buffer.BlockCopy(yuvFrame, 0, yuvByteBuffer, 0, yuvFrame.Length);
                            lock (yuvFrameReady)
                                foreach (var evt in yuvFrameReady)
                                    evt.Set();
                        }
                    }

                    if (needRgb || needJpeg)
                    {
                        byte[] rgbFrame = sensor.AcquireColorFrameRGB();
                        if (rgbFrame != null)
                        {
                            lock (rgbByteBuffer)
                                Buffer.BlockCopy(rgbFrame, 0, rgbByteBuffer, 0, rgbFrame.Length);
                            lock (rgbFrameReady)
                                foreach (var evt in rgbFrameReady)
                                    evt.Set();

                            if (needJpeg)
                            {
                                EncodeJpeg(rgbFrame);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ColorPoll error: " + ex.Message);
                    Thread.Sleep(100);
                }
            }
        }

        private void EncodeJpeg(byte[] bgraData)
        {
            stopWatch.Restart();

            int width = sensorCalibration.ColorImageWidth;
            int height = sensorCalibration.ColorImageHeight;

            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                var bitmapData = bitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);

                Marshal.Copy(bgraData, 0, bitmapData.Scan0, width * height * 4);
                bitmap.UnlockBits(bitmapData);

                using (var memoryStream = new MemoryStream())
                {
                    // Get JPEG codec
                    var jpegCodec = GetJpegCodec();
                    if (jpegCodec != null)
                    {
                        var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 50L);
                        bitmap.Save(memoryStream, jpegCodec, encoderParams);
                    }
                    else
                    {
                        bitmap.Save(memoryStream, ImageFormat.Jpeg);
                    }

                    lock (jpegByteBuffer)
                    {
                        nJpegBytes = (int)memoryStream.Length;
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        memoryStream.Read(jpegByteBuffer, 0, nJpegBytes);
                    }

                    lock (jpegFrameReady)
                        foreach (var evt in jpegFrameReady)
                            evt.Set();
                }
            }
        }

        private static ImageCodecInfo GetJpegCodec()
        {
            foreach (var codec in ImageCodecInfo.GetImageEncoders())
            {
                if (codec.MimeType == "image/jpeg")
                    return codec;
            }
            return null;
        }

        public void Dispose()
        {
            cts.Cancel();
            sensor?.Dispose();
        }
    }

    /// <summary>
    /// gRPC service implementation. A new instance is created per call (transient),
    /// but all instances share the singleton KinectHandler for frame data.
    /// For streaming scenarios, per-client wait handles are registered/unregistered
    /// around each call.
    /// </summary>
    public class KinectServer2Service : RoomAlive.Grpc.KinectServer2.KinectServer2Base
    {
        public override Task<ImageReply> LatestDepthImage(Empty request, ServerCallContext context)
        {
            var evt = new AutoResetEvent(false);
            lock (KinectHandler.instance.depthFrameReady)
                KinectHandler.instance.depthFrameReady.Add(evt);
            try
            {
                evt.WaitOne();
                byte[] copy;
                lock (KinectHandler.instance.depthByteBuffer)
                {
                    copy = new byte[KinectHandler.instance.depthByteBuffer.Length];
                    Buffer.BlockCopy(KinectHandler.instance.depthByteBuffer, 0, copy, 0, copy.Length);
                }
                return Task.FromResult(new ImageReply { Data = ByteString.CopyFrom(copy) });
            }
            finally
            {
                lock (KinectHandler.instance.depthFrameReady)
                    KinectHandler.instance.depthFrameReady.Remove(evt);
            }
        }

        public override Task<ImageReply> LatestYUVImage(Empty request, ServerCallContext context)
        {
            var evt = new AutoResetEvent(false);
            lock (KinectHandler.instance.yuvFrameReady)
                KinectHandler.instance.yuvFrameReady.Add(evt);
            try
            {
                evt.WaitOne();
                byte[] copy;
                lock (KinectHandler.instance.yuvByteBuffer)
                {
                    copy = new byte[KinectHandler.instance.yuvByteBuffer.Length];
                    Buffer.BlockCopy(KinectHandler.instance.yuvByteBuffer, 0, copy, 0, copy.Length);
                }
                return Task.FromResult(new ImageReply { Data = ByteString.CopyFrom(copy) });
            }
            finally
            {
                lock (KinectHandler.instance.yuvFrameReady)
                    KinectHandler.instance.yuvFrameReady.Remove(evt);
            }
        }

        public override Task<ImageReply> LatestRGBImage(Empty request, ServerCallContext context)
        {
            var evt = new AutoResetEvent(false);
            lock (KinectHandler.instance.rgbFrameReady)
                KinectHandler.instance.rgbFrameReady.Add(evt);
            try
            {
                evt.WaitOne();
                byte[] copy;
                lock (KinectHandler.instance.rgbByteBuffer)
                {
                    copy = new byte[KinectHandler.instance.rgbByteBuffer.Length];
                    Buffer.BlockCopy(KinectHandler.instance.rgbByteBuffer, 0, copy, 0, copy.Length);
                }
                return Task.FromResult(new ImageReply { Data = ByteString.CopyFrom(copy) });
            }
            finally
            {
                lock (KinectHandler.instance.rgbFrameReady)
                    KinectHandler.instance.rgbFrameReady.Remove(evt);
            }
        }

        public override Task<ImageReply> LatestJPEGImage(Empty request, ServerCallContext context)
        {
            var evt = new AutoResetEvent(false);
            lock (KinectHandler.instance.jpegFrameReady)
                KinectHandler.instance.jpegFrameReady.Add(evt);
            try
            {
                evt.WaitOne();
                byte[] copy;
                lock (KinectHandler.instance.jpegByteBuffer)
                {
                    copy = new byte[KinectHandler.instance.nJpegBytes];
                    Buffer.BlockCopy(KinectHandler.instance.jpegByteBuffer, 0, copy, 0, KinectHandler.instance.nJpegBytes);
                }
                return Task.FromResult(new ImageReply { Data = ByteString.CopyFrom(copy) });
            }
            finally
            {
                lock (KinectHandler.instance.jpegFrameReady)
                    KinectHandler.instance.jpegFrameReady.Remove(evt);
            }
        }

        public override Task<ColorGainReply> LastColorGain(Empty request, ServerCallContext context)
        {
            return Task.FromResult(new ColorGainReply { Gain = KinectHandler.instance.lastColorGain });
        }

        public override Task<ExposureTimeTicksReply> LastColorExposureTimeTicks(Empty request, ServerCallContext context)
        {
            return Task.FromResult(new ExposureTimeTicksReply { Ticks = KinectHandler.instance.lastColorExposureTimeTicks });
        }

        public override Task<Kinect2CalibrationData> GetCalibration(Empty request, ServerCallContext context)
        {
            var cal = KinectHandler.instance.sensorCalibration;

            var reply = new Kinect2CalibrationData();

            if (cal.ColorCameraMatrix != null)
                reply.ColorCameraMatrix = ByteString.CopyFrom(MatrixToBytes(cal.ColorCameraMatrix));
            if (cal.ColorLensDistortion != null)
                reply.ColorLensDistortion = ByteString.CopyFrom(MatrixToBytes(cal.ColorLensDistortion));
            if (cal.DepthCameraMatrix != null)
                reply.DepthCameraMatrix = ByteString.CopyFrom(MatrixToBytes(cal.DepthCameraMatrix));
            if (cal.DepthLensDistortion != null)
                reply.DepthLensDistortion = ByteString.CopyFrom(MatrixToBytes(cal.DepthLensDistortion));
            if (cal.DepthToColorTransform != null)
                reply.DepthToColorTransform = ByteString.CopyFrom(MatrixToBytes(cal.DepthToColorTransform));

            return Task.FromResult(reply);
        }

        private static byte[] MatrixToBytes(Matrix m)
        {
            int rows = m.Rows;
            int cols = m.Cols;
            var bytes = new byte[rows * cols * sizeof(double)];
            int offset = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var val = BitConverter.GetBytes(m[r, c]);
                    Buffer.BlockCopy(val, 0, bytes, offset, sizeof(double));
                    offset += sizeof(double);
                }
            return bytes;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            // TODO: Replace with your actual IDepthSensor implementation, e.g.:
            //   IDepthSensor sensor = new Kinect2Sensor();
            //   IDepthSensor sensor = new OrbbecSensor();
            //   IDepthSensor sensor = new AzureKinectSensor();
            IDepthSensor sensor = CreateSensor(args);

            using (var handler = new KinectHandler(sensor))
            {
                var builder = WebApplication.CreateBuilder(args);
                builder.Services.AddGrpc();

                var app = builder.Build();
                app.MapGrpcService<KinectServer2Service>();

                app.Urls.Add("http://0.0.0.0:9000");

                Console.WriteLine("KinectServer2 gRPC service listening on port 9000. Press Ctrl+C to stop.");
                app.Run();
            }
        }

        private static IDepthSensor CreateSensor(string[] args)
        {
            // Placeholder: in production, select the sensor implementation based on
            // command-line arguments or configuration.
            throw new NotImplementedException(
                "Provide an IDepthSensor implementation. " +
                "For example, pass a Kinect2Sensor, OrbbecSensor, or AzureKinectSensor instance.");
        }
    }
}
